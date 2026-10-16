'use strict';

const TASKS = new Set(['phone_like', 'switch_account', 'pc_like', 'multi_like', 'replica_like']);
const PLUGINS = new Set(['sdk-health', 'session-snapshot', 'sdk-session', 'sdk-contacts']);
const GET = new Set(['/api/status', '/api/config', '/api/devices', '/api/logs',
  '/api/sdk/clients',
  '/api/sdk/status', '/api/sdk/plugins', '/api/sdk/contacts', '/api/sdk/ai/config',
  '/api/sdk/ai/status', '/api/sdk/ai/profiles/export']);
const POST = new Set(['/api/config', '/api/task/start', '/api/task/stop',
  '/api/sdk/clients/open', '/api/sdk/clients/focus', '/api/sdk/clients/alias', '/api/sdk/clients/verify',
  '/api/sdk/start', '/api/sdk/stop', '/api/sdk/login', '/api/sdk/plugins/run',
  '/api/sdk/ai/config', '/api/sdk/ai/preview', '/api/sdk/ai/start',
  '/api/sdk/ai/stop', '/api/sdk/ai/profiles/import']);
const LONG = new Set(['/api/sdk/contacts', '/api/sdk/plugins/run', '/api/sdk/ai/preview', '/api/sdk/clients/verify']);
const PREFIX = '/wechat-workspace-api';

class BridgeError extends Error {
  constructor(status, message, code = 'BAD_REQUEST') { super(message); this.status = status; this.code = code; }
}
function normalPath(raw) {
  if (typeof raw !== 'string' || raw.length > 2048 || !raw.startsWith('/') || raw.startsWith('//') || /[\\#\x00-\x20]/.test(raw)) {
    throw new BridgeError(400, '请求路径无效');
  }
  if (raw === PREFIX) raw = '/';
  else if (raw.startsWith(PREFIX + '/')) raw = raw.slice(PREFIX.length);
  const split = raw.indexOf('?');
  const pathname = split < 0 ? raw : raw.slice(0, split);
  if (pathname.includes('%') || pathname.includes('..') || pathname.includes('//')) throw new BridgeError(400, '请求路径无效');
  const url = new URL(raw, 'http://bridge.invalid');
  return { pathname, query: url.searchParams, search: url.search };
}
function route(method, raw) {
  const parsed = normalPath(raw);
  if (!((method === 'GET' && GET.has(parsed.pathname)) || (method === 'POST' && POST.has(parsed.pathname)))) {
    throw new BridgeError(404, '此接口未开放', 'ROUTE_NOT_ALLOWED');
  }
  if (parsed.pathname === '/api/logs') {
    for (const key of parsed.query.keys()) if (!['task', 'n'].includes(key)) throw new BridgeError(400, '日志查询参数无效');
    if (parsed.query.getAll('task').length !== 1 || parsed.query.getAll('n').length > 1 || !TASKS.has(parsed.query.get('task'))) {
      throw new BridgeError(400, '日志任务无效');
    }
    const n = parsed.query.get('n') || '80';
    if (!/^\d{1,3}$/.test(n) || Number(n) < 1 || Number(n) > 500) throw new BridgeError(400, '日志条数应为 1 至 500');
    return { pathname: parsed.pathname, path: '/api/logs?task=' + parsed.query.get('task') + '&n=' + Number(n), long: false };
  }
  if (parsed.search) throw new BridgeError(400, '此接口不接受查询参数');
  return { pathname: parsed.pathname, path: parsed.pathname, long: LONG.has(parsed.pathname) };
}
function validateBody(path, body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new BridgeError(400, '请求正文应为 JSON 对象');
  if (path.startsWith('/api/sdk/clients/')) {
    const fields = path.endsWith('/alias') ? ['instanceId', 'alias'] : path.endsWith('/focus') ? ['instanceId'] : [];
    if (Object.keys(body).length !== fields.length || Object.keys(body).some(k => !fields.includes(k))) throw new BridgeError(400, '窗口操作参数无效');
    if (fields.includes('instanceId') && (typeof body.instanceId !== 'string' || !/^[1-9][0-9]{0,9}:[1-9][0-9]{10,18}$/.test(body.instanceId))) throw new BridgeError(400, '窗口标识无效');
    if (fields.includes('alias') && (typeof body.alias !== 'string' || body.alias.length > 60 || /\p{Cc}/u.test(body.alias))) throw new BridgeError(400, '备注最多60字且不能含控制字符');
  }
  if (path === '/api/task/start' || path === '/api/task/stop') {
    if (!TASKS.has(body.task) || Object.keys(body).some(k => !['task', 'args'].includes(k)) ||
        (body.args !== undefined && (!Array.isArray(body.args) || body.args.length !== 0))) {
      throw new BridgeError(400, '仅允许固定任务，不允许自定义执行参数');
    }
  }
  if (path === '/api/sdk/plugins/run' && (!PLUGINS.has(body.id) || Object.keys(body).some(k => k !== 'id'))) {
    throw new BridgeError(400, '此插件未开放');
  }
}
module.exports = { BridgeError, normalPath, route, validateBody, TASKS, PLUGINS };
