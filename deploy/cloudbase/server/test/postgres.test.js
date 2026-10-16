'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { PostgresStore, rows, jsonSql, returningSelect } = require('../store-postgres');
const { diagnostic, safeError } = require('../diagnostics');
const { Bridge } = require('../bridge');
const { route } = require('../routes');
const { createHandler } = require('../index');
const { hash } = require('../auth');

class FakePostgres {
  constructor() { this.documents = new Map(); this.queries = []; }
  answer(columns, result) { return { Columns: columns, Rows: result.map(row => JSON.stringify(row)), AffectedRows: result.length }; }
  async ExecutePGSql(input) {
    const original = input.Sql; this.queries.push(original);
    const wrapped = original.startsWith('WITH changed AS (') && original.endsWith(') SELECT id FROM changed');
    const sql = wrapped ? original.slice('WITH changed AS ('.length, -') SELECT id FROM changed'.length) : original;
    const modified = result => wrapped ? this.answer(['id'], result) : { Columns: null, Rows: null, AffectedRows: result.length };
    const id = /(?:WHERE id=|VALUES\()'(agent|cmd_[a-f0-9]{64})'/.exec(sql)?.[1];
    if (sql.startsWith('SELECT id,version,data')) {
      const record = this.documents.get(id);
      const result = this.answer(['id', 'version', 'data'], record ? [[id, String(record.version), JSON.stringify(record.data)]] : []);
      if (!record) result.Rows = null;
      return result;
    }
    if (sql.startsWith('SELECT id,data') && sql.includes("data->>'state'='queued'")) {
      const values = [...this.documents.entries()].filter(([_, r]) => r.data.state === 'queued').sort((a, b) => a[1].data.createdAt - b[1].data.createdAt);
      return this.answer(['id', 'data'], values.map(([key, r]) => [key, JSON.stringify(r.data)]));
    }
    const encoded = /decode\('([a-f0-9]*)','hex'\)/.exec(sql)?.[1];
    if (sql.startsWith('INSERT INTO')) {
      if (this.documents.has(id)) return modified([]);
      this.documents.set(id, { version: 1, data: JSON.parse(Buffer.from(encoded, 'hex').toString('utf8')) });
      return modified([[id]]);
    }
    if (sql.startsWith('UPDATE ')) {
      const version = Number(/AND version=(\d+)/.exec(sql)?.[1]);
      const record = this.documents.get(id);
      if (!record || record.version !== version) return modified([]);
      this.documents.set(id, { version: record.version + 1, data: JSON.parse(Buffer.from(encoded, 'hex').toString('utf8')) });
      return modified([[id]]);
    }
    if (sql.startsWith('DELETE FROM ')) {
      const version = Number(/AND version=(\d+)/.exec(sql)?.[1]);
      const record = this.documents.get(id);
      if (!record || record.version !== version) return modified([]);
      this.documents.delete(id); return modified([[id]]);
    }
    throw new Error('Unexpected fake SQL operation');
  }
}
test('ExecutePGSql official Columns and JSON row strings are parsed strictly', () => {
  assert.deepEqual(rows({ Columns: ['id', 'data'], Rows: ['["agent","{\\"agentId\\":\\"desktop\\"}"]'] }), [{ id: 'agent', data: '{"agentId":"desktop"}' }]);
  assert.throws(() => rows({ Columns: ['id'], Rows: ['["a","b"]'] }));
  assert.deepEqual(rows({ Columns: null, Rows: null, AffectedRows: 1 }), []);
  assert.deepEqual(rows({ Columns: ['id', 'version'], Rows: null }), []);
  assert.throws(() => rows({ Columns: null, Rows: ['["not-empty"]'] }));
});
test('SQL carries untrusted JSON solely as hexadecimal UTF-8 and rejects injectable record ids', async () => {
  const payload = { name: "'); DROP TABLE unrelated;--中文", nested: { key: 'private-fixture' } };
  const expression = jsonSql(payload);
  assert.ok(!expression.includes('DROP TABLE')); assert.ok(!expression.includes('private-fixture'));
  const hex = /decode\('([a-f0-9]+)'/.exec(expression)[1];
  assert.deepEqual(JSON.parse(Buffer.from(hex, 'hex').toString('utf8')), payload);
  const store = new PostgresStore(new FakePostgres(), 'test-environment', { interval: 0 });
  await assert.rejects(store.get("agent';DELETE FROM other;--"));
});
test('PostgreSQL CAS persists claims across independent bridge/store instances and consumes once', async () => {
  const database = new FakePostgres();
  const first = new Bridge(new PostgresStore(database, 'test-environment', { interval: 0 }), { pollMs: 0 });
  const second = new Bridge(new PostgresStore(database, 'test-environment', { interval: 0 }), { pollMs: 0 });
  await first.heartbeat('desktop');
  const queued = await first.enqueue('POST', route('POST', '/api/config'), { selected: 'fixture' }, 'postgres-cas-0123456789');
  const results = await Promise.all([first.claim('desktop'), second.claim('desktop'), first.claim('desktop'), second.claim('desktop')]);
  assert.equal(results.filter(Boolean).length, 1);
  const claimed = results.find(Boolean); assert.equal(claimed.id, queued._id);
  assert.equal(await second.claim('desktop'), null);
  const restored = new Bridge(new PostgresStore(database, 'test-environment', { interval: 0 }));
  await restored.complete({ id: claimed.id, status: 200, body: { ok: true, from: 'durable-fixture' } });
  assert.equal((await first.wait(claimed.id)).body.from, 'durable-fixture');
  assert.ok(database.queries.some(sql => /^WITH changed AS \(UPDATE .*AND version=\d+ RETURNING id\) SELECT id FROM changed$/.test(sql)));
});
test('database failures fail closed without revealing upstream SQL or selecting an in-memory queue', async () => {
  const token = 'browser_test_abcdefghijklmnopqrstuvwxyz';
  const store = new PostgresStore({ async ExecutePGSql() { throw new Error('credential-fixture SQL-private-fixture'); } }, 'test-env', { interval: 0 });
  const handler = createHandler({ store, policy: { browserHash: hash(token), agentHash: hash('agent_test_abcdefghijklmnopqrstuvwxyz') } });
  const result = await handler({ method: 'GET', path: '/api/cloud/status', headers: { Authorization: 'Bearer ' + token } });
  assert.equal(result.statusCode, 503); assert.equal(JSON.parse(result.body).code, 'BRIDGE_UNAVAILABLE');
  assert.ok(!result.body.includes('credential-fixture')); assert.ok(!result.body.includes('SQL-private-fixture'));
});
test('installed Tencent TCB SDK exposes the verified ExecutePGSql client without making network calls', () => {
  const { tcb } = require('tencentcloud-sdk-nodejs-tcb');
  assert.equal(typeof tcb.v20180608.Client, 'function');
  assert.equal(typeof tcb.v20180608.Client.prototype.ExecutePGSql, 'function');
});
test('ExecutePGSql suppresses bare DML RETURNING: final SELECT preserves CAS success without retrying a committed write', async () => {
  const database = new FakePostgres();
  const record = jsonSql({ kind: 'agent', agentId: 'desktop', lastSeen: 10 });
  const raw = await database.ExecutePGSql({ Sql: "INSERT INTO wechat_workspace.queue(id,data) VALUES('agent'," + record + ") ON CONFLICT(id) DO NOTHING RETURNING id" });
  assert.deepEqual(raw, { Columns: null, Rows: null, AffectedRows: 1 });
  const selected = await database.ExecutePGSql({ Sql: returningSelect("UPDATE wechat_workspace.queue SET data=" + record + ",version=version+1 WHERE id='agent' AND version=1 RETURNING id") });
  assert.deepEqual(rows(selected), [{ id: 'agent' }]);
  const store = new PostgresStore(database, 'test-env', { interval: 0 });
  const result = await store.atomic('agent', previous => ({ doc: { ...previous, lastSeen: 20 }, result: 'exactly-one-update' }));
  assert.equal(result, 'exactly-one-update');
  assert.equal(database.documents.get('agent').version, 3);
  assert.equal(database.queries.filter(sql => sql.startsWith('WITH changed AS (UPDATE')).length, 2);
  await store.atomic('agent', () => ({ remove: true, result: true }));
  assert.equal(database.documents.has('agent'), false);
});
test('safe diagnostics only serialize allowlisted stage/code, never messages SQL tokens or records', () => {
  const output = [];
  const unsafe = new Error('private-token SELECT secret FROM private-table');
  unsafe.code = 'FailedOperation.PGExecuteSqlError'; unsafe.sql = 'private-sql'; unsafe.safeStage = 'private-stage';
  diagnostic(unsafe, 'browser_wait', text => output.push(text));
  diagnostic(safeError('list_queued', 'PG_RESPONSE_INVALID'), 'handler', text => output.push(text));
  assert.deepEqual(output.map(JSON.parse), [
    { event: 'bridge_error', stage: 'browser_wait', failureCode: 'PG_SQL_FAILED' },
    { event: 'bridge_error', stage: 'list_queued', failureCode: 'PG_RESPONSE_INVALID' }
  ]);
  assert.ok(!output.join('').includes('private'));
});
