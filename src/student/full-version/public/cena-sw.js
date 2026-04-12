/* eslint-disable */
// Cena Student — minimal offline shell service worker (STU-W-15 Phase A).
// Strategy:
//   - precache the app shell on install
//   - cache-first for same-origin GET requests of static assets
//   - network-first with offline-fallback for navigations
//   - API calls (`/api/*`) always bypass the worker (network-only)

const CACHE_VERSION = 'cena-v1'
const OFFLINE_URL = '/offline.html'
const APP_SHELL = [
  '/',
  '/offline.html',
  '/manifest.webmanifest',
  '/favicon.ico',
  '/images/icon-192.png',
  '/images/icon-512.png',
]

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_VERSION).then(cache => cache.addAll(APP_SHELL).catch(() => null)),
  )
  self.skipWaiting()
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => {
      return Promise.all(
        keys
          .filter(k => k !== CACHE_VERSION && k.startsWith('cena-'))
          .map(k => caches.delete(k)),
      )
    }).then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', event => {
  const req = event.request
  const url = new URL(req.url)

  // Only handle GET
  if (req.method !== 'GET')
    return

  // Never intercept API calls — they must always hit the network.
  if (url.pathname.startsWith('/api/')) {
    return
  }

  // Never intercept the MSW worker file or MSW mocks.
  if (url.pathname.startsWith('/mockServiceWorker')) {
    return
  }

  // FIND-ux-029: structured log when a manifest icon fetch fails in the SW.
  // Detectable in production via console filter for [cena-sw:icon-fetch-err].
  if (url.pathname.startsWith('/images/icon-')) {
    event.respondWith(
      caches.match(req).then(cached => {
        if (cached) return cached
        return fetch(req).then(resp => {
          if (resp && resp.status === 200) {
            const ct = resp.headers.get('content-type') || ''
            if (!ct.startsWith('image/')) {
              console.error(
                JSON.stringify({
                  event: 'cena-sw:icon-fetch-err',
                  url: req.url,
                  contentType: ct,
                  msg: 'PWA manifest icon returned non-image Content-Type; likely SPA HTML fallback',
                }),
              )
            }
            const clone = resp.clone()
            caches.open(CACHE_VERSION).then(c => c.put(req, clone)).catch(() => null)
          }
          return resp
        }).catch(err => {
          console.error(
            JSON.stringify({
              event: 'cena-sw:icon-fetch-err',
              url: req.url,
              error: err.message || String(err),
              msg: 'PWA manifest icon fetch failed',
            }),
          )
          return new Response('', { status: 504 })
        })
      }),
    )
    return
  }

  // Navigation requests → network-first, offline page fallback.
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(() => caches.match(OFFLINE_URL)),
    )
    return
  }

  // Same-origin static assets → cache-first with background update.
  if (url.origin === self.location.origin) {
    event.respondWith(
      caches.match(req).then(cached => {
        const fetchPromise = fetch(req).then(resp => {
          if (resp && resp.status === 200) {
            const respClone = resp.clone()
            caches.open(CACHE_VERSION).then(c => c.put(req, respClone)).catch(() => null)
          }
          return resp
        }).catch(() => cached)

        return cached || fetchPromise
      }),
    )
  }
})

self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING')
    self.skipWaiting()
})
