'use strict';
const fs = require('node:fs');
const crypto = require('node:crypto');
const { BridgeError } = require('./routes');
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
function validatePolicy(policy) {
  if (!policy || !/^[a-f0-9]{64}$/i.test(policy.browserHash || '') || !/^[a-f0-9]{64}$/i.test(policy.agentHash || '') ||
      policy.browserHash.toLowerCase() === policy.agentHash.toLowerCase()) throw new Error('A valid access-policy.json with distinct SHA256 role hashes is required.');
  return { browserHash: policy.browserHash.toLowerCase(), agentHash: policy.agentHash.toLowerCase() };
}
function loadPolicy(filename) { return validatePolicy(JSON.parse(fs.readFileSync(filename, 'utf8').replace(/^\uFEFF/, ''))); }
function authorize(headers, role, policy) {
  const match = /^Bearer ([A-Za-z0-9._~-]{24,256})$/.exec(headers.authorization || '');
  if (!match) throw new BridgeError(401, '请先输入访问令牌', 'AUTH_REQUIRED');
  const digest = Buffer.from(hash(match[1]), 'hex');
  const browser = crypto.timingSafeEqual(digest, Buffer.from(policy.browserHash, 'hex'));
  const agent = crypto.timingSafeEqual(digest, Buffer.from(policy.agentHash, 'hex'));
  if (!browser && !agent) throw new BridgeError(401, '访问令牌无效', 'AUTH_INVALID');
  if ((role === 'browser' && !browser) || (role === 'agent' && !agent)) throw new BridgeError(403, '访问令牌角色不匹配', 'ROLE_FORBIDDEN');
}
module.exports = { hash, validatePolicy, loadPolicy, authorize };
