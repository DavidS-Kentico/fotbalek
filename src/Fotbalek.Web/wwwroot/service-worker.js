// Fotbálek installable-shell service worker.
//
// This app is Blazor *Server*: every button click, the live game and chat all run over the
// SignalR circuit, so it fundamentally cannot work offline. This service worker therefore does
// NOT attempt an offline app. Its only jobs are:
//   1. Exist with a fetch handler, so browsers treat the app as installable (home-screen / PWA).
//   2. Cache a tiny static shell (icons + manifest) so install and the app icon are instant.
//   3. Show a friendly "you're offline" page for navigations when the network is down, instead of
//      the browser's default error page.
//
// It MUST NEVER cache or intercept the Blazor circuit, SignalR, auth, or any dynamic response —
// doing so would serve stale HTML/JS and break the live app. Keep this deliberately dumb.

const CACHE = 'fotbalek-shell-v1';

// Only stable, non-fingerprinted URLs go here. Do NOT add app.css / app.js / _framework assets:
// MapStaticAssets fingerprints those and their URLs change every build, so a hardcoded entry
// would go stale. They are fetched live from the network instead.
const SHELL = [
  '/manifest.webmanifest',
  '/favicon.png',
  '/icon-192.png',
  '/icon-512.png',
  '/icon-maskable-512.png',
  '/offline.html',
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE)
      .then((cache) => cache.addAll(SHELL))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  const url = new URL(req.url);

  // Only handle same-origin GETs; anything else falls through to the browser's default.
  if (req.method !== 'GET' || url.origin !== self.location.origin) return;

  // Never touch the live plumbing — these all require the network.
  if (
    url.pathname.startsWith('/_framework') ||   // blazor.web.js + runtime
    url.pathname.startsWith('/_blazor')    ||   // SignalR negotiate / circuit
    url.pathname.startsWith('/hubs')       ||   // GameHub
    url.pathname.startsWith('/Account')    ||   // auth endpoints
    url.pathname.startsWith('/Admin')           // admin endpoints
  ) return;

  // Page navigations: always go to the network (the app is server-rendered and live).
  // Only if the network fails do we show the offline notice.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(() => caches.match('/offline.html'))
    );
    return;
  }

  // Everything else (static assets): serve the precached shell from cache when we have it,
  // otherwise just fetch from the network. No dynamic caching, so nothing goes stale.
  event.respondWith(
    caches.match(req).then((hit) => hit || fetch(req))
  );
});
