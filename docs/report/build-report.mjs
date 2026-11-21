// Builds docs/BiteBox-Project-Report.pdf from the Markdown sources in this folder plus docs/BiteBox-SRS.md.
//   node docs/report/build-report.mjs
// Uses libraries already on the machine (no install): `marked` and `mermaid` from a globally installed npm
// package (override with BB_NODE_MODULES), and Google Chrome's DevTools Protocol for printing.
// Two passes: pass 1 prints the PDF, reads which page each heading landed on, and pass 2 prints again with
// those page numbers in the table of contents.
import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync, readdirSync, mkdtempSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { inflateSync } from 'node:zlib';
import { createRequire } from 'node:module';

const here = dirname(fileURLToPath(import.meta.url));
const docs = join(here, '..');
const NM = process.env.BB_NODE_MODULES ?? 'C:/Users/Aman Rastogi/AppData/Roaming/npm/node_modules/omniroute/node_modules';
const CHROME = process.env.CHROME_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const require = createRequire(import.meta.url);
const { marked } = await import(pathToFileURL(join(NM, 'marked/lib/marked.esm.js')).href);
const mermaidJs = readFileSync(join(NM, 'mermaid/dist/mermaid.min.js'), 'utf8');
const meta = JSON.parse(readFileSync(join(here, 'report-meta.json'), 'utf8'));
// Output: --out <folder> (default: docs/)
const outArg = process.argv.indexOf('--out');
const OUT_PDF = join(outArg > 0 ? process.argv[outArg + 1] : docs, 'BiteBox-Project-Report.pdf');
const OUT_HTML = join(here, 'BiteBox-Project-Report.html');

// ---------- Markdown → HTML ----------
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
marked.use({
  gfm: true,
  renderer: {
    code({ text, lang }) {
      if (lang === 'mermaid') {
        // Portrait pages: lay wide diagrams out vertically so their text stays readable.
        if (/^erDiagram/.test(text)) text = text.replace(/^erDiagram/, 'erDiagram\n  direction LR');
        if (/^flowchart LR\s*\n\s*subgraph/.test(text)) text = text.replace(/^flowchart LR/, 'flowchart TB');
      }
      if (lang === 'mermaid') return `<figure class="diagram"><pre class="mermaid">${esc(text)}</pre></figure>`;
      return `<pre class="code"><code>${esc(text)}</code></pre>`;
    },
  },
});

