const CACHE = 'busylight-v2';
const ASSETS = ['./index.html', './manifest.json', './icon.svg', './busylight-core.js', './app.js'];

self.addEventListener('install', e => {
  e.waitUntil(
    caches.open(CACHE)
      // cache: 'reload' bypasses the browser's HTTP cache, so a fresh install is
      // never seeded with the stale copies the browser happened to be holding.
      .then(c => c.addAll(ASSETS.map(u => new Request(u, { cache: 'reload' }))))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', e => {
  e.waitUntil(
    caches.keys()
      .then(keys => Promise.all(
        keys.filter(k => k !== CACHE).map(k => caches.delete(k))
      ))
      .then(() => self.clients.claim())
  );
});

// Network-first, falling back to the cache when offline.
//
// The PWA redeploys on every push to main and carries no version of its own, so
// the previous cache-first worker pinned each phone to whatever shipped the day
// it first opened the app: nothing short of clearing site data ever updated it.
// Freshness matters more than offline start-up here — the app is useless without
// the BLE device in front of you anyway — so the network gets first say, and the
// cache stays as the offline safety net.
self.addEventListener('fetch', e => {
  if (e.request.method !== 'GET') return;
  e.respondWith(
    fetch(e.request)
      .then(res => {
        // Refresh the offline copy, but only for our own successful responses —
        // opaque cross-origin results and errors must not overwrite good cache.
        if (res.ok && new URL(e.request.url).origin === self.location.origin) {
          const copy = res.clone();
          caches.open(CACHE).then(c => c.put(e.request, copy));
        }
        return res;
      })
      .catch(() => caches.match(e.request).then(cached => cached ?? Response.error()))
  );
});
