// Usage: node render.mjs <scene.json relative to this folder> <output prefix> [width height]
// Serves this folder and the repository (/repo/...) to viewer.html in headless Chromium (SwiftShader WebGL) and
// saves one PNG per camera. build.py --preview writes the scene JSON and decoration GLBs here and runs it.
// Environment: REPO (default: the repository containing this folder), CHROMIUM (browser executable).
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = process.env.REPO || path.resolve(here, '../../../..');
const [scene, prefix, w = '1600', h = '1000'] = process.argv.slice(2);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.glb': 'model/gltf-binary', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  const file = url.startsWith('/repo/') ? path.join(repo, url.slice(6)) : path.join(here, url);
  fs.readFile(file, (err, data) => {
    if (err) { if (!url.endsWith('favicon.ico')) console.error('404:', url); res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(data);
  });
});
await new Promise(r => server.listen(0, r));
const port = server.address().port;
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM || '/opt/pw-browsers/chromium-1194/chrome-linux/chrome',
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--disable-gpu-sandbox'] });
const page = await browser.newPage({ viewport: { width: +w, height: +h } });
page.on('console', m => { if (m.type() === 'error') console.error('page:', m.text()); });
await page.goto(`http://127.0.0.1:${port}/viewer.html?scene=${encodeURIComponent(scene)}&w=${w}&h=${h}`);
await page.waitForFunction(() => window.__done === true, null, { timeout: 900000, polling: 500 });
const error = await page.evaluate(() => window.__error);
if (error) { console.error(error); process.exitCode = 1; }
const results = await page.evaluate(() => window.__results || []);
for (const r of results) {
  const out = `${prefix}_${r.name}.png`;
  fs.writeFileSync(out, Buffer.from(r.png.split(',')[1], 'base64'));
  console.log('wrote', out);
}
await browser.close();
server.close();