const usedIds = new Set();
function slug(text) {
  let s = text.toLowerCase().replace(/<[^>]+>/g, '').replace(/&amp;/g, '&').replace(/&[a-z]+;|&#\d+;/g, '')
    .replace(/[^\p{L}\p{N}\s-]/gu, '').trim().replace(/\s/g, '-');
  let id = s, n = 1;
  while (usedIds.has(id)) id = `${s}-${n++}`;
  usedIds.add(id);
  return id;
}

const toc = []; // { level, id, html, part }
function render(md, part, { tocDepth = 3 } = {}) {
  let html = marked.parse(md);
  html = html.replace(/<h([1-4])>([\s\S]*?)<\/h\1>/g, (_, lvl, inner) => {
    const id = slug(inner);
    if (+lvl <= tocDepth) toc.push({ level: +lvl, id, html: inner.replace(/<a [^>]*>|<\/a>/g, ''), part });
    return `<h${lvl} id="${id}">${inner}</h${lvl}>`;
  });
  return html;
}

// Report chapters
const intro = render(readFileSync(join(here, 'report-intro.md'), 'utf8'), 'I');

const captions = {
  '01-home': ['Home page', 'Hero, featured dishes, categories and today\'s offers. The page is the public entry point and works without signing in.'],
  '02-menu': ['Menu page', 'Category chips, veg-only, in-stock, price and spice filters with sorting. Filters are stored in the URL so a filtered view can be shared.'],
  '03-menu-filtered': ['Search and filters', 'A vegetarian search for "paneer" sorted by price: the query runs on the server through one composable IQueryable pipeline.'],
  '04-dish-detail': ['Dish detail', 'Veg/non-veg mark, spice level, prep time, servings, calories, rating, stock warning, reviews from verified buyers and related dishes.'],
  '05-bitebot': ['BiteBot AI assistant', '"spicy veg dinner for two under 600": BiteBot returns only real, in-stock vegetarian dishes from the database and a combo that fits the budget, each with an add-to-cart button.'],
  '06-login': ['Sign in', 'Email and password sign-in. The session is an HttpOnly cookie; failed attempts get a generic message and repeated failures lock the account.'],
  '07-cart': ['Cart', 'Server-side cart for a signed-in customer (a guest cart lives in the browser and is merged on sign-in). Quantities are limited by available stock.'],
  '08-checkout': ['Checkout', 'Delivery or pickup, saved address, payment method and coupon. The order summary is a live quote calculated by the API: item total, discount, delivery fee and 5% GST.'],
  '09-my-orders': ['My orders', 'Order history with status badges, totals and quick links to track or reorder.'],
  '10-order-tracking': ['Order detail and tracking', 'Price breakdown snapshot (with the BITE20 coupon), payment status, delivery address snapshot and the full status history timeline.'],
  '11-admin-dashboard': ['Admin dashboard', 'KPIs (revenue, average order value, active, completed, new customers), revenue per day, top dishes, orders by status, low stock and latest orders for any date range.'],
  '12-admin-orders': ['Order management (admin)', 'All orders with filters and search. The action buttons are generated from the order state machine, so only legal next steps are offered.'],
  '13-admin-products': ['Menu items', 'Create, edit, feature, hide and soft-delete dishes; photo upload with file-signature validation.'],
  '14-admin-inventory': ['Inventory', 'Stock per dish with restock, wastage and correction actions. Every change is written to an inventory ledger.'],
  '15-admin-promotions': ['Promotions', 'Percentage or flat coupons with minimum order, maximum discount, usage limits and validity windows.'],
  '16-admin-people': ['Customers and staff', 'User list with roles, block/unblock and staff creation.'],
  '17-admin-reports': ['Reports', 'Sales and product reports with CSV export.'],
  '18-kitchen-queue': ['Kitchen view', 'The same order screen signed in as Kitchen: the menu only shows what this role may use and the live queue shows active orders.'],
  '19-mobile-home': ['Mobile: home', 'The storefront is responsive down to 360 px phones.'],
  '20-mobile-menu': ['Mobile: menu', 'Menu cards reflow into a single column on small screens.'],
  '21-swagger': ['Swagger / OpenAPI', 'Interactive documentation of every REST endpoint, generated from the controllers.'],
};
const shotsDir = join(here, 'screenshots');
const shots = existsSync(shotsDir) ? readdirSync(shotsDir).filter(f => /\.(png|jpe?g)$/.test(f)).sort() : [];
let shotsMd = `# Chapter 2: Application screenshots\n\nThe screenshots below were captured from the running application (Angular dev server + ASP.NET Core API + MySQL with demo data) on ${meta.date}, using the script \`docs/report/capture-screenshots.mjs\`.\n\n`;
let figNo = 0;
const shotsHtmlParts = [];
const shotsIntro = render(shotsMd, 'I');
for (const f of shots) {
  const key = f.replace(/\.(png|jpe?g)$/, '');
  const [title, text] = captions[key] ?? [key, ''];
  figNo++;
  const mobile = key.includes('mobile');
  shotsHtmlParts.push(`<figure class="shot${mobile ? ' mobile' : ''}"><img src="${pathToFileURL(join(shotsDir, f)).href}" alt="${esc(title)}"><figcaption><b>Figure 2.${figNo}: ${esc(title)}.</b> ${esc(text)}</figcaption></figure>`);
}
const shotsHtml = shotsIntro + `<div class="shots">${shotsHtmlParts.join('\n')}</div>`;

// The SRS body: drop its own title block and table of contents.
let srs = readFileSync(join(docs, 'BiteBox-SRS.md'), 'utf8');
srs = srs.slice(srs.indexOf('## 1. Introduction')).replace(/\n---\s*\n\*End of document\.\*\s*$/, '\n');
const srsDivider = render('# Software Requirements Specification and System Design', 'II', { tocDepth: 1 });
// Wide sequence diagrams get their own landscape page (with the heading above them).
const srsHtml = render(srs, 'II').replace(
  /(<h[23] id="[^"]+">[^\n]*<\/h[23]>\s*)(<figure class="diagram"><pre class="mermaid">sequenceDiagram[\s\S]*?<\/figure>)/g,
  '<section class="landscape">$1$2</section>');

const conclusion = render(readFileSync(join(here, 'report-conclusion.md'), 'utf8'), 'III', { tocDepth: 2 });

// ---------- Page assembly ----------
const field = (label, value) => `<tr><th>${label}</th><td>${value ? esc(value) : '<span class="blank"></span>'}</td></tr>`;
const cover = `
<section class="cover">
  <div class="cover-top">
    <div class="logo">${logoSvg()}</div>
    <p class="kicker">Project Report</p>
    <h1 class="cover-title">${esc(meta.title)}</h1>
    <p class="cover-sub">${esc(meta.subtitle)}</p>
    <p class="cover-tag">“${esc(meta.tagline)}”</p>
  </div>
  <p class="cover-includes">Including the Software Requirements Specification (IEEE 830 / ISO/IEC/IEEE 29148 structure), system architecture, backend &amp; frontend code walkthrough, database design, testing and deployment.</p>
  <table class="cover-meta">
    ${field('Submitted by', meta.submittedBy)}
    ${'SAP ID' in meta ? field('SAP ID', meta['SAP ID']) : field('Enrolment / Roll no.', meta.enrolmentNo)}
    ${field('Course', meta.course)}
    ${field('Institution', meta.institution)}
    ${'guide' in meta ? field('Project guide', meta.guide) : ''}
    ${field('Session', meta.session)}
    ${field('Date', meta.date)}
  </table>
  <p class="cover-stack">Angular 22 · ASP.NET Core 9 · Entity Framework Core 9 · MySQL 8 · Claude (optional AI)</p>
</section>`;

function logoSvg() {
  return `<svg viewBox="0 0 48 48" width="64" height="64" aria-hidden="true"><rect x="4" y="12" width="40" height="32" rx="9" fill="#E8542A"/><path d="M16 14v-2a8 8 0 0 1 16 0v2" fill="none" stroke="#E8542A" stroke-width="4" stroke-linecap="round"/><path d="M17 27a7 7 0 0 0 14 0" fill="none" stroke="#fff" stroke-width="4" stroke-linecap="round"/></svg>`;
}

function tocHtml(pages) {
  const partTitles = { I: 'Part I: Project report', II: 'Part II: SRS and system design', III: 'Part III: Conclusion' };
  let html = '<section class="toc-page"><h1 class="toc-title">Contents</h1><ol class="toc">';
  for (const e of toc) {
    const p = pages?.[e.id] ?? '';
    html += `<li class="l${e.level}${e.part === 'II' && e.level === 1 ? ' part' : ''}"><a href="#${e.id}"><span class="t">${e.html}</span><span class="dots"></span><span class="pg">${p}</span></a></li>`;
  }
  return html + '</ol></section>';
}

const css = `
@page { size: A4; margin: 20mm 18mm 20mm 18mm;
  @bottom-center { content: counter(page); font: 9pt "Segoe UI", sans-serif; color: #8a7f78; }
  @top-right { content: "BiteBox · Project Report"; font: 8pt "Segoe UI", sans-serif; color: #b0a59e; } }
@page wide { size: A4 landscape; }
.landscape { page: wide; }
.landscape figure.diagram svg { max-height: 150mm; }
@page :first { @bottom-center { content: none; } @top-right { content: none; } }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { font: 10.5pt/1.55 "Segoe UI", "Nirmala UI", Arial, sans-serif; color: #26201c; margin: 0; }
h1, h2, h3, h4 { font-family: Cambria, Georgia, "Times New Roman", serif; color: #1d1714; line-height: 1.25; break-after: avoid; }
h1 { font-size: 22pt; margin: 0 0 6mm; padding-bottom: 3mm; border-bottom: 2.5pt solid #E8542A; break-before: page; }
h2 { font-size: 15.5pt; margin: 8mm 0 3mm; }
h3 { font-size: 12.5pt; margin: 6mm 0 2mm; color: #3a2c25; }
h4 { font-size: 11pt; margin: 4mm 0 1.5mm; }
#software-requirements-specification-and-system-design { font-size: 26pt; border: none; text-align: center; margin-top: 90mm; }
.part-note { text-align: center; color: #6b5f58; max-width: 130mm; margin: 0 auto; }
.srs > h2 { break-before: page; font-size: 18pt; border-bottom: 1.2pt solid #f0d5ca; padding-bottom: 2mm; margin-top: 0; }
p, li { orphans: 3; widows: 3; }
a { color: #b8401c; text-decoration: none; }
strong { color: #1d1714; }
hr { display: none; }
code { font-family: "Cascadia Code", Consolas, monospace; font-size: 8.8pt; background: #f6efea; padding: 0.4pt 2.5pt; border-radius: 2.5pt; }
pre.code { background: #1f1a17; color: #f4ece6; padding: 3mm 4mm; border-radius: 3mm; font-size: 8.3pt; line-height: 1.45;
  white-space: pre-wrap; word-break: break-word; break-inside: avoid; }
pre.code code { background: none; color: inherit; padding: 0; font-size: inherit; }
blockquote { margin: 3mm 0; padding: 2.5mm 4mm; background: #fff4ee; border-left: 3pt solid #E8542A; border-radius: 0 2mm 2mm 0; color: #4a3d36; }
blockquote p { margin: 1mm 0; }
table { width: 100%; border-collapse: collapse; margin: 3mm 0 4mm; font-size: 9pt; line-height: 1.4; }
th, td { border: 0.6pt solid #e4d8d0; padding: 1.6mm 2.2mm; text-align: left; vertical-align: top; overflow-wrap: break-word; }
th { background: #fbeee7; font-weight: 600; color: #3a2c25; }
tr { break-inside: avoid; }
thead { display: table-header-group; }
figure.diagram { margin: 4mm 0; text-align: center; break-inside: avoid; }
figure.diagram svg { max-width: 100% !important; height: auto; max-height: 215mm; }
pre.mermaid { background: none; }
/* cover */
.cover { height: 257mm; display: flex; flex-direction: column; justify-content: space-between; text-align: center;
  border: 1.5pt solid #E8542A; border-radius: 6mm; padding: 18mm 16mm; background: linear-gradient(180deg, #fff6f1 0%, #ffffff 55%); }
.cover .kicker { letter-spacing: 3pt; text-transform: uppercase; color: #b8401c; font-weight: 600; font-size: 10pt; margin: 4mm 0 0; }
.cover-title { font-family: Cambria, Georgia, serif; font-size: 50pt; border: none; margin: 2mm 0 0; padding: 0; break-before: avoid; color: #E8542A; }
.cover-sub { font-family: Cambria, Georgia, serif; font-size: 17pt; margin: 2mm 0; color: #1d1714; }
.cover-tag { font-style: italic; color: #6b5f58; font-size: 12pt; }
.cover-includes { color: #4a3d36; font-size: 10pt; }
.cover-meta { width: 125mm; margin: 0 auto; font-size: 11pt; }
.cover-meta th, .cover-meta td { border: none; border-bottom: 0.6pt solid #ead9cf; background: none; padding: 2.2mm 2mm; }
.cover-meta th { width: 48mm; color: #6b5f58; font-weight: 500; }
.cover-meta .blank { display: inline-block; width: 70mm; }
.cover-stack { color: #8a7f78; font-size: 9pt; letter-spacing: 0.3pt; }
/* toc */
.toc-title { break-before: page; }
.toc { list-style: none; padding: 0; margin: 0; font-size: 10pt; }
.toc li a { display: flex; align-items: baseline; color: #26201c; }
.toc .t { flex: 0 1 auto; }
.toc .dots { flex: 1 1 auto; border-bottom: 0.8pt dotted #c9b9ae; margin: 0 2mm; transform: translateY(-1mm); min-width: 6mm; }
.toc .pg { flex: 0 0 auto; min-width: 8mm; text-align: right; font-variant-numeric: tabular-nums; }
.toc .l1 { font-weight: 700; margin-top: 3mm; font-size: 11pt; }
.toc .l1.part { margin-top: 5mm; color: #b8401c; }
.toc .l2 { padding-left: 5mm; margin-top: 0.8mm; }
.toc .l3 { padding-left: 11mm; font-size: 9pt; color: #4a3d36; }
.toc code { font-size: 8pt; }
/* screenshots */
.shots figure.shot { margin: 0 0 7mm; break-inside: avoid; text-align: center; }
.shots img { max-width: 100%; max-height: 205mm; border: 0.6pt solid #e4d8d0; border-radius: 2mm; box-shadow: 0 1mm 3mm rgba(0,0,0,0.08); }
.shots figure.mobile { display: inline-block; width: 48%; vertical-align: top; margin: 0 0.8% 7mm; }
.shots figure.mobile img { max-height: 190mm; }
figcaption { font-size: 9pt; color: #4a3d36; margin-top: 2mm; text-align: left; }
`;

function buildHtml(pages) {
  return `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>BiteBox: Project Report</title><style>${css}</style></head><body>
${cover}
${tocHtml(pages)}
<main>
${intro}
${shotsHtml}
${srsDivider}
<p class="part-note">The following sections form the Software Requirements Specification (structured after IEEE 830 / ISO/IEC/IEEE 29148) together with the complete system design: architecture, backend and frontend code walkthrough, database design, key algorithms, security, testing, deployment and requirement traceability.</p>
<div class="srs">${srsHtml}</div>
${conclusion}
</main>
<script>${mermaidJs}</script>
<script>
  mermaid.initialize({ startOnLoad: false, theme: 'base', securityLevel: 'loose', flowchart: { htmlLabels: true, useMaxWidth: true },
    themeVariables: { primaryColor: '#fde7dd', primaryBorderColor: '#E8542A', primaryTextColor: '#1d1714', lineColor: '#7a6a62',
      secondaryColor: '#f5efe9', tertiaryColor: '#ffffff', fontFamily: 'Segoe UI, sans-serif', fontSize: '14px' } });
  mermaid.run({ querySelector: 'pre.mermaid' })
    .then(() => document.fonts.ready)
    .then(() => { window.__ready = true; }, e => { window.__mermaidError = (e && (e.message || e.str)) || JSON.stringify(e); window.__ready = true; });
</script>
</body></html>`;
}

// ---------- Chrome (DevTools Protocol) ----------
const sleep = ms => new Promise(r => setTimeout(r, ms));
const PORT = 9334;
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${mkdtempSync(join(tmpdir(), 'bb-pdf-'))}`,
  '--no-first-run', '--allow-file-access-from-files', 'about:blank'], { stdio: 'ignore' });
let ws, nextId = 1;
const pending = new Map();
const send = (method, params = {}) => { const id = nextId++; ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject })); };

async function printPdf(html) {
  writeFileSync(OUT_HTML, html);
  await send('Page.navigate', { url: pathToFileURL(OUT_HTML).href });
  for (let i = 0; i < 300; i++) {
    await sleep(200);
    const r = await send('Runtime.evaluate', { expression: 'window.__ready === true', returnByValue: true });
    if (r.result.value) break;
  }
  const err = (await send('Runtime.evaluate', { expression: 'window.__mermaidError || null', returnByValue: true })).result.value;
  if (err) console.warn('Mermaid warning:', err);
  const { data } = await send('Page.printToPDF', { printBackground: true, preferCSSPageSize: true, generateDocumentOutline: true,
    generateTaggedPDF: true, transferMode: 'ReturnAsBase64' });
  return Buffer.from(data, 'base64');
}

// Find which page each named destination (heading id) is on, by reading the PDF's objects.
function headingPages(pdf) {
  const text = pdf.toString('latin1');
  const objects = new Map();
  for (const m of text.matchAll(/(\d+) 0 obj\s*([\s\S]*?)endobj/g)) objects.set(+m[1], m[2]);
  // Expand object streams (PDF 1.5+) if present.
  for (const [, body] of objects) {
    if (!/\/Type\s*\/ObjStm/.test(body)) continue;
    const n = +body.match(/\/N (\d+)/)[1], first = +body.match(/\/First (\d+)/)[1];
    const start = body.indexOf('stream') + 6;
    let s = body.slice(start).replace(/^\r?\n/, '');
    s = s.slice(0, s.lastIndexOf('endstream'));
    let raw;
    try { raw = inflateSync(Buffer.from(s, 'latin1')).toString('latin1'); } catch { continue; }
    const head = raw.slice(0, first).trim().split(/\s+/).map(Number);
    for (let i = 0; i < n; i++) {
      const num = head[2 * i], off = head[2 * i + 1], next = i + 1 < n ? head[2 * i + 3] : raw.length - first;
      objects.set(num, raw.slice(first + off, first + next));
    }
  }
  // Page order from the page tree.
  const catalog = [...objects.values()].find(b => /\/Type\s*\/Catalog/.test(b));
  const rootPages = +catalog.match(/\/Pages (\d+) 0 R/)[1];
  const order = [];
  (function walk(num) {
    const b = objects.get(num);
    if (/\/Type\s*\/Pages/.test(b)) for (const k of b.match(/\/Kids\s*\[([^\]]*)\]/)[1].matchAll(/(\d+) 0 R/g)) walk(+k[1]);
    else order.push(num);
  })(rootPages);
  const pageIndex = new Map(order.map((n, i) => [n, i + 1]));
  // Named destinations: /Dests dictionary or name tree.
  const pages = {};
  const decodeName = s => s.replace(/#([0-9a-fA-F]{2})/g, (_, h) => String.fromCharCode(parseInt(h, 16)));
  const decodeStr = s => s.replace(/\\(\d{3}|.)/g, (_, c) => c.length === 3 ? String.fromCharCode(parseInt(c, 8)) : c);
  const collect = body => {
    for (const m of body.matchAll(/\/([^\s\/\[\]<>()]+)\s*\[\s*(\d+) 0 R\s*\/XYZ/g)) pages[decodeName(m[1])] ??= pageIndex.get(+m[2]);
    for (const m of body.matchAll(/\(((?:\\.|[^\\)])*)\)\s*\[\s*(\d+) 0 R\s*\/XYZ/g)) pages[decodeStr(m[1])] ??= pageIndex.get(+m[2]);
  };
  for (const b of objects.values()) collect(b);
  // Destinations may be stored as indirect arrays: resolve "(name) N 0 R" / "/name N 0 R" pairs.
  for (const b of objects.values()) {
    for (const m of b.matchAll(/(?:\/([^\s\/\[\]<>()]+)|\(((?:\\.|[^\\)])*)\))\s+(\d+) 0 R/g)) {
      const target = objects.get(+m[3]);
      const d = target?.match(/^\s*\[\s*(\d+) 0 R\s*\/XYZ/);
      if (d) { const name = m[1] ? decodeName(m[1]) : decodeStr(m[2]); pages[name] ??= pageIndex.get(+d[1]); }
    }
  }
  return { pages, total: order.length };
}

try {
  for (let i = 0; i < 50 && !ws; i++) {
    try {
      const page = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find(t => t.type === 'page');
      if (page) ws = new WebSocket(page.webSocketDebuggerUrl);
    } catch { await sleep(200); }
  }
  await new Promise(r => ws.addEventListener('open', r, { once: true }));
  ws.addEventListener('message', e => { const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(m.error.message)) : p.resolve(m.result); } });
  await send('Page.enable'); await send('Runtime.enable');

  // Pass 1 uses placeholder page numbers of the final width so the layout doesn't shift in pass 2.
  const placeholder = Object.fromEntries(toc.map(e => [e.id, '000']));
  const first = await printPdf(buildHtml(placeholder));
  const { pages, total } = headingPages(first);
  const found = toc.filter(e => pages[e.id]).length;
  console.log(`pass 1: ${total} pages, located ${found}/${toc.length} headings`);
  const final = await printPdf(buildHtml(found ? pages : null));
  writeFileSync(OUT_PDF, final);
  console.log(`wrote ${OUT_PDF} (${(final.length / 1024 / 1024).toFixed(1)} MB, ${headingPages(final).total} pages)`);
} finally {
  ws?.close();
  chrome.kill();
}
