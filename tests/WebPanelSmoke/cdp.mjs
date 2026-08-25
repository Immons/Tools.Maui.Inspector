// Minimal CDP driver for headless Chromium (no npm deps): node cdp.mjs <chrome-binary> <url> <script.mjs>
// The script module exports `run(page)` where page offers evaluate(js), screenshot(path), wait(ms), log(...).
import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const [,, chrome, url, scriptPath] = process.argv;
const port = 9333;
const proc = spawn(chrome, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  `--remote-debugging-port=${port}`, '--window-size=1400,1000', '--hide-scrollbars', 'about:blank',
], { stdio: ['ignore', 'ignore', 'pipe'] });
let stderr = '';
proc.stderr.on('data', d => { stderr += d; });

async function waitForTarget() {
  for (let i = 0; i < 50; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
      const page = list.find(t => t.type === 'page');
      if (page) return page;
    } catch { /* not up yet */ }
    await new Promise(r => setTimeout(r, 200));
  }
  throw new Error('chrome did not start: ' + stderr);
}

let nextId = 1;
const pending = new Map();
const events = [];
let ws;

function send(method, params = {}) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

async function main() {
  const target = await waitForTarget();
  ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = reject; });
  ws.onmessage = (e) => {
    const msg = JSON.parse(e.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(JSON.stringify(msg.error))); else resolve(msg.result);
    } else if (msg.method) {
      events.push(msg);
      if (msg.method === 'Runtime.consoleAPICalled') {
        const text = (msg.params.args || []).map(a => a.value ?? a.description ?? '').join(' ');
        if (msg.params.type === 'error' || msg.params.type === 'warning') console.log('  [console.' + msg.params.type + ']', text);
      }
      if (msg.method === 'Runtime.exceptionThrown')
        console.log('  [exception]', msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text);
    }
  };
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1400, height: 1000, deviceScaleFactor: 1, mobile: false });

  const page = {
    async goto(u) { await send('Page.navigate', { url: u }); await this.wait(800); },
    async evaluate(expression) {
      const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (r.exceptionDetails) throw new Error('evaluate failed: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
      return r.result.value;
    },
    async screenshot(path, fullPage = false) {
      if (fullPage) {
        const metrics = await send('Page.getLayoutMetrics');
        const h = Math.min(Math.ceil(metrics.cssContentSize.height), 6000);
        await send('Emulation.setDeviceMetricsOverride', { width: 1400, height: h, deviceScaleFactor: 1, mobile: false });
        await this.wait(200);
      }
      const shot = await send('Page.captureScreenshot', { format: 'png' });
      writeFileSync(path, Buffer.from(shot.data, 'base64'));
      if (fullPage) await send('Emulation.setDeviceMetricsOverride', { width: 1400, height: 1000, deviceScaleFactor: 1, mobile: false });
    },
    wait(ms) { return new Promise(r => setTimeout(r, ms)); },
    log(...args) { console.log(...args); },
  };

  await page.goto(url);
  const mod = await import(pathToFileURL(scriptPath).href);
  try {
    await mod.run(page);
  } finally {
    ws.close();
    proc.kill();
  }
}

main().catch(e => { console.error('FAILED:', e.message); proc.kill(); process.exit(1); });
