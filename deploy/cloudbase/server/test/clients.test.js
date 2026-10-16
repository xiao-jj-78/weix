'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { route, validateBody } = require('../routes');
test('cloud only routes client operations with bounded process lifetime identity', () => {
  const id = '42:638960000000000000';
  assert.equal(route('GET', '/api/sdk/clients').path, '/api/sdk/clients');
  assert.equal(route('POST', '/api/sdk/clients/verify').long, true);
  validateBody('/api/sdk/clients/focus', { instanceId: id });
  validateBody('/api/sdk/clients/alias', { instanceId: id, alias: '工作微信' });
  for (const body of [{ instanceId: '42' }, { instanceId: id, command: 'x' }, { instanceId: '../42' }]) assert.throws(() => validateBody('/api/sdk/clients/focus', body));
  assert.throws(() => validateBody('/api/sdk/clients/alias', { instanceId: id, alias: '\u0080' }));
  assert.throws(() => validateBody('/api/sdk/clients/open', { executable: 'other.exe' }));
  assert.throws(() => route('POST', '/api/sdk/clients/kill'));
});
