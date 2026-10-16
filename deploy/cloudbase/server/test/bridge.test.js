'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { once } = require('node:events');
const { Bridge, RETAIN_MS } = require('../bridge');
const { createHandler, createEventMain, createHttpServer } = require('../index');
const { hash } = require('../auth');
const { route } = require('../routes');
const { MemoryStore } = require('./memory-store');
const BROWSER = 'test_browser_abcdefghijklmnopqrstuvwxyz';
const AGENT = 'test_agent_abcdefghijklmnopqrstuvwxyz0123';
const policy = { browserHash: hash(BROWSER), agentHash: hash(AGENT) };
function fixture(options = {}) {
  const store = new MemoryStore();
  const bridge = new Bridge(store, { pollMs: 0, ...options });
  const handler = createHandler({ policy, bridge });
  const call = (method, path, body, token = BROWSER, extra = {}) => handler({ method, path,
    headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json', ...extra },
    body: body === undefined ? Buffer.alloc(0) : Buffer.from(JSON.stringify(body)) });
  return { store, bridge, handler, call };
}
const code = response => JSON.parse(response.body).code;

test('public health reveals no environment or policy and authentication roles are separate', async () => {
  const f = fixture();
  const health = await f.handler({ method: 'GET', path: '/health' });
  assert.equal(health.statusCode, 200); assert.deepEqual(Object.keys(JSON.parse(health.body)), ['ok', 'service', 'version']);
  assert.equal((await f.handler({ method: 'GET', path: '/api/cloud/status' })).statusCode, 401);
  assert.equal((await f.call('GET', '/api/cloud/status', undefined, AGENT)).statusCode, 403);
  assert.equal((await f.call('POST', '/bridge/poll', { agentId: 'test' })).statusCode, 403);
});
test('off-line node fails immediately and queues no command', async () => {
  const f = fixture(); const result = await f.call('GET', '/api/status');
  assert.equal(result.statusCode, 503); assert.equal(code(result), 'NODE_OFFLINE');
  assert.equal(f.store.documents.size, 0);
});
test('arbitrary URLs, shell paths, service stop and arbitrary worker arguments are refused', async () => {
  const f = fixture();
  for (const path of ['/api/service/stop', '/api/sdk/host/stop', '/api/shell', '/api/status?url=http://evil', '//evil/api/status', '/api/%73tatus', '/api/status/../config']) {
    const result = await f.call(path.includes('stop') || path.includes('shell') ? 'POST' : 'GET', path, undefined);
    assert.ok([400, 404].includes(result.statusCode), path);
  }
  const result = await f.call('POST', '/api/task/start', { task: 'replica_like', args: ['arbitrary.py'] });
  assert.equal(result.statusCode, 400); assert.equal(f.store.documents.size, 0);
  assert.equal((await f.call('POST', '/api/sdk/plugins/run', { id: '../untrusted' })).statusCode, 400);
});
test('body limits and request encodings are checked before database use', async () => {
  const f = fixture();
  assert.equal((await f.call('POST', '/api/config', { value: 'x'.repeat(65536) })).statusCode, 413);
  assert.equal((await f.call('POST', '/api/config', {}, BROWSER, { 'content-encoding': 'gzip' })).statusCode, 415);
  assert.equal((await f.handler({ method: 'POST', path: '/api/config', headers: { authorization: 'Bearer ' + BROWSER, 'content-type': 'application/json' }, body: Buffer.from('{bad') })).statusCode, 400);
});
test('write requests require a stable idempotency key and concurrent repeats create one record', async () => {
  const f = fixture(); await f.bridge.heartbeat('desktop');
  assert.equal((await f.call('POST', '/api/config', {})).statusCode, 400);
  const endpoint = route('POST', '/api/config');
  const commands = await Promise.all(Array.from({ length: 30 }, () => f.bridge.enqueue('POST', endpoint, { a: 1 }, 'shared-key-0123456789')));
  assert.equal(new Set(commands.map(c => c._id)).size, 1);
  assert.equal((await f.store.queued()).length, 1);
  await assert.rejects(f.bridge.enqueue('POST', endpoint, { a: 2 }, 'shared-key-0123456789'), e => e.code === 'IDEMPOTENCY_CONFLICT');
});
test('concurrent agent consumers atomically claim a command at most once and remove request secrets', async () => {
  const f = fixture(); await f.bridge.heartbeat('desktop');
  const queued = await f.bridge.enqueue('POST', route('POST', '/api/config'), { privateValue: 'test-secret' }, 'one-command-0123456789');
  const claims = await Promise.all(Array.from({ length: 32 }, () => f.bridge.claim('desktop')));
  assert.equal(claims.filter(Boolean).length, 1);
  const command = claims.find(Boolean); assert.equal(command.id, queued._id);
  assert.equal(command.body.privateValue, 'test-secret');
  assert.equal((await f.store.get(command.id)).requestBody, '');
  assert.equal(await f.bridge.claim('desktop'), null);
});
test('result requires a claimed command and matching claim binding; duplicate results are idempotent', async () => {
  const f = fixture(); await f.bridge.heartbeat('desktop');
  const queued = await f.bridge.enqueue('GET', route('GET', '/api/status'), null);
  await assert.rejects(f.bridge.complete({ id: queued._id, status: 200, body: { ok: true } }), e => e.code === 'COMMAND_NOT_CLAIMED');
  const command = await f.bridge.claim('desktop');
  await assert.rejects(f.bridge.complete({ id: command.id, status: 200, body: { ok: true }, claimToken: 'wrong' }), e => e.code === 'CLAIM_MISMATCH');
  const input = { id: command.id, status: 200, body: { ok: true, result: 'done' }, agentId: 'desktop', claimToken: command.claimToken };
  assert.equal((await f.bridge.complete(input)).ok, true);
  assert.equal((await f.bridge.complete(input)).duplicate, true);
  await assert.rejects(f.bridge.complete({ ...input, body: { ok: true, result: 'different' } }), e => e.code === 'RESULT_CONFLICT');
  assert.deepEqual(await f.bridge.wait(command.id), { status: 200, body: input.body });
});
test('expired unclaimed commands never execute; claimed timeout remains uncertain and accepts a late result', async () => {
  let now = Date.now(); const f = fixture({ clock: () => now, shortMs: 10 });
  await f.bridge.heartbeat('desktop');
  const first = await f.bridge.enqueue('POST', route('POST', '/api/config'), {}, 'expiry-test-0123456789');
  now += 11; assert.equal(await f.bridge.claim('desktop'), null);
  await assert.rejects(f.bridge.wait(first._id), e => e.code === 'COMMAND_EXPIRED');
  const second = await f.bridge.enqueue('POST', route('POST', '/api/config'), {}, 'uncertain-test-0123456789');
  const command = await f.bridge.claim('desktop'); now += 11;
  await assert.rejects(f.bridge.wait(second._id), e => e.code === 'EXECUTION_UNCERTAIN');
  assert.equal(await f.bridge.claim('desktop'), null);
  assert.equal((await f.bridge.complete({ id: command.id, status: 200, body: { ok: true } })).late, true);
});
test('large result is rejected, payload cleanup retains dedupe tombstone and TTL removes it later', async () => {
  let now = Date.now(); const f = fixture({ clock: () => now }); await f.bridge.heartbeat('desktop');
  const queued = await f.bridge.enqueue('GET', route('GET', '/api/status'), null);
  const command = await f.bridge.claim('desktop');
  await assert.rejects(f.bridge.complete({ id: command.id, status: 200, body: { text: 'x'.repeat(524288) } }), e => e.code === 'RESULT_TOO_LARGE');
  await f.bridge.complete({ id: command.id, status: 200, body: { ok: true } });
  now += 121000; await f.bridge.cleanup();
  assert.equal((await f.store.get(queued._id)).responseBody, '');
  await assert.rejects(f.bridge.wait(queued._id), e => e.code === 'RESULT_EXPIRED');
  now += RETAIN_MS; await f.bridge.cleanup(); assert.equal(await f.store.get(queued._id), null);
});
test('only one live Windows agentId is accepted; another may connect after offline timeout', async () => {
  let now = Date.now(); const f = fixture({ clock: () => now });
  await f.bridge.heartbeat('desktop-a');
  await assert.rejects(f.bridge.heartbeat('desktop-b'), e => e.code === 'NODE_ALREADY_CONNECTED');
  now += 45001; assert.equal((await f.bridge.nodeStatus()).nodeOnline, false);
  await f.bridge.heartbeat('desktop-b'); assert.equal((await f.bridge.nodeStatus()).agentId, 'desktop-b');
});
test('ordinary event adapter handles HTTP gateway prefix, headers, query object and base64 body', async () => {
  const f = fixture(); const main = createEventMain(f.handler);
  const result = await main({ httpMethod: 'GET', path: '/wechat-workspace-api/api/cloud/status', headers: { Authorization: 'Bearer ' + BROWSER } });
  assert.equal(result.statusCode, 200); assert.equal(JSON.parse(result.body).nodeOnline, false);
  const logs = await main({ httpMethod: 'GET', path: '/wechat-workspace-api/api/logs', queryStringParameters: { task: 'replica_like', n: '50' }, headers: { Authorization: 'Bearer ' + BROWSER } });
  assert.equal(code(logs), 'NODE_OFFLINE');
  const poll = await main({ httpMethod: 'POST', path: '/wechat-workspace-api/bridge/poll', isBase64Encoded: true,
    body: Buffer.from(JSON.stringify({ agentId: 'desktop' })).toString('base64'), headers: { Authorization: 'Bearer ' + AGENT, 'Content-Type': 'application/json' } });
  assert.deepEqual(JSON.parse(poll.body), { ok: true, command: null });
});
test('exact public origin supports CORS preflight; all other origins are denied', async () => {
  const origin = 'https://example.tcloudbaseapp.com'; const f = fixture();
  const handler = createHandler({ policy, bridge: f.bridge, publicOrigin: origin });
  const preflight = await handler({ method: 'OPTIONS', path: '/wechat-workspace-api/api/config', headers: { Origin: origin, 'Sec-Fetch-Site': 'cross-site', 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'authorization,content-type,x-idempotency-key' } });
  assert.equal(preflight.statusCode, 204); assert.equal(preflight.headers['Access-Control-Allow-Origin'], origin); assert.equal(preflight.headers.Vary, 'Origin');
  const result = await handler({ method: 'GET', path: '/api/cloud/status', headers: { Origin: origin, 'Sec-Fetch-Site': 'cross-site', Authorization: 'Bearer ' + BROWSER } });
  assert.equal(result.statusCode, 200); assert.equal(result.headers['Access-Control-Allow-Origin'], origin);
  const forbidden = await handler({ method: 'GET', path: '/api/cloud/status', headers: { Origin: 'https://evil.invalid', Authorization: 'Bearer ' + BROWSER } });
  assert.equal(forbidden.statusCode, 403); assert.equal(forbidden.headers['Access-Control-Allow-Origin'], undefined);
});
test('full local HTTP transfer keeps Python API status/body and completes without invoking WeChat', async () => {
  const f = fixture({ waitStep: 1 }); await f.bridge.heartbeat('desktop');
  const server = createHttpServer(f.handler); server.listen(0, '127.0.0.1'); await once(server, 'listening');
  const base = 'http://127.0.0.1:' + server.address().port;
  try {
    const browser = fetch(base + '/api/status', { headers: { Authorization: 'Bearer ' + BROWSER } });
    let command;
    for (let attempt = 0; attempt < 100 && !command; attempt++) { command = await f.bridge.claim('desktop'); if (!command) await new Promise(r => setTimeout(r, 2)); }
    assert.ok(command);
    await f.bridge.complete({ id: command.id, status: 200, body: { ok: true, app: 'fixture', tasks: [] } });
    const result = await browser; assert.equal(result.status, 200); assert.equal((await result.json()).app, 'fixture');
  } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
});
