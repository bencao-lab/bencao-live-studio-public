import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { BrowserManager } from './browser-engine/dist/collector/browser.js';
import { LiveSession } from './browser-engine/dist/collector/liveSession.js';
import { normalize } from './browser-engine/dist/pipeline/normalizer.js';
import { resolveRoom } from './room.mjs';

const webRid = process.argv[2];
const options = JSON.parse(process.argv[3]);
const controller = new AbortController();
const browser = new BrowserManager();
let session, stopping = false, pending = 0;
const wire = { chat: 0, gift: 0, giftDecoded: 0, giftRejected: 0 };
function send(message) {
  if (!process.connected || pending >= 128) return;
  pending++;
  process.send(message, () => pending--);
}
function stop() {
  stopping = true;
  controller.abort();
  // Closing the browser also interrupts a pending room navigation.
  void browser.close();
}
process.on('disconnect', stop);
process.on('SIGTERM', stop);
process.on('message', message => { if (message?.type === 'stop') stop(); });

let failures = 0;
try {
  const { stdout: cookie } = await promisify(execFile)('powershell.exe', [
    '-NoProfile', '-NonInteractive', '-File',
    fileURLToPath(new URL('./read-session.ps1', import.meta.url)),
  ], { windowsHide: true, timeout: 10000, maxBuffer: 262144 });
  if (!/(?:^|;\s*)(sessionid|sessionid_ss|sid_tt)=/.test(cookie)) {
    throw new Error('抖音登录已失效，请重新登录礼物接收账号。');
  }
  while (!stopping) {
    try {
      send({ type: 'state', state: failures ? 'reconnecting' : 'connecting' });
      const { roomId } = await resolveRoom(webRid, controller.signal);
      const opened = await browser.openRoom('https://live.douyin.com/' + webRid, { cookie });
      if (stopping) break;
      session = new LiveSession(webRid, opened.page, opened.cdp, {
        onOpen() { send({ type: 'state', state: 'connected', roomId }); },
        onMessage(message, meta) {
          if (stopping) return;
          if (message.method === 'WebcastChatMessage') wire.chat++;
          if (message.method === 'WebcastGiftMessage') wire.gift++;
          let event;
          try { event = normalize(message, meta); } catch {}
          if (message.method === 'WebcastGiftMessage') {
            if (event?.type === 'gift') wire.giftDecoded++; else wire.giftRejected++;
          }
          send({ type: 'traffic', wire: { ...wire } });
          if (!event || !['chat', 'gift'].includes(event.type)) return;
          const data = event.data;
          send({
            type: event.type, roomId, messageId: String(event.id),
            fallbackId: String(event.id), userId: event.user?.id ?? '',
            nickname: event.user?.nickname ?? '观众',
            text: event.type === 'chat' ? data.content?.slice(0, 1000) ?? '' : '',
            giftId: data.giftId, giftName: data.giftName,
            count: Math.max(1, data.repeatCount || data.comboCount || data.groupCount || 1),
            combo: Boolean(data.combo), repeatEnd: Boolean(data.repeatEnd),
            groupId: data.groupId, unitDiamonds: data.diamondCount,
          });
        },
      }, opened.context);
      if (!await session.waitForLive(30000)) throw new Error('抖音消息通道连接超时。');
      while (!stopping && session.status === 'live') {
        await delay(500, undefined, { signal: controller.signal });
      }
      if (!stopping) throw new Error('抖音消息通道断开，正在重连。');
    } catch (error) {
      if (stopping) break;
      failures++;
      if (failures > options.maxReconnectAttempts) throw error;
      send({ type: 'state', state: 'reconnecting', attempt: failures, error: '抖音消息通道断开，正在重连。' });
      await session?.stop(); session = null;
      await delay(Math.min(options.reconnectMaxMs, 2000 * failures), undefined, { signal: controller.signal });
    } finally {
      await session?.stop(); session = null;
    }
  }
} catch {
  if (!stopping) send({ type: 'state', state: 'failed', error: '抖音浏览器接收失败，请检查登录状态、Chrome 和房间开播状态。' });
} finally {
  await browser.close();
  if (process.connected) process.disconnect();
}
