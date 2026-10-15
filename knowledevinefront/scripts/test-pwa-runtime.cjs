// Run the production Angular worker in a disposable Karma browser and fake server.
// No portal database, account, credential or SMTP connection is used.
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
const { Server, config: karmaConfig } = require('karma');
const root = path.resolve(__dirname, '..');
const dist = path.join(root, 'dist/knowledevinefront/browser');

(async () => {
  const manifest = JSON.parse(await fs.readFile(path.join(dist, 'manifest.webmanifest'), 'utf8'));
  const ngsw = JSON.parse(await fs.readFile(path.join(dist, 'ngsw.json'), 'utf8'));
  assert.equal(manifest.start_url, '/portal/'); assert.equal(manifest.scope, '/portal/'); assert.equal(manifest.display, 'standalone');
  assert.deepEqual(ngsw.dataGroups, []);
  for (const url of Object.keys(ngsw.hashTable)) assert.match(url, /^\/(index\.html|manifest\.webmanifest|[^/]+\.(js|css)|assets\/icons\/[^/]+\.png|assets\/images\/(knowledge-vine-brand\.webp|favicon\.png))$/);
  const state = { offline: false, version: 'a', writes: 0 };
  const mime = { '.js': 'application/javascript', '.css': 'text/css', '.png': 'image/png', '.webp': 'image/webp', '.html': 'text/html', '.webmanifest': 'application/manifest+json' };
  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://localhost');
    response.setHeader('Cache-Control', 'no-store');
    if (url.pathname === '/runtime-control') {
      if (request.method === 'POST') { let body = ''; for await (const chunk of request) body += chunk; Object.assign(state, JSON.parse(body)); }
      response.setHeader('Content-Type', 'application/json'); return response.end(JSON.stringify(state));
    }
    if (state.offline) { response.writeHead(503); return response.end('Temporary simulated connection failure'); }
    if (url.pathname.startsWith('/api/')) {
      if (request.method === 'POST') ++state.writes;
      response.setHeader('Content-Type', 'application/json'); return response.end(JSON.stringify({ note: 'STEP7_PRIVATE_NOTE_NEVER_CACHE', writes: state.writes }));
    }
    if (url.pathname === '/portal/runtime-probe.html') { response.setHeader('Content-Type', 'text/html'); return response.end('<!doctype html><title>Disposable worker client</title><body>Worker test client</body>'); }
    if (url.pathname === '/ngsw.json') {
      response.setHeader('Content-Type', 'application/json'); return response.end(JSON.stringify({ ...ngsw, appData: { testVersion: state.version } }));
    }
    const filename = path.join(dist, url.pathname.startsWith('/portal') ? 'index.html' : url.pathname);
    if (!filename.startsWith(dist + path.sep)) { response.writeHead(404); return response.end(); }
    try {
      const bytes = await fs.readFile(filename); response.setHeader('Content-Type', mime[path.extname(filename)] || 'application/octet-stream');
      if (url.pathname === '/ngsw-worker.js') response.setHeader('Service-Worker-Allowed', '/portal/'); response.end(bytes);
    } catch { response.writeHead(404); response.end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const target = `http://127.0.0.1:${server.address().port}`;
  const proxies = {};
  for (const prefix of ['/portal/', '/api/', '/assets/', '/ngsw-worker.js', '/ngsw.json', '/index.html', '/manifest.webmanifest', '/runtime-control', '/safety-worker.js', '/worker-basic.min.js']) proxies[prefix] = target + prefix;
  for (const file of await fs.readdir(dist)) if (/\.(js|css)$/.test(file)) proxies['/' + file] = target + '/' + file;
  const config = await karmaConfig.parseConfig(undefined, {
    basePath: root, frameworks: ['jasmine'],
    files: [{ pattern: 'scripts/pwa-runtime.spec.js', watched: false }], proxies,
    port: 9877, hostname: '127.0.0.1', listenAddress: '127.0.0.1',
    browsers: ['ChromeHeadless'], singleRun: true, reporters: ['progress'],
    browserNoActivityTimeout: 45000, client: { jasmine: { random: false, timeoutInterval: 25000 } },
  }, { promiseConfig: true, throwErrors: true });
  const karma = new Server(config, code => { server.close(); process.exitCode = code; });
  await karma.start();
})().catch(error => { console.error(error); process.exitCode = 1; });
