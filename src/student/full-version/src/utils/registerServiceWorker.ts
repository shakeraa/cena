/**
 * Cena service worker registration (STU-W-15 Phase A).
 *
 * Only registers in production builds — in dev we rely on MSW's worker
 * for API mocking and skip our own. Also skipped during SSR.
 */

export function registerServiceWorker() {
  if (typeof window === 'undefined')
    return

  // In dev, MSW's worker is registered and we don't want to collide.
  // `import.meta.env.DEV` is true for vite dev server.
  if ((import.meta as any).env?.DEV)
    return

  if (!('serviceWorker' in navigator))
    return

  // Register after page load so it doesn't fight the initial paint.
  window.addEventListener('load', () => {
    navigator.serviceWorker
      .register('/cena-sw.js', { scope: '/' })
      .then(registration => {
        // Log once on success.
        // eslint-disable-next-line no-console
        console.info('[cena-sw] registered at', registration.scope)

        // Listen for updates so we can prompt the user.
        registration.addEventListener('updatefound', () => {
          const installing = registration.installing
          if (!installing)
            return
          installing.addEventListener('statechange', () => {
            if (installing.state === 'installed' && navigator.serviceWorker.controller) {
              // A new worker is waiting — broadcast an event the app can
              // listen for to show a "reload to update" toast.
              window.dispatchEvent(new CustomEvent('cena-sw-update-available'))
            }
          })
        })
      })
      .catch(err => {
        // eslint-disable-next-line no-console
        console.warn('[cena-sw] registration failed', err)
      })
  })
}
