// Captures screenshots of the running BiteBox app for the project report.
// Needs: API on :5080 and `ng serve` on :4200, plus Google Chrome. No npm packages: it talks to
// Chrome through the DevTools Protocol, using Node's built-in fetch and WebSocket.
// Usage: node docs/report/capture-screenshots.mjs
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync, mkdtempSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = join(here, 'screenshots');
mkdirSync(outDir, { recursive: true });
const WEB = 'http://localhost:4200';
const CHROME = process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9333;
const devSettings = JSON.parse(readFileSync(join(here, '../../backend/src/BiteBox.API/appsettings.Development.json'), 'utf8'));
const DEMO_PASSWORD = devSettings.BiteBox?.Seed?.DemoPassword ?? devSettings.Seed?.DemoPassword;
if (!DEMO_PASSWORD) throw new Error('Demo password not found in appsettings.Development.json');

const sleep = ms => new Promise(r => setTimeout(r, ms));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${mkdtempSync(join(tmpdir(), 'bb-shot-'))}`,
  '--no-first-run', '--no-default-browser-check', '--hide-scrollbars', 'about:blank',
], { stdio: 'ignore' });

let ws, nextId = 1;
const pending = new Map();
function send(method, params = {}) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

async function connect() {
  for (let i = 0; i < 50; i++) {
    try {
      const targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const page = targets.find(t => t.type === 'page');
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* chrome still starting */ }
    await sleep(200);
  }
  throw new Error('Chrome did not start');
}

async function viewport(width, height, mobile = false) {
  await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: mobile ? 2 : 1.5, mobile });
}
async function go(path, wait = 1800) {
  await send('Page.navigate', { url: WEB + path });
  await sleep(wait);
}
async function evaluate(expression) {
  const r = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(`${expression.slice(0, 60)}… → ${r.exceptionDetails.exception?.description ?? r.exceptionDetails.text}`);
  return r.result.value;
}
async function shot(name, { fullPage = false, maxHeight = 2200, tallViewport = false } = {}) {
  const params = { format: 'jpeg', quality: 86 };
  if (tallViewport) {
    // Grow the viewport itself so 100vh layouts (the admin sidebar) stretch to the full page.
    const h = await evaluate('document.documentElement.scrollHeight');
    await viewport(1440, Math.min(h, maxHeight));
    await sleep(700);
  } else if (fullPage) {
    const h = await evaluate('document.documentElement.scrollHeight');
    const w = await evaluate('document.documentElement.clientWidth');
    params.captureBeyondViewport = true;
    params.clip = { x: 0, y: 0, width: w, height: Math.min(h, maxHeight), scale: 1 };
  }
  const { data } = await send('Page.captureScreenshot', params);
  writeFileSync(join(outDir, `${name}.jpg`), Buffer.from(data, 'base64'));
  if (tallViewport) await viewport(1440, 900);
  console.log('saved', name);
}

// In-page helpers: obtain a CSRF token, then call the API exactly like the Angular app does.
const api = `
  window.__bb = {
    token: async () => { await fetch('/api/v1/auth/csrf', { credentials: 'include' });
      return decodeURIComponent((document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/) || [,''])[1]); },
    call: async (method, url, body) => { const t = await window.__bb.token();
      const r = await fetch(url, { method, credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': t }, body: body ? JSON.stringify(body) : undefined });
      const text = await r.text(); return { status: r.status, body: text ? JSON.parse(text) : null }; },
  };`;
async function login(email) {
  await evaluate(api);
  const r = await evaluate(`window.__bb.call('POST', '/api/v1/auth/login', { email: ${JSON.stringify(email)}, password: ${JSON.stringify(DEMO_PASSWORD)}, rememberMe: false }).then(r => r.status)`);
  if (r !== 200) throw new Error(`login ${email} failed: ${r}`);
}
async function logout() {
  await send('Network.clearBrowserCookies');
  await evaluate('localStorage.clear(); sessionStorage.clear(); true');
}

try {
  ws = new WebSocket(await connect());
  await new Promise(r => ws.addEventListener('open', r, { once: true }));
  ws.addEventListener('message', e => {
    const msg = JSON.parse(e.data);
    if (msg.id && pending.has(msg.id)) {
      const p = pending.get(msg.id); pending.delete(msg.id);
      msg.error ? p.reject(new Error(msg.error.message)) : p.resolve(msg.result);
    }
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
  await viewport(1440, 900);

  // ---- Guest storefront ----
  await go('/', 2500);
  await shot('01-home', { fullPage: true, maxHeight: 2600 });
  await go('/menu', 2200);
  await shot('02-menu');
  await go('/menu?veg=true&sort=price-asc&q=paneer', 2200);
  await shot('03-menu-filtered');
  await go('/menu/paneer-butter-masala', 2200);
  await shot('04-dish-detail', { fullPage: true, maxHeight: 1700 });

  // BiteBot: open the panel, type a request, send it.
  await go('/menu', 2000);
  await evaluate(`document.querySelector('button.launcher').click(); true`);
  await sleep(600);
  await evaluate(`(() => { const i = document.querySelector('input[name=msg]'); i.value = 'spicy veg dinner for two under 600'; i.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`);
  await sleep(300);
  await evaluate(`document.querySelector('button.send').click(); true`);
  await sleep(2500);
  await shot('05-bitebot');

  await go('/login', 1500);
  await shot('06-login');

  // ---- Customer ----
  await go('/', 1500);
  await login('priya@example.com');
  const products = await evaluate(`fetch('/api/v1/products?pageSize=60').then(r => r.json())`);
  const items = products.items ?? products;
  const pick = slug => items.find(p => p.slug === slug)?.id;
  const added = [];
  for (const [slug, qty] of [['paneer-butter-masala', 2], ['veg-dum-biryani', 1], ['classic-cold-coffee', 2]]) {
    const id = pick(slug);
    if (!id) continue;
    const r = await evaluate(`window.__bb.call('POST', '/api/v1/cart/items', { productId: ${id}, quantity: ${qty} }).then(r => r.status)`);
    if (r === 200) added.push(id);
  }
  await go('/cart', 2200);
  await shot('07-cart');
  await go('/checkout', 2800);
  await shot('08-checkout', { fullPage: true, maxHeight: 2000 });
  await go('/account/orders', 2200);
  await shot('09-my-orders');
  await evaluate(api);
  const mine = await evaluate(`window.__bb.call('GET', '/api/v1/orders?page=1&pageSize=20').then(r => r.body)`);
  const delivered = (mine.items ?? []).find(o => o.status === 'Delivered') ?? (mine.items ?? [])[0];
  if (delivered) {
    await go(`/account/orders/${delivered.orderNumber}`, 2500);
    await evaluate(`document.querySelectorAll('details').forEach(d => d.open = true); true`);
    await sleep(400);
    await shot('10-order-tracking', { fullPage: true, maxHeight: 2000 });
  }
  // Leave Priya's cart as it was.
  await evaluate(api);
  for (const id of added) await evaluate(`window.__bb.call('PUT', '/api/v1/cart/items/${id}', { quantity: 0 }).then(r => r.status)`);
  await logout();

  // ---- Admin ----
  await go('/', 1200);
  await login('admin@bitebox.dev');
  await go('/admin', 3000);
  await shot('11-admin-dashboard', { tallViewport: true, maxHeight: 2600 });
  for (const [path, name] of [
    ['/admin/orders', '12-admin-orders'], ['/admin/products', '13-admin-products'], ['/admin/inventory', '14-admin-inventory'],
    ['/admin/coupons', '15-admin-promotions'], ['/admin/users', '16-admin-people'], ['/admin/reports', '17-admin-reports'],
  ]) { await go(path, 2500); await shot(name); }
  await logout();

  // ---- Kitchen view of the same queue ----
  await go('/', 1200);
  await login('kitchen@bitebox.dev');
  await go('/admin/orders', 2500);
  await shot('18-kitchen-queue');
  await logout();

  // ---- Mobile + Swagger ----
  await viewport(390, 844, true);
  await go('/', 2500);
  await shot('19-mobile-home');
  await go('/menu', 2200);
  await shot('20-mobile-menu');
  await viewport(1440, 900);
  await send('Page.navigate', { url: 'http://localhost:5080/swagger' });
  await sleep(2500);
  await shot('21-swagger');
} finally {
  ws?.close();
  chrome.kill();
}
