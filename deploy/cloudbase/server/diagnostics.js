'use strict';
const STAGES = new Set(['read_record', 'write_insert', 'write_compare_swap', 'delete_record', 'list_queued',
  'list_expired', 'list_payloads', 'cleanup_delete', 'cleanup_payload', 'decode_record', 'query',
  'browser_node_status', 'agent_poll', 'agent_result', 'browser_enqueue', 'browser_wait', 'handler', 'configuration']);
const CODES = new Set(['PG_RATE_LIMIT', 'PG_SQL_FAILED', 'PG_CONNECT_FAILED', 'PG_TIMEOUT', 'PG_AUTH_FAILED',
  'PG_ROLE_NOT_FOUND', 'PG_RESPONSE_INVALID', 'PG_BAD_RECORD_KEY', 'PG_CONTENTION', 'RUNTIME_IDENTITY_MISSING', 'INTERNAL']);
function failureCode(error) {
  if (CODES.has(error?.safeCode)) return error.safeCode;
  const code = typeof error?.code === 'string' ? error.code : '';
  if (code.startsWith('RequestLimitExceeded')) return 'PG_RATE_LIMIT';
  if (code === 'FailedOperation.PGExecuteSqlError') return 'PG_SQL_FAILED';
  if (code === 'FailedOperation.PGConnectError') return 'PG_CONNECT_FAILED';
  if (code === 'FailedOperation.OperationTimeout' || code === 'ETIMEDOUT' || code === 'ECONNRESET') return 'PG_TIMEOUT';
  if (code.startsWith('UnauthorizedOperation') || code.startsWith('AuthFailure')) return 'PG_AUTH_FAILED';
  if (code === 'ResourceNotFound.RoleNotFound') return 'PG_ROLE_NOT_FOUND';
  return 'INTERNAL';
}
function safeError(stage, code) {
  const error = new Error('Bridge operation failed');
  error.safeStage = STAGES.has(stage) ? stage : 'handler';
  error.safeCode = CODES.has(code) ? code : 'INTERNAL';
  return error;
}
function diagnostic(error, fallback = 'handler', output = console.error) {
  const record = { event: 'bridge_error', stage: STAGES.has(error?.safeStage) ? error.safeStage : STAGES.has(fallback) ? fallback : 'handler',
    failureCode: failureCode(error) };
  // Explicit allowlists above are the entire log schema. Never emit message,
  // stack, SQL, URL, headers, environment, record ID or upstream response data.
  output(JSON.stringify(record));
  return record;
}
module.exports = { failureCode, safeError, diagnostic };
