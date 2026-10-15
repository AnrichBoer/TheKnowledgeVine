/* global describe, beforeAll, afterAll, it, expect */
describe('Production service-worker privacy and update integration', () => {
  let registration, frame, client;
  async function until(check) {
    const deadline = Date.now() + 18000;
    while (Date.now() < deadline) { const result = await check(); if (result) return result; await new Promise(resolve => setTimeout(resolve, 40)); }
    throw new Error('Production service worker did not become ready in time');
  }
  async function control(values) {
    return (await fetch('/runtime-control', values ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) } : undefined)).json();
  }
  beforeAll(async () => {
    registration = await navigator.serviceWorker.register('/ngsw-worker.js', { scope: '/portal/', updateViaCache: 'none' });
    await until(() => registration.active);
    frame = document.createElement('iframe'); frame.src = '/portal/runtime-probe.html';
    await new Promise(resolve => { frame.onload = resolve; document.body.appendChild(frame); });
    client = frame.contentWindow;
    await until(() => client.navigator.serviceWorker.controller);
    await until(async () => {
      for (const name of await caches.keys()) if (name.startsWith('ngsw:')) {
        const cache = await caches.open(name); if (await cache.match('/index.html')) return true;
      }
      return false;
    });
  });
  afterAll(async () => {
    await control({ offline: false }); frame?.remove(); await registration?.unregister();
    for (const name of await caches.keys()) if (name.startsWith('ngsw:')) await caches.delete(name);
  });
  it('installs the emitted worker with portal-only scope and caches the public shell', async () => {
    expect(new URL(registration.scope).pathname).toBe('/portal/');
    expect((await client.fetch('/index.html')).status).toBe(200);
    const manifest = await (await client.fetch('/manifest.webmanifest')).json(); expect(manifest.start_url).toBe('/portal/');
  });
  it('never caches private API responses, including an unbypassed read', async () => {
    const withBypass = await client.fetch('/api/portal/test-private', { headers: { 'ngsw-bypass': 'true' } });
    expect((await withBypass.json()).note).toBe('STEP7_PRIVATE_NOTE_NEVER_CACHE');
    expect((await (await client.fetch('/api/portal/test-private')).json()).note).toBe('STEP7_PRIVATE_NOTE_NEVER_CACHE');
    for (const name of await caches.keys()) if (name.startsWith('ngsw:')) {
      const cache = await caches.open(name);
      for (const request of await cache.keys()) {
        expect(new URL(request.url).pathname.startsWith('/api/')).toBeFalse();
        expect(await (await cache.match(request)).text()).not.toContain('STEP7_PRIVATE_NOTE_NEVER_CACHE');
      }
    }
  });
  it('serves the public shell during failure, but cannot replay a private note or queue writes', async () => {
    const before = await control(); await control({ offline: true });
    const note = await client.fetch('/api/portal/test-private', { headers: { 'ngsw-bypass': 'true' } }); expect(note.status).toBe(503);
    const write = await client.fetch('/api/portal/test-private', { method: 'POST', headers: { 'ngsw-bypass': 'true' }, body: 'fictional' }); expect(write.status).toBe(503);
    expect((await client.fetch('/index.html')).status).toBe(200);
    await control({ offline: false }); expect((await control()).writes).toBe(before.writes);
    expect((await (await client.fetch('/api/portal/test-private')).json()).note).toBe('STEP7_PRIVATE_NOTE_NEVER_CACHE');
  });
  it('opens a new portal deep link from the public shell when the server is unavailable', async () => {
    const offlineFrame = document.createElement('iframe');
    const outsideFrame = document.createElement('iframe');
    await control({ offline: true });
    try {
      offlineFrame.src = '/portal/parent?worker-launch-check=1';
      let loaded = false;
      offlineFrame.onload = () => { loaded = true; }; document.body.appendChild(offlineFrame);
      await until(() => loaded);
      const launched = offlineFrame.contentWindow;
      expect(launched.navigator.serviceWorker.controller).not.toBeNull();
      expect(launched.document.querySelector('app-root')).not.toBeNull();
      expect(launched.document.body.textContent).not.toContain('STEP7_PRIVATE_NOTE_NEVER_CACHE');
      expect((await launched.fetch('/api/portal/test-private', { headers: { 'ngsw-bypass': 'true' } })).status).toBe(503);
      // Even a cached asset is not used for navigation outside the registered portal scope.
      outsideFrame.src = '/index.html?outside-scope-check=1'; loaded = false;
      outsideFrame.onload = () => { loaded = true; }; document.body.appendChild(outsideFrame);
      await until(() => loaded);
      expect(outsideFrame.contentWindow.navigator.serviceWorker.controller).toBeNull();
      expect(outsideFrame.contentDocument.body.textContent).toContain('Temporary simulated connection failure');
    } finally { offlineFrame.remove(); outsideFrame.remove(); await control({ offline: false }); }
  });
  it('downloads a later deployment and announces VERSION_READY without navigating the active client', async () => {
    await control({ version: 'b' });
    const current = client.location.href; const messages = [];
    const listen = event => messages.push(event.data);
    client.navigator.serviceWorker.addEventListener('message', listen);
    try {
      client.navigator.serviceWorker.controller.postMessage({ action: 'CHECK_FOR_UPDATES', nonce: 7001 });
      await until(() => messages.some(message => message.type === 'VERSION_READY'));
      expect(messages.some(message => message.type === 'VERSION_READY')).toBeTrue(); expect(client.location.href).toBe(current);
    } finally { client.navigator.serviceWorker.removeEventListener('message', listen); }
  });
});
