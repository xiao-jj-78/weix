'use strict';
// Production persists every operation in the selected CloudBase PostgreSQL.
// ExecutePGSql does not expose SQL bind parameters. All JSON is UTF-8 hex encoded
// and document keys / revision numbers are validated before SQL construction.
const TABLE = 'wechat_workspace.queue';
const { failureCode, safeError } = require('./diagnostics');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
function key(id) {
  if (typeof id !== 'string' || !/^(agent|cmd_[a-f0-9]{64})$/.test(id)) throw safeError('read_record', 'PG_BAD_RECORD_KEY');
  return "'" + id + "'";
}
function integer(value) {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error('Invalid database integer');
  return String(value);
}
function jsonSql(value) {
  const text = JSON.stringify(value);
  if (!text || Buffer.byteLength(text) > 700000) throw new Error('Database record too large');
  return "convert_from(decode('" + Buffer.from(text).toString('hex') + "','hex'),'UTF8')::jsonb";
}
function returningSelect(sql) {
  // ExecutePGSql suppresses RETURNING columns when the top-level statement is
  // a DML command, even when AffectedRows is nonzero. A final SELECT preserves
  // the row-set required to distinguish a successful CAS from a lost race.
  return 'WITH changed AS (' + sql + ') SELECT id FROM changed';
}
function rows(response) {
  if (!response || (response.Rows != null && !Array.isArray(response.Rows))) throw safeError('query', 'PG_RESPONSE_INVALID');
  // ExecutePGSql may report null column metadata for a valid empty result,
  // including UPDATE/DELETE RETURNING that changed no records.
  if (response.Columns == null && (response.Rows == null || response.Rows.length === 0)) return [];
  if (!Array.isArray(response.Columns)) throw safeError('query', 'PG_RESPONSE_INVALID');
  return (response.Rows || []).map(row => {
    let values;
    try { values = typeof row === 'string' ? JSON.parse(row) : row; } catch { throw safeError('query', 'PG_RESPONSE_INVALID'); }
    if (!Array.isArray(values) || values.length !== response.Columns.length) throw safeError('query', 'PG_RESPONSE_INVALID');
    return Object.fromEntries(response.Columns.map((column, i) => [column, values[i]]));
  });
}
function decode(row) {
  if (!row) return null;
  let data;
  try { data = typeof row.data === 'string' ? JSON.parse(row.data) : row.data; } catch { throw safeError('decode_record', 'PG_RESPONSE_INVALID'); }
  if (!data || typeof data !== 'object' || Array.isArray(data)) throw safeError('decode_record', 'PG_RESPONSE_INVALID');
  return { ...data, _id: row.id };
}
class PostgresStore {
  constructor(client, env, options = {}) {
    if (!env || !/^[A-Za-z0-9_-]{1,128}$/.test(env)) throw new Error('CLOUDBASE_ENV_ID is required');
    this.client = client; this.env = env; this.role = options.role;
    this.interval = options.interval === undefined ? 125 : options.interval;
    this.nextCall = 0; this.gate = Promise.resolve();
  }
  async query(sql, stage = 'query') {
    // 8 calls/s per warm instance, below ExecutePGSql's 20/s environment quota.
    // No request, SQL text, credential or response body is logged.
    let unlock;
    const previous = this.gate;
    this.gate = new Promise(resolve => { unlock = resolve; });
    await previous;
    try {
      const delay = this.nextCall - Date.now();
      if (delay > 0) await sleep(delay);
      this.nextCall = Date.now() + this.interval;
      const request = { EnvId: this.env, Sql: sql };
      if (this.role) request.Role = this.role;
      try { return rows(await this.client.ExecutePGSql(request)); }
      catch (error) { throw safeError(stage, failureCode(error)); }
    } finally { unlock(); }
  }
  async record(id) { return (await this.query('SELECT id,version,data FROM ' + TABLE + ' WHERE id=' + key(id), 'read_record'))[0] || null; }
  async get(id) { return decode(await this.record(id)); }
  async atomic(id, operation) {
    for (let attempt = 0; attempt < 12; attempt++) {
      const record = await this.record(id);
      const change = operation(decode(record));
      if (!change.doc && !change.remove) return change.result;
      let sql;
      if (change.remove) {
        if (!record) return change.result;
        sql = 'DELETE FROM ' + TABLE + ' WHERE id=' + key(id) + ' AND version=' + integer(Number(record.version)) + ' RETURNING id';
      } else {
        const { _id, ...data } = change.doc;
        const encoded = jsonSql(data);
        if (!record) sql = 'INSERT INTO ' + TABLE + '(id,data) VALUES(' + key(id) + ',' + encoded + ') ON CONFLICT(id) DO NOTHING RETURNING id';
        else sql = 'UPDATE ' + TABLE + ' SET data=' + encoded + ',version=version+1 WHERE id=' + key(id) + ' AND version=' + integer(Number(record.version)) + ' RETURNING id';
      }
      if ((await this.query(returningSelect(sql), change.remove ? 'delete_record' : record ? 'write_compare_swap' : 'write_insert')).length) return change.result;
      await sleep(Math.min(25 * (attempt + 1), 150));
    }
    throw safeError('write_compare_swap', 'PG_CONTENTION');
  }
  async queued(limit = 20) {
    return (await this.query("SELECT id,data FROM " + TABLE + " WHERE data->>'state'='queued' ORDER BY (data->>'createdAt')::bigint LIMIT " + integer(limit), 'list_queued')).map(decode);
  }
  async expired(now, limit = 100) {
    return (await this.query("SELECT id,data FROM " + TABLE + " WHERE (data->>'purgeAfter')::bigint <= " + integer(now) + ' LIMIT ' + integer(limit), 'list_expired')).map(decode);
  }
  async stalePayloads(now, limit = 100) {
    return (await this.query("SELECT id,data FROM " + TABLE + " WHERE data->>'payloadUntil' IS NOT NULL AND (data->>'payloadUntil')::bigint <= " + integer(now) + ' LIMIT ' + integer(limit), 'list_payloads')).map(decode);
  }
  async cleanup(now) {
    const timestamp = integer(now);
    await this.query(returningSelect('DELETE FROM ' + TABLE + " WHERE (data->>'purgeAfter')::bigint <= " + timestamp + ' RETURNING id'), 'cleanup_delete');
    await this.query(returningSelect('UPDATE ' + TABLE + " SET version=version+1,data=data || jsonb_build_object('requestBody','','responseBody','','payloadUntil',NULL,'state',CASE WHEN data->>'state'='queued' THEN 'expired' WHEN data->>'state'='claimed' THEN 'uncertain' ELSE data->>'state' END) WHERE data->>'payloadUntil' IS NOT NULL AND (data->>'payloadUntil')::bigint <= " + timestamp + ' RETURNING id'), 'cleanup_payload');
  }
}
function createPostgresStore(env, context) {
  const sdk = require('tencentcloud-sdk-nodejs-tcb');
  const Client = sdk.tcb.v20180608.Client;
  function runtimeCredential() {
    const secretId = process.env.TENCENTCLOUD_SECRETID;
    const secretKey = process.env.TENCENTCLOUD_SECRETKEY;
    const token = process.env.TENCENTCLOUD_SESSIONTOKEN;
    if (!secretId || !secretKey || !token) throw safeError('query', 'RUNTIME_IDENTITY_MISSING');
    return { secretId, secretKey, token };
  }
  // Re-evaluate temporary runtime credentials for each request, including warm
  // invocations after credential rotation. Never read local CLI credential files.
  const client = { async ExecutePGSql(request) {
    const actual = new Client({ credential: runtimeCredential(), region: process.env.TENCENTCLOUD_REGION || 'ap-shanghai',
      profile: { httpProfile: { endpoint: 'tcb.tencentcloudapi.com', reqTimeout: 8 }, signMethod: 'TC3-HMAC-SHA256' } });
    return actual.ExecutePGSql(request);
  } };
  return new PostgresStore(client, env, { role: process.env.CLOUDBASE_PG_ROLE || undefined });
}
module.exports = { TABLE, PostgresStore, createPostgresStore, jsonSql, rows, returningSelect };
