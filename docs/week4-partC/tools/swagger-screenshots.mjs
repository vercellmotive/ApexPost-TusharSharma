// Takes the Swagger UI screenshots from the real, secured API, driving headless Chrome through the DevTools protocol.
// Needs Node 22+ (built-in WebSocket) and Chrome. Start the API in Development over HTTPS first (see README.md,
// Part D, for the two user secrets), then:
//   node docs/week4-partC/tools/swagger-screenshots.mjs https://localhost:7180 docs/week4-partC
// Chrome is started with --ignore-certificate-errors, which affects only this throwaway headless profile, because the
// ASP.NET Core development certificate is not trusted by it. The API itself never skips certificate checks.
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const [base = 'https://localhost:7180', outDir = 'docs/week4-partC'] = process.argv.slice(2);
const chromePath = process.env.CHROME ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const port = 9333;
const chrome = spawn(chromePath, [
  '--headless=new', '--ignore-certificate-errors', `--remote-debugging-port=${port}`, '--window-size=1400,1000', '--hide-scrollbars',
  `--user-data-dir=${mkdtempSync(join(tmpdir(), 'apexpost-chrome-'))}`, 'about:blank',
], { stdio: 'ignore' });

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
let target;
for (let attempt = 0; attempt < 50 && !target; attempt++) {
  await sleep(200);
  try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find(item => item.type === 'page'); } catch { }
}

const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(resolve => socket.addEventListener('open', resolve));
let nextId = 0;
const pending = new Map();
socket.addEventListener('message', event => {
  const message = JSON.parse(event.data);
  if (message.id && pending.has(message.id)) { pending.get(message.id)(message); pending.delete(message.id); }
});
const send = (method, params = {}) => new Promise(resolve => {
  const id = ++nextId; pending.set(id, resolve); socket.send(JSON.stringify({ id, method, params }));
});
const run = async expression => {
  const reply = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
  if (reply.result?.exceptionDetails) throw new Error(JSON.stringify(reply.result.exceptionDetails));
  return reply.result?.result?.value;
};
const waitFor = async (expression, timeout = 10000) => {
  for (let waited = 0; waited < timeout; waited += 200) { if (await run(expression)) return; await sleep(200); }
  throw new Error(`Timed out waiting for ${expression}`);
};
// A signed token is a credential, so it is never left in a screenshot: the page text is masked before each capture.
const maskTokens = () => run(`document.querySelectorAll('pre, .curl, code').forEach(node => {
  node.childNodes.forEach(child => {
    if (child.nodeType === 3) child.textContent = child.textContent.replace(/Bearer [A-Za-z0-9._-]{20,}/g, 'Bearer <access token>');
  });
  node.innerHTML = node.innerHTML.replace(/Bearer [A-Za-z0-9._-]{20,}/g, 'Bearer &lt;access token&gt;');
})`);
const shoot = async (file, selector) => {
  await maskTokens();
  const box = selector
    ? await run(`(() => { const r = document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect(); return { x: r.x, y: r.y + scrollY, width: r.width, height: r.height }; })()`)
    : await run(`({ x: 0, y: 0, width: document.documentElement.scrollWidth, height: Math.min(document.documentElement.scrollHeight, 2200) })`);
  const shot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip: { ...box, scale: 1 } });
  writeFileSync(join(outDir, file), Buffer.from(shot.result.data, 'base64'));
  console.log(`wrote ${file}`);
};
const open = async url => {
  await send('Page.navigate', { url });
  await waitFor(`document.querySelectorAll('.opblock').length > 0`);
  await sleep(500);
};

// Fills a Try it out body and executes it, then returns the selector of that operation's block.
const execute = async (operationId, tag, body) => {
  const block = `#operations-${tag}-${operationId}`;
  await run(`document.querySelector('${block} .opblock-summary-control').click()`);
  await waitFor(`!!document.querySelector('${block} .try-out__btn')`);
  await run(`document.querySelector('${block} .try-out__btn').click()`);
  if (body) {
    await waitFor(`!!document.querySelector('${block} textarea.body-param__text')`);
    await run(`(() => {
      const area = document.querySelector('${block} textarea.body-param__text');
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(area, ${JSON.stringify(JSON.stringify(body, null, 2))});
      area.dispatchEvent(new Event('input', { bubbles: true }));
    })()`);
  }
  await sleep(300);
  await run(`document.querySelector('${block} .execute').click()`);
  await waitFor(`!!document.querySelector('${block} .live-responses-table .response-col_status')`);
  await sleep(500);
  await run(`document.querySelector('${block} .responses-wrapper').scrollIntoView()`);
  return block;
};

await send('Page.enable');
await send('Runtime.enable');

await open(`${base}/swagger/index.html`);

// Without a token the API answers 401 ProblemDetails.
await shoot('response-401-unauthorized.png', await execute('ListShipments', 'Shipments', null) + ' .responses-wrapper');

// Authorize: ask the Development-only token endpoint for a Courier + Admin token and paste it in the dialog.
await open(`${base}/swagger/index.html`);
const token = await run(`fetch('/dev/token', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roles: ['Admin', 'Courier'], courierId: 'CR-1001' }) }).then(r => r.json()).then(j => j.accessToken)`);
await run(`document.querySelector('.btn.authorize').click()`);
await waitFor(`!!document.querySelector('.modal-ux input')`);
await run(`(() => {
  const input = document.querySelector('.modal-ux input[type=text], .modal-ux input:not([type])');
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, ${JSON.stringify(token)});
  input.dispatchEvent(new Event('input', { bubbles: true }));
})()`);
await sleep(300);
await run(`document.querySelector('.modal-ux .auth-btn-wrapper .authorize').click()`);
await sleep(500);
await shoot('swagger-authorize.png', '.modal-ux');
await run(`document.querySelector('.modal-ux .btn-done').click()`);
await sleep(300);

await open(`${base}/swagger/index.html`);
await shoot('swagger-v2.png');
await open(`${base}/swagger/index.html?urls.primaryName=${encodeURIComponent('v1 (deprecated)')}`);
await shoot('swagger-v1.png');

const trackingNumber = `APX-6${String(Date.now()).slice(-7)}`;
const shipment = {
  trackingNumber, originBranchCode: 'B001', recipientName: 'Mia Fictional', recipientPhone: '+61 2 5550 1234',
  recipientAddress: '12 Example Lane', city: 'Sydney', weightKg: 2.5,
};

await open(`${base}/swagger/index.html`);
await shoot('response-201-location.png', await execute('CreateShipment', 'Shipments', shipment) + ' .responses-wrapper');

await open(`${base}/swagger/index.html`);
await shoot('response-409-conflict.png', await execute('CreateShipment', 'Shipments', shipment) + ' .responses-wrapper');

await open(`${base}/swagger/index.html`);
const invalid = { trackingNumber: 'APX-123', originBranchCode: 'B001', recipientPhone: 'call me', recipientAddress: '12 Example Lane', city: 'Sydney', weightKg: 70.5 };
await shoot('response-400-validation.png', await execute('CreateShipment', 'Shipments', invalid) + ' .responses-wrapper');

socket.close();
chrome.kill();
