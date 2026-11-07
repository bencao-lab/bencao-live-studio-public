import https from 'node:https';
export const USER_AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36';

function request(url, { signal, referer, cookie = '' }) {
  return new Promise((resolve, reject) => {
    const u = new URL(url);
    if (u.protocol !== 'https:' || u.hostname !== 'live.douyin.com') return reject(new Error('直播间地址无效。'));
    const req = https.get(u, { signal, headers: { 'User-Agent': USER_AGENT, 'Accept-Language': 'zh-CN,zh;q=0.9', Referer: referer, ...(cookie ? { Cookie: cookie } : {}) } }, res => {
      let size = 0; const chunks = [];
      res.on('data', chunk => { size += chunk.length; if (size > 8 * 1024 * 1024) req.destroy(new Error('直播间响应过大。')); else chunks.push(chunk); });
      res.on('error', reject);
      res.on('end', () => { clearTimeout(timer); if (res.statusCode !== 200) reject(new Error(`直播间请求返回 ${res.statusCode}。`)); else resolve({ headers: res.headers, body: Buffer.concat(chunks).toString('utf8') }); });
    });
    const timer = setTimeout(() => req.destroy(new Error('直播间请求超时。')), 20000);
    req.on('error', e => { clearTimeout(timer); reject(e); });
    req.on('close', () => clearTimeout(timer));
  });
}

export async function resolveRoom(webRid, signal) {
  if (!/^\d{1,25}$/.test(webRid)) throw new Error('直播房号无效。');
  const referer = `https://live.douyin.com/${webRid}`;
  const first = await request(referer, { signal, referer });
  const ttwid = (first.headers['set-cookie'] ?? []).map(c => /^ttwid=([^;]+)/.exec(c)?.[1]).find(Boolean);
  if (!ttwid) throw new Error('没有取得访客会话，请稍后重新连接。');
  const params = new URLSearchParams({ aid: '6383', app_name: 'douyin_web', live_id: '1', device_platform: 'web', language: 'zh-CN', cookie_enabled: 'true', screen_width: '1920', screen_height: '1080', browser_language: 'zh-CN', browser_platform: 'Win32', browser_name: 'Chrome', browser_version: '126.0.0.0', web_rid: webRid, enter_from: 'web_live', is_need_double_stream: 'false' });
  const response = await request(`https://live.douyin.com/webcast/room/web/enter/?${params}`, { signal, referer, cookie: `ttwid=${ttwid}` });
  let result; try { result = JSON.parse(response.body); } catch { throw new Error('直播间返回了非预期内容。'); }
  const room = result?.data?.data?.[0];
  if (result.status_code !== 0 || !/^\d{15,25}$/.test(room?.id_str ?? '')) throw new Error('没有找到正在直播的房间。');
  if (room.status !== 2) throw new Error('这个房间当前未开播。');
  return { roomId: room.id_str, ttwid };
}
