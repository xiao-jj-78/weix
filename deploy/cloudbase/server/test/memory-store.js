'use strict';
// Test-only store. Production index.js never imports this module.
class MemoryStore {
  constructor() { this.documents = new Map(); this.lock = Promise.resolve(); }
  async get(id) { return structuredClone(this.documents.get(id) || null); }
  async atomic(id, operation) {
    let unlock; const old = this.lock; this.lock = new Promise(resolve => { unlock = resolve; });
    await old;
    try {
      const change = operation(structuredClone(this.documents.get(id) || null));
      if (change.doc) this.documents.set(id, structuredClone(change.doc));
      else if (change.remove) this.documents.delete(id);
      return structuredClone(change.result);
    } finally { unlock(); }
  }
  async queued() { return [...this.documents.values()].filter(d => d.state === 'queued').sort((a, b) => a.createdAt - b.createdAt).map(d => structuredClone(d)); }
  async expired(now) { return [...this.documents.values()].filter(d => d.purgeAfter <= now).map(d => structuredClone(d)); }
  async stalePayloads(now) { return [...this.documents.values()].filter(d => d.payloadUntil && d.payloadUntil <= now).map(d => structuredClone(d)); }
}
module.exports = { MemoryStore };
