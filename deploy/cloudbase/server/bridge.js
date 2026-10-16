'use strict';
const crypto = require('node:crypto');
const { hash } = require('./auth');
const { BridgeError } = require('./routes');
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const MAX_RESULT = 512 * 1024;
const OFFLINE_MS = 45000;
const RETAIN_MS = 24 * 60 * 60 * 1000;
const PAYLOAD_MS = 2 * 60 * 1000;
const COMMAND_ID = /^cmd_[a-f0-9]{64}$/;

class Bridge {
  constructor(store, options = {}) {
    this.store = store; this.clock = options.clock || Date.now; this.sleep = options.sleep || sleep;
    this.shortMs = options.shortMs || 30000; this.longMs = options.longMs || 110000;
    this.pollMs = options.pollMs === undefined ? 0 : options.pollMs;
    this.waitStep = options.waitStep || 1200; this.pollStep = options.pollStep || 1500;
    this.lastCleanup = 0;
  }
  async nodeStatus() {
    const agent = await this.store.get('agent');
    return { ok: true, nodeOnline: Boolean(agent && this.clock() - agent.lastSeen < OFFLINE_MS),
      lastSeen: agent?.lastSeen || null, agentId: agent?.agentId || null };
  }
  async heartbeat(agentId) {
    if (typeof agentId !== 'string' || !/^[A-Za-z0-9_-]{1,80}$/.test(agentId)) throw new BridgeError(400, 'Windows 节点标识无效');
    const now = this.clock();
    await this.store.atomic('agent', previous => {
      if (previous && previous.agentId !== agentId && now - previous.lastSeen < OFFLINE_MS) throw new BridgeError(409, '另一个 Windows 节点正在连接', 'NODE_ALREADY_CONNECTED');
      return { doc: { _id: 'agent', kind: 'agent', agentId, lastSeen: now, purgeAfter: now + RETAIN_MS }, result: true };
    });
  }
  async enqueue(method, endpoint, body, key) {
    if (!key && method === 'POST') throw new BridgeError(400, '写操作必须提供 X-Idempotency-Key', 'IDEMPOTENCY_REQUIRED');
    if (key && !/^[A-Za-z0-9_-]{16,128}$/.test(key)) throw new BridgeError(400, '幂等请求标识无效');
    const id = 'cmd_' + hash(key || crypto.randomUUID());
    const requestBody = method === 'POST' ? JSON.stringify(body) : '';
    const fingerprint = hash(method + '\n' + endpoint.path + '\n' + requestBody);
    const existing = await this.store.get(id);
    if (existing) {
      if (existing.fingerprint !== fingerprint) throw new BridgeError(409, '此请求标识已用于不同操作', 'IDEMPOTENCY_CONFLICT');
      return existing;
    }
    const node = await this.nodeStatus();
    if (!node.nodeOnline) throw new BridgeError(503, 'Windows 工作节点离线，请在电脑上启动云连接器', 'NODE_OFFLINE');
    const now = this.clock();
    return this.store.atomic(id, previous => {
      if (previous) {
        if (previous.fingerprint !== fingerprint) throw new BridgeError(409, '此请求标识已用于不同操作', 'IDEMPOTENCY_CONFLICT');
        return { result: previous };
      }
      const command = { _id: id, kind: 'command', state: 'queued', method, path: endpoint.path, requestBody,
        fingerprint, createdAt: now, expiresAt: now + (endpoint.long ? this.longMs : this.shortMs),
        purgeAfter: now + RETAIN_MS, payloadUntil: now + PAYLOAD_MS };
      return { doc: command, result: command };
    });
  }
  async claim(agentId) {
    const candidates = await this.store.queued();
    for (const candidate of candidates) {
      const now = this.clock();
      const claimToken = crypto.randomBytes(24).toString('base64url');
      const result = await this.store.atomic(candidate._id, previous => {
        if (!previous || previous.state !== 'queued') return { result: null };
        if (previous.expiresAt <= now) return { doc: { ...previous, state: 'expired', requestBody: '', payloadUntil: null }, result: null };
        const command = { id: previous._id, method: previous.method, path: previous.path,
          body: previous.requestBody ? JSON.parse(previous.requestBody) : null, expiresAt: previous.expiresAt, claimToken };
        return { doc: { ...previous, state: 'claimed', claimedBy: agentId, claimHash: hash(claimToken),
          claimedAt: now, requestBody: '', payloadUntil: null }, result: command };
      });
      if (result) return result;
    }
    return null;
  }
  async poll(agentId) {
    await this.heartbeat(agentId);
    await this.cleanup();
    const deadline = this.clock() + this.pollMs;
    do {
      const command = await this.claim(agentId);
      if (command) return { ok: true, command };
      if (this.clock() >= deadline) break;
      await this.sleep(Math.min(this.pollStep, Math.max(0, deadline - this.clock())));
    } while (this.clock() < deadline);
    return { ok: true, command: null };
  }
  async complete(input) {
    if (!input || !COMMAND_ID.test(input.id || '') || !Number.isInteger(input.status) || input.status < 200 || input.status > 599 ||
      !input.body || typeof input.body !== 'object' || Array.isArray(input.body)) throw new BridgeError(400, 'Windows 回传结果格式无效');
    const responseBody = JSON.stringify(input.body);
    if (Buffer.byteLength(responseBody) > MAX_RESULT) throw new BridgeError(413, 'Windows 回传结果超过 512 KB', 'RESULT_TOO_LARGE');
    const resultHash = hash(input.status + '\n' + responseBody);
    const now = this.clock();
    return this.store.atomic(input.id, previous => {
      if (!previous || previous.kind !== 'command' || previous.purgeAfter <= now) throw new BridgeError(410, '命令不存在或已清理', 'COMMAND_GONE');
      if (input.agentId !== undefined && input.agentId !== previous.claimedBy) throw new BridgeError(403, '回传节点不匹配', 'CLAIM_MISMATCH');
      if (input.claimToken !== undefined && hash(String(input.claimToken)) !== previous.claimHash) throw new BridgeError(403, '回传消费凭据无效', 'CLAIM_MISMATCH');
      if (previous.state === 'completed') {
        if (previous.resultHash !== resultHash) throw new BridgeError(409, '命令已有不同执行结果', 'RESULT_CONFLICT');
        return { result: { ok: true, duplicate: true, late: previous.late || false } };
      }
      if (!['claimed', 'uncertain'].includes(previous.state)) throw new BridgeError(409, '命令未被消费或已过期', 'COMMAND_NOT_CLAIMED');
      const late = previous.expiresAt <= now;
      return { doc: { ...previous, state: 'completed', status: input.status, responseBody, resultHash,
        completedAt: now, late, payloadUntil: now + PAYLOAD_MS }, result: { ok: true, late } };
    });
  }
  async wait(id) {
    while (true) {
      const command = await this.store.get(id);
      if (!command) throw new BridgeError(410, '命令记录已清理', 'COMMAND_GONE');
      if (command.state === 'completed') {
        if (!command.responseBody) throw new BridgeError(410, '结果内容已按保留期清理；同一命令不会重复执行', 'RESULT_EXPIRED');
        return { status: command.status, body: JSON.parse(command.responseBody) };
      }
      if (command.state === 'expired') throw new BridgeError(504, '命令等待过期，未执行', 'COMMAND_EXPIRED');
      if (command.state === 'uncertain') throw new BridgeError(504, '命令已交给 Windows，结果尚未确认；请查看电脑状态，勿重复提交', 'EXECUTION_UNCERTAIN');
      if (command.expiresAt <= this.clock()) {
        await this.store.atomic(id, previous => {
          if (!previous || !['queued', 'claimed'].includes(previous.state)) return { result: null };
          return { doc: { ...previous, state: previous.state === 'queued' ? 'expired' : 'uncertain', requestBody: '', payloadUntil: null }, result: null };
        });
        continue;
      }
      await this.sleep(Math.min(this.waitStep, Math.max(0, command.expiresAt - this.clock())));
    }
  }
  async cleanup() {
    const now = this.clock();
    if (now - this.lastCleanup < 60000) return;
    this.lastCleanup = now;
    if (this.store.cleanup) { await this.store.cleanup(now); return; }
    for (const item of await this.store.expired(now)) {
      await this.store.atomic(item._id, previous => previous && previous.purgeAfter <= now ? { remove: true, result: null } : { result: null });
    }
    for (const item of await this.store.stalePayloads(now)) {
      if (!item.payloadUntil) continue;
      await this.store.atomic(item._id, previous => {
        if (!previous?.payloadUntil || previous.payloadUntil > now) return { result: null };
        const state = previous.state === 'queued' ? 'expired' : previous.state === 'claimed' ? 'uncertain' : previous.state;
        return { doc: { ...previous, state, requestBody: '', responseBody: '', payloadUntil: null }, result: null };
      });
    }
  }
}
module.exports = { Bridge, MAX_RESULT, OFFLINE_MS, RETAIN_MS };
