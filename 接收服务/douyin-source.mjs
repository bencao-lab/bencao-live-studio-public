import { EventEmitter } from 'node:events';
import { fork } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
async function signStub() { throw new Error('浏览器接收模式不使用独立签名'); }

const UUID = /^[a-f0-9-]{36}$/i;
const DIGITS = /^\d{1,25}$/;
const DIRECTORY = fileURLToPath(new URL('./douyin/', import.meta.url));
export function stableUuid(key) {
  const b = createHash('sha256').update(key).digest().subarray(0, 16);
  b[6] = (b[6] & 15) | 0x50; b[8] = (b[8] & 63) | 0x80;
  const s = b.toString('hex'); return `${s.slice(0,8)}-${s.slice(8,12)}-${s.slice(12,16)}-${s.slice(16,20)}-${s.slice(20)}`;
}
export function parseRoom(input) {
  const s = typeof input === 'string' ? input.trim() : '';
  if (DIGITS.test(s)) return s;
  try {
    const u = new URL(s), match = /^\/(\d{1,25})\/?$/.exec(u.pathname);
    if (u.protocol === 'https:' && u.hostname === 'live.douyin.com' && !u.username && !u.password && !u.port && match) return match[1];
  } catch {}
  throw new Error('请输入抖音直播间链接或数字房号。');
}
function startWorker(webRid, options) {
  return fork(fileURLToPath(new URL('./douyin/browser-collector.mjs', import.meta.url)), [webRid, JSON.stringify(options)], {
    cwd: DIRECTORY, execArgv: [],
    env: Object.fromEntries(['SystemRoot','WINDIR','LOCALAPPDATA','APPDATA','TEMP','TMP','PATH','USERPROFILE','BENCAO_DATA_ROOT'].filter(key => process.env[key]).map(key => [key, process.env[key]])),
    windowsHide: true, stdio: ['ignore', 'ignore', 'pipe', 'ipc'],
  });
}

