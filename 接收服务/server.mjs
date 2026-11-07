import http from 'node:http';
import { randomBytes } from 'node:crypto';
import { DouyinSource } from './douyin-source.mjs';
import { join } from 'node:path';
import { homedir } from 'node:os';

process.env.BENCAO_DATA_ROOT ??= join(process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local'), '本草收藏直播台外发版');

const options = {
  enabled: true, maxMessages: 80, maxDedup: 5000, dedupTtlMs: 900000,
  maxPendingReplies: 3, maxCommentAgeMs: 120000, maxReconnectAttempts: 5,
  reconnectMaxMs: 30000, heartbeatMs: 10000, controllerLeaseMs: 15000,
  idleDisconnectMs: 15000, pollMs: 1500, giftComboWaitMs: 2000, maxGiftGroups: 80,
};
const source = new DouyinSource({ options });
const token = randomBytes(32).toString('hex');
let lastRequest = Date.now(), closing = false;
function reply(res, status, body) {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
  res.end(JSON.stringify(body));
}
const server = http.createServer(async (req, res) => {
  if (!['127.0.0.1:18769', 'localhost:18769'].includes(req.headers.host) ||
      req.headers.origin || req.headers['sec-fetch-site'] === 'cross-site') {
    return reply(res, 403, { error: '仅供本机直播台使用' });
  }
  try {
    if (req.method === 'GET' && req.url === '/api/status') {
      lastRequest = Date.now();
      return reply(res, 200, { app: 'bencao-public-receiver-v1', token, douyin: source.snapshot() });
    }
    if (req.method === 'GET' && req.url === '/app/config.json') {
      return reply(res, 200, { douyin: { defaultRoom: '' } });
    }
    if (req.method !== 'POST' || req.headers['x-stage-token'] !== token || closing) {
      return reply(res, 403, { error: '接收会话无效' });
    }
    let body = '';
    for await (const chunk of req) {
      body += chunk;
      if (body.length > 16384) return reply(res, 413, { error: '请求过大' });
    }
    const input = JSON.parse(body);
    if (!/^[a-f0-9-]{36}$/i.test(input.clientId ?? '')) return reply(res, 400, { error: '客户端身份无效' });
    lastRequest = Date.now();
    switch (req.url) {
      case '/api/douyin/connect': return reply(res, 200, await source.connect(input.room, input.clientId));
      case '/api/douyin/poll': return reply(res, 200, source.poll(input.clientId, input.after));
      case '/api/douyin/release':
        await source.release(input.clientId); return reply(res, 200, { ok: true });
      case '/api/douyin/disconnect':
        await source.disconnect(); return reply(res, 200, { ok: true });
      default: return reply(res, 404, { error: '接口不存在' });
    }
  } catch {
    reply(res, 400, { error: '请求失败，请检查直播间号码和接收组件' });
  }
});
const timer = setInterval(() => {
  source.sweep();
  if (Date.now() - lastRequest > 60000 && !source.worker) void stop();
}, 1000);
async function stop() {
  if (closing) return;
  closing = true; clearInterval(timer);
  await source.close();
  server.close();
  server.closeIdleConnections();
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
server.listen(18769, '127.0.0.1');
