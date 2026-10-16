'use strict';
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { Bridge } = require('./bridge');
const { createPostgresStore } = require('./store-postgres');
const { loadPolicy, validatePolicy, authorize } = require('./auth');
const { BridgeError, normalPath, route, validateBody } = require('./routes');
const { diagnostic } = require('./diagnostics');
const MAX_REQUEST = 64 * 1024;
const MAX_RESULT_REQUEST = 512 * 1024 + 4096;
const SECURITY_HEADERS = {
  'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff', 'X-Frame-Options': 'DENY', 'Referrer-Policy': 'no-referrer',
  'Content-Security-Policy': "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'"
};
function response(status, body, extra = {}) {
  return { statusCode: status, headers: { ...SECURITY_HEADERS, ...extra }, body: typeof body === 'string' ? body : JSON.stringify(body), isBase64Encoded: false };
}
function lowerHeaders(input) {
  const headers = {};
  for (const [name, value] of Object.entries(input || {})) headers[name.toLowerCase()] = Array.isArray(value) ? value.join(',') : String(value);
  return headers;
}
function parseBody(buffer) {
  if (!buffer.length) return {};
  try { return JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(buffer)); }
  catch { throw new BridgeError(400, '请求正文必须是 UTF-8 JSON 对象'); }
}
function browserOrigin(headers, publicOrigin) {
  if (!headers.origin) return;
  let origin;
  try { origin = new URL(headers.origin); } catch { throw new BridgeError(403, '请求来源无效', 'ORIGIN_FORBIDDEN'); }
  if (publicOrigin && origin.origin === publicOrigin) return;
  if (publicOrigin) throw new BridgeError(403, '请求来源不匹配', 'ORIGIN_FORBIDDEN');
  if (headers['sec-fetch-site'] === 'cross-site') throw new BridgeError(403, '不接受跨站请求', 'ORIGIN_FORBIDDEN');
  if (!headers.host || origin.host !== headers.host || !['https:', 'http:'].includes(origin.protocol)) throw new BridgeError(403, '请求来源不匹配', 'ORIGIN_FORBIDDEN');
}
function createHandler(options) {
  const policy = validatePolicy(options.policy);
  const bridge = options.bridge || new Bridge(options.store, options.bridgeOptions);
  const publicDir = options.publicDir || path.resolve(__dirname, '../public');
  return async input => {
    let cors = {};
    let stage = 'handler';
    const reply = (status, body, extra = {}) => response(status, body, { ...cors, ...extra });
    try {
      const method = String(input.method || '').toUpperCase();
      const rawPath = input.path || '/';
      const parsed = normalPath(rawPath);
      const headers = lowerHeaders(input.headers);
      browserOrigin(headers, options.publicOrigin);
      if (headers.origin && options.publicOrigin === headers.origin) cors = { 'Access-Control-Allow-Origin': options.publicOrigin, Vary: 'Origin' };
      if (method === 'OPTIONS') {
        const requestedHeaders = (headers['access-control-request-headers'] || '').toLowerCase().split(',').map(h => h.trim()).filter(Boolean);
        if (!cors['Access-Control-Allow-Origin'] || !['GET', 'POST'].includes(headers['access-control-request-method']) ||
            requestedHeaders.some(h => !['authorization', 'content-type', 'x-idempotency-key'].includes(h))) throw new BridgeError(403, '跨域预检不被允许', 'ORIGIN_FORBIDDEN');
        return reply(204, '', { 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
          'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Idempotency-Key', 'Access-Control-Max-Age': '600' });
      }
      const bodyBuffer = Buffer.isBuffer(input.body) ? input.body : Buffer.from(input.body || '');
      if (method === 'GET' && parsed.pathname === '/health' && !parsed.search) {
        return reply(200, { ok: true, service: 'wechat-workspace-bridge', version: '1.1.0' });
      }
      const assets = { '/': ['index.html', 'text/html'], '/cloud.css': ['cloud.css', 'text/css'],
        '/cloud-config.js': ['cloud-config.js', 'application/javascript'], '/cloud-bootstrap.js': ['cloud-bootstrap.js', 'application/javascript'] };
      if ((method === 'GET' || method === 'HEAD') && Object.hasOwn(assets, parsed.pathname) && !parsed.search) {
        const [name, mime] = assets[parsed.pathname];
        const filename = path.join(publicDir, name);
        if (!fs.existsSync(filename)) return reply(503, { ok: false, message: '网页入口尚未准备完成', code: 'PAGE_UNAVAILABLE' });
        return reply(200, method === 'HEAD' ? '' : fs.readFileSync(filename, 'utf8'), { 'Content-Type': mime + '; charset=utf-8' });
      }
      const agentRoute = method === 'POST' && ['/bridge/poll', '/bridge/result'].includes(parsed.pathname) && !parsed.search;
      if (parsed.pathname.startsWith('/bridge/') && !agentRoute) throw new BridgeError(404, '此接口未开放', 'ROUTE_NOT_ALLOWED');
      authorize(headers, agentRoute ? 'agent' : 'browser', policy);
      if (!agentRoute) browserOrigin(headers, options.publicOrigin);
      const limit = agentRoute && parsed.pathname === '/bridge/result' ? MAX_RESULT_REQUEST : MAX_REQUEST;
      if (bodyBuffer.length > limit) throw new BridgeError(413, '请求内容过大', 'BODY_TOO_LARGE');
      if (headers['content-encoding'] && headers['content-encoding'] !== 'identity') throw new BridgeError(415, '不接受压缩请求正文');
      if (method === 'GET' && bodyBuffer.length) throw new BridgeError(400, 'GET 请求不接受正文');
      if (method === 'POST' && bodyBuffer.length && !(headers['content-type'] || '').toLowerCase().startsWith('application/json')) {
        throw new BridgeError(415, '请使用 application/json');
      }
      if (method === 'GET' && parsed.pathname === '/api/cloud/status' && !parsed.search) { stage = 'browser_node_status'; return reply(200, await bridge.nodeStatus()); }
      if (agentRoute) {
        const body = parseBody(bodyBuffer);
        if (parsed.pathname === '/bridge/poll') { stage = 'agent_poll'; return reply(200, await bridge.poll(body.agentId)); }
        stage = 'agent_result';
        return reply(200, await bridge.complete(body));
      }
      const endpoint = route(method, rawPath);
      const body = method === 'POST' ? parseBody(bodyBuffer) : null;
      if (method === 'POST') validateBody(endpoint.pathname, body);
      stage = 'browser_enqueue';
      const command = await bridge.enqueue(method, endpoint, body, headers['x-idempotency-key']);
      stage = 'browser_wait';
      const result = await bridge.wait(command._id);
      return reply(result.status, result.body);
    } catch (error) {
      if (error instanceof BridgeError) return reply(error.status, { ok: false, message: error.message, code: error.code });
      diagnostic(error, stage, options.diagnostic || console.error);
      // Deliberately do not serialize or log upstream errors: they may include
      // SQL payloads containing AI keys or contacts from an in-flight command.
      return reply(503, { ok: false, message: '云桥接存储暂不可用，请稍后检查节点状态；写操作请保留原请求标识', code: 'BRIDGE_UNAVAILABLE' });
    }
  };
}
function eventRequest(event) {
  const method = event.httpMethod || event.requestContext?.http?.method || 'GET';
  let requestPath = event.rawPath || event.path || '/';
  let query = event.rawQueryString;
  if (!query && event.queryStringParameters && typeof event.queryStringParameters === 'object') query = new URLSearchParams(event.queryStringParameters).toString();
  if (query && !requestPath.includes('?')) requestPath += '?' + query;
  let body = event.body || '';
  if (typeof body !== 'string') body = JSON.stringify(body);
  return { method, path: requestPath, headers: event.headers || {}, body: Buffer.from(body, event.isBase64Encoded ? 'base64' : 'utf8') };
}
function createEventMain(handler) {
  return async event => handler(eventRequest(typeof event === 'string' ? JSON.parse(event) : event));
}
let productionHandler;
function production(context) {
  if (!productionHandler) {
    const env = process.env.CLOUDBASE_ENV_ID || process.env.TCB_ENV || process.env.SCF_NAMESPACE;
    const policy = loadPolicy(process.env.ACCESS_POLICY_PATH || path.join(__dirname, 'access-policy.json'));
    productionHandler = createHandler({ policy, store: createPostgresStore(env, context),
      publicDir: process.env.PUBLIC_DIR || path.join(__dirname, 'public'), publicOrigin: process.env.PUBLIC_ORIGIN });
  }
  return productionHandler;
}
async function main(event, context) {
  try { return await production(context)(eventRequest(event)); }
  catch (error) { diagnostic(error, 'configuration'); return response(503, { ok: false, message: '云桥接尚未完成服务器配置', code: 'BRIDGE_NOT_CONFIGURED' }); }
}
function createHttpServer(handler) {
  return http.createServer(async (req, res) => {
    let size = 0; const chunks = [];
    const resultPath = /^\/(?:wechat-workspace-api\/)?bridge\/result(?:\?|$)/.test(req.url || '');
    const limit = resultPath ? MAX_RESULT_REQUEST : MAX_REQUEST;
    try {
      for await (const chunk of req) {
        size += chunk.length;
        if (size > limit) { const result = response(413, { ok: false, message: '请求内容过大', code: 'BODY_TOO_LARGE' }); res.writeHead(result.statusCode, { ...result.headers, Connection: 'close' }); res.end(result.body); req.resume(); return; }
        chunks.push(chunk);
      }
      const result = await handler({ method: req.method, path: req.url, headers: req.headers, body: Buffer.concat(chunks) });
      res.writeHead(result.statusCode, result.headers); res.end(result.body);
    } catch { if (!res.headersSent) { res.writeHead(400, SECURITY_HEADERS); res.end(JSON.stringify({ ok: false, message: '请求读取失败' })); } }
  });
}
if (require.main === module) {
  try {
    const port = Number(process.env.PORT || 8080);
    if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid port');
    const server = createHttpServer(production()); server.requestTimeout = 125000;
    server.listen(port, process.env.BIND_HOST || '127.0.0.1', () => process.stdout.write('WeChat workspace bridge listening\n'));
  } catch { process.stderr.write('Bridge startup configuration is incomplete.\n'); process.exitCode = 1; }
}
module.exports = { main, createHandler, createEventMain, eventRequest, createHttpServer, MAX_REQUEST, MAX_RESULT_REQUEST };