export class DouyinSource extends EventEmitter {
  constructor({ options, maxCommentChars = 200, workerFactory = startWorker, signer = signStub, now = Date.now }) {
    super(); this.options = options; this.enabled = options?.enabled === true; this.maxCommentChars = maxCommentChars;
    this.workerFactory = workerFactory; this.signer = signer; this.now = now;
    this.state = 'disconnected'; this.webRid = ''; this.roomId = ''; this.error = ''; this.epoch = 0;
    this.messages = new Map(); this.seen = new Map(); this.clients = new Map(); this.pending = []; this.giftGroups = new Map();
    this.sequence = 0; this.received = 0; this.duplicates = 0; this.skipped = 0; this.autoOwner = null;
    this.autoEpoch = 0; this.giftFrames = 0; this.receivedGifts = 0;
    this.wire = null;
    this.giftComboWaitMs = options?.giftComboWaitMs ?? 2000; this.maxGiftGroups = options?.maxGiftGroups ?? options?.maxMessages ?? 80;
    if (this.enabled && (![this.giftComboWaitMs,this.maxGiftGroups].every(n => Number.isInteger(n) && n > 0 && n <= 3600000))) throw new Error('礼物合并配置无效。');
    if (this.enabled) for (const k of ['maxMessages','maxDedup','dedupTtlMs','maxPendingReplies','maxCommentAgeMs','maxReconnectAttempts','reconnectMaxMs','heartbeatMs','controllerLeaseMs','idleDisconnectMs','pollMs']) {
      if (!Number.isInteger(options[k]) || options[k] < 1 || options[k] > 3600000) throw new Error(`弹幕配置无效：${k}`);
    }
  }
  touch(clientId) {
    if (!UUID.test(clientId ?? '')) throw new Error('页面身份无效，请刷新。');
    this.clients.set(clientId, this.now());
    while (this.clients.size > 8) {
      const oldest = [...this.clients.keys()].find(id => id !== this.autoOwner && id !== clientId);
      if (!oldest) break; this.clients.delete(oldest);
    }
  }
  setAuto(enabled, clientId) {
    this.touch(clientId);
    if (enabled && this.state !== 'connected') throw new Error('请先连接直播间。');
    if (this.autoOwner && (this.autoOwner !== clientId || !enabled)) this.emit('auto-off', this.autoOwner);
    this.autoOwner = enabled ? clientId : null; this.pending = []; this.autoEpoch++;
    return this.snapshot(clientId);
  }
  sweep() {
    const now = this.now();
    if (this.autoOwner && now - (this.clients.get(this.autoOwner) ?? 0) > this.options.controllerLeaseMs) {
      const previous = this.autoOwner; this.autoOwner = null; this.pending = []; this.autoEpoch++; this.emit('auto-off', previous);
    }
    for (const [key, group] of this.giftGroups) if (now - group.lastUpdate >= this.giftComboWaitMs) this.finishGift(key, group, 'idle');
    for (const [id, last] of this.clients) if (now - last > this.options.idleDisconnectMs) this.clients.delete(id);
    this.pending = this.pending.filter(id => { const row = this.messages.get(id); return row && row.status === 'received' && now - row.at <= this.options.maxCommentAgeMs; });
    for (const [id, at] of this.seen) { if (now - at <= this.options.dedupTtlMs) break; this.seen.delete(id); }
    if (this.worker && !this.clients.size) this.disconnect().catch(() => {});
  }
  snapshot(clientId = '', after = 0) {
    return { enabled: this.enabled, generation: this.epoch, state: this.state, webRid: this.webRid, roomId: this.roomId, error: this.error,
      received: this.received, duplicates: this.duplicates, skipped: this.skipped, cursor: this.sequence,
      giftFrames: this.giftFrames, receivedGifts: this.receivedGifts, waitingGiftGroups: this.giftGroups.size,
      wire: this.wire ? { ...this.wire } : null,
      autoEnabled: Boolean(this.autoOwner), ownsAuto: this.autoOwner === clientId,
      pending: this.pending.slice(), capacity: this.options?.maxPendingReplies ?? 0,
      messages: [...this.messages.values()].filter(row => row.sequence > after).map(({ at, ...row }) => row),
      statuses: [...this.messages.values()].map(({ id, status }) => ({ id, status })) };
  }
  poll(clientId, after = 0) { this.touch(clientId); if (this.enabled) this.sweep(); return this.snapshot(clientId, Number.isSafeInteger(after) && after >= 0 ? after : 0); }
  async connect(room, clientId) {
    if (!this.enabled) throw new Error('当前配置尚未启用直播弹幕。');
    const webRid = parseRoom(room); this.touch(clientId);
    if (webRid === this.webRid && this.worker && ['connected','connecting','reconnecting'].includes(this.state)) return this.snapshot(clientId);
    await this.disconnect();
    this.touch(clientId); this.webRid = webRid; this.roomId = ''; this.state = 'connecting'; this.error = '';
    this.messages.clear(); this.seen.clear(); this.pending = []; this.giftGroups.clear(); this.received = 0; this.duplicates = 0; this.skipped = 0; this.giftFrames = 0; this.receivedGifts = 0; this.wire = null;
    const epoch = ++this.epoch;
    this.signatureAbort = new AbortController();
    let worker;
    try { worker = this.workerFactory(webRid, this.options); } catch { this.state = 'failed'; this.error = '弹幕接收进程未能启动。'; return this.snapshot(clientId); }
    this.worker = worker;
    let signing = false, stderr = '';
    worker.stderr?.on('data', b => { stderr = (stderr + b.toString()).slice(-4096); });
    worker.on('message', async m => {
      if (epoch !== this.epoch || !m || typeof m !== 'object' || JSON.stringify(m).length > 16384) return;
      if (m.type === 'sign') {
        if (signing || !/^[a-f0-9]{32}$/.test(m.stub ?? '') || !Number.isInteger(m.id)) return;
        signing = true;
        try {
          const signature = await this.signer(m.stub, { signal: this.signatureAbort.signal });
          if (epoch === this.epoch && worker.connected) worker.send({ type: 'signature', id: m.id, signature });
        } catch { if (epoch === this.epoch && worker.connected) worker.send({ type: 'signature', id: m.id, error: '弹幕签名失败，请检查接收组件或稍后重连。' }); }
        finally { signing = false; }
      } else if (m.type === 'traffic') {
        const keys = ['chat','gift','giftDecoded','giftRejected'];
        if (keys.every(key => Number.isSafeInteger(m.wire?.[key]) && m.wire[key] >= 0)) this.wire = Object.fromEntries(keys.map(key => [key,m.wire[key]]));
      } else if (m.type === 'state' && ['connecting','connected','reconnecting','failed'].includes(m.state)) {
        if (m.state === 'connected' && !/^\d{15,25}$/.test(m.roomId ?? '')) return;
        if (m.state === 'connected' && /^\d{15,25}$/.test(m.roomId ?? '')) {
          if (this.roomId && this.roomId !== m.roomId) { this.emit('scope-end'); this.setAuto(false, clientId); this.pending = []; this.messages.clear(); this.seen.clear(); this.giftGroups.clear(); }
          this.roomId = m.roomId;
        }
        this.state = m.state; this.error = typeof m.error === 'string' ? m.error.slice(0, 200) : '';
        if (m.state === 'failed' && this.autoOwner) { this.emit('auto-off', this.autoOwner); this.autoOwner = null; this.pending = []; }
      } else if (['chat','gift'].includes(m.type)) this.accept(m);
    });
    worker.once('error', () => { if (epoch === this.epoch) { this.state = 'failed'; this.error = '弹幕接收进程启动失败。'; } });
    worker.once('exit', () => {
      if (epoch !== this.epoch) return;
      this.worker = null; this.signatureAbort.abort();
      clearInterval(this.sweeper); this.sweeper = null;
      if (this.autoOwner) this.emit('auto-off', this.autoOwner);
      this.autoOwner = null; this.pending = [];
      if (this.state !== 'failed') { this.state = 'failed'; this.error = /Cannot find (package|module)/.test(stderr) ? '弹幕组件依赖未就绪，请先安装 app/douyin 中的依赖。' : '弹幕接收进程已停止，可重新连接。'; }
    });
    clearInterval(this.sweeper); this.sweeper = setInterval(() => this.sweep(), 1000); this.sweeper.unref();
    return this.snapshot(clientId);
  }
  accept(m) {
    if (m.type === 'gift') return this.acceptGift(m);
    if (this.state !== 'connected' || m.roomId !== this.roomId || typeof m.text !== 'string' || !m.text.trim() || m.text.length > 1000) return;
    const validMessage = DIGITS.test(m.messageId ?? '') && m.messageId !== '0';
    const key = `${m.roomId}:${validMessage ? m.messageId : `frame:${String(m.fallbackId).slice(0,80)}`}`;
    const now = this.now();
    if (this.wasSeen(key, now)) { this.duplicates++; return; }
    this.remember(key, now); this.storeEvent(m, key);
  }
  wasSeen(key, now = this.now()) { return this.seen.has(key) && now - this.seen.get(key) <= this.options.dedupTtlMs; }
  remember(key, now = this.now()) {
    this.seen.delete(key); this.seen.set(key, now);
    while (this.seen.size > this.options.maxDedup) this.seen.delete(this.seen.keys().next().value);
  }
  acceptGift(m) {
    if (this.state !== 'connected' || m.roomId !== this.roomId || !DIGITS.test(m.giftId ?? '') || m.giftId === '0' ||
      !Number.isInteger(m.count) || m.count < 1 || m.count > 1000000 || typeof m.giftName !== 'string' || [...m.giftName].length > 60 || typeof m.combo !== 'boolean' || typeof m.repeatEnd !== 'boolean') return;
    this.giftFrames++;
    const validMessage = DIGITS.test(m.messageId ?? '') && m.messageId !== '0', now = this.now();
    const messageKey = `${m.roomId}:gift-message:${validMessage ? m.messageId : `frame:${String(m.fallbackId).slice(0,80)}`}`;
    if (this.wasSeen(messageKey, now)) { this.duplicates++; return; }
    this.remember(messageKey, now);
    const grouped = m.combo && DIGITS.test(m.groupId ?? '') && m.groupId !== '0' && DIGITS.test(m.userId ?? '');
    // Without a group ID only the terminal combo message is authoritative enough to thank.
    if (m.combo && !grouped && !m.repeatEnd) return;
    const key = grouped ? `${m.roomId}:gift-group:${m.userId}:${m.giftId}:${m.groupId}` : messageKey;
    if (grouped && this.wasSeen(key, now)) { this.duplicates++; return; }
    let group = this.giftGroups.get(key);
    if (!group) group = { message: { ...m }, count: m.count, lastUpdate: now, autoEpoch: this.autoEpoch };
    else if (m.count > group.count) { group.count = m.count; group.lastUpdate = now; }
    group.message = { ...m, giftName: m.giftName || group.message.giftName };
    if (!m.combo || m.repeatEnd) return this.finishGift(key, group, m.combo ? 'end' : 'single');
    this.giftGroups.set(key, group);
    while (this.giftGroups.size > this.maxGiftGroups) { this.giftGroups.delete(this.giftGroups.keys().next().value); this.skipped++; }
  }
  finishGift(key, group, completion) {
    this.giftGroups.delete(key);
    if (this.state !== 'connected' || group.message.roomId !== this.roomId || this.wasSeen(key) && key.includes(':gift-group:')) return;
    this.remember(key);
    const gift = { id: group.message.giftId, name: group.message.giftName || '礼物', count: group.count, unitDiamonds: group.message.unitDiamonds || 0, completion };
    this.storeEvent({ ...group.message, text: `送出${gift.name} × ${gift.count}` }, key, 'gift', gift, group.autoEpoch);
    this.receivedGifts++;
  }
  storeEvent(m, key, kind = 'chat', gift, autoEpoch = this.autoEpoch) {
    const validUser = DIGITS.test(m.userId ?? '') && m.userId !== '0';
    const validMessage = DIGITS.test(m.messageId ?? '') && m.messageId !== '0', now = this.now();
    const row = { id: stableUuid(`douyin-turn:${key}`), viewerId: validUser ? stableUuid(`douyin-viewer:${m.roomId}:${m.userId}`) : null,
      roomId: m.roomId, webRid: this.webRid, nickname: typeof m.nickname === 'string' ? m.nickname.slice(0,80) : '观众',
      text: m.text.trim(), receivedAt: new Date(now).toISOString(), at: now, sequence: ++this.sequence,
      kind, ...(gift ? { gift } : {}), replyable: validUser && validMessage && [...m.text.trim()].length <= this.maxCommentChars, status: 'received' };
    this.messages.set(row.id, row); this.received++;
    while (this.messages.size > this.options.maxMessages) { const oldest = this.messages.keys().next().value; this.messages.delete(oldest); this.pending = this.pending.filter(id => id !== oldest); }
    if (this.autoOwner && row.replyable && autoEpoch === this.autoEpoch) {
      this.pending.push(row.id);
      while (this.pending.length > this.options.maxPendingReplies) {
        const chat = this.pending.findIndex(id => this.messages.get(id)?.kind !== 'gift');
        this.pending.splice(chat < 0 ? 0 : chat, 1); this.skipped++;
      }
      this.pending.sort((a,b) => Number(this.messages.get(b)?.kind === 'gift') - Number(this.messages.get(a)?.kind === 'gift'));
    }
  }
  select(messageId, clientId, automatic = false) {
    this.touch(clientId); this.sweep();
    const row = this.messages.get(messageId);
    if (this.state !== 'connected' || !row || row.roomId !== this.roomId) throw new Error('这条弹幕已不在当前连接中。');
    if (!row.replyable) throw new Error('这条弹幕缺少有效身份或超过200字，暂不回复。');
    if (row.status !== 'received') throw new Error('这条弹幕已经处理，未重复生成。');
    if (this.now() - row.at > this.options.maxCommentAgeMs) throw new Error('这条弹幕已经过期，请选择较新的评论。');
    if (automatic && (this.autoOwner !== clientId || !this.pending.includes(messageId))) throw new Error('自动回复控制已更新，请重新选择。');
    return { ...row };
  }
  mark(id, status) { const row = this.messages.get(id); if (row) row.status = status; this.pending = this.pending.filter(x => x !== id); }
  release(clientId) {
    if (this.autoOwner === clientId) { this.emit('auto-off', clientId); this.autoOwner = null; this.pending = []; }
    this.clients.delete(clientId);
    if (!this.clients.size) return this.disconnect();
  }
  async disconnect() {
    ++this.epoch; this.emit('scope-end');
    if (this.autoOwner) this.emit('auto-off', this.autoOwner);
    this.autoOwner = null; this.pending = []; this.giftGroups.clear(); this.autoEpoch++; clearInterval(this.sweeper); this.sweeper = null;
    this.signatureAbort?.abort();
    const child = this.worker; this.worker = null; this.state = 'disconnected'; this.error = '';
    if (!child || child.exitCode !== null) return;
    await new Promise(resolve => {
      const done = () => { clearTimeout(timer); resolve(); };
      const timer = setTimeout(() => { child.kill(); resolve(); }, 3000);
      child.once('exit', done);
      if (child.connected) child.send({ type: 'stop' }); else child.kill();
    });
  }
  async close() { this.clients.clear(); await this.disconnect(); }
}
