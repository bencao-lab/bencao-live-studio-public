import { chromium } from './browser-engine/node_modules/playwright-core/index.mjs';
import { findChrome, DEFAULT_USER_AGENT } from './browser-engine/dist/collector/browser.js';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

let browser;
try {
  browser = await chromium.launch({ executablePath: findChrome(), headless: false });
  const context = await browser.newContext({ userAgent: DEFAULT_USER_AGENT, locale: 'zh-CN' });
  const page = await context.newPage();
  await page.goto('https://live.douyin.com/', { waitUntil: 'domcontentloaded', timeout: 45000 });
  let cookie = '';
  const deadline = Date.now() + 180000;
  while (Date.now() < deadline && !page.isClosed()) {
    const cookies = await context.cookies('https://live.douyin.com/');
    if (cookies.some(item => ['sessionid','sessionid_ss','sid_tt'].includes(item.name) && item.value)) {
      cookie = cookies.map(item => item.name + '=' + item.value).join('; ');
      break;
    }
    await delay(1000);
  }
  if (!cookie) throw new Error('登录未完成');
  await new Promise((resolve, reject) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-File',
      fileURLToPath(new URL('./save-session.ps1', import.meta.url))],
      { windowsHide: true, stdio: ['pipe','ignore','ignore'] });
    child.once('error', reject);
    child.once('exit', code => code === 0 ? resolve() : reject(new Error('登录保存失败')));
    child.stdin.on('error', reject);
    child.stdin.end(cookie);
  });
} catch {
  process.exitCode = 1;
} finally {
  await browser?.close();
}
