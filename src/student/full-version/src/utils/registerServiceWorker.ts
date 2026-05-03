/**
 * PWA-001: service worker registration is now handled by vite-plugin-pwa
 * via the useServiceWorker composable (src/composables/useServiceWorker.ts).
 *
 * This function is kept as a no-op so existing imports in main.ts continue
 * to compile without changes. It will be removed in a future cleanup.
 */
export function registerServiceWorker(): void {
  // no-op — vite-plugin-pwa handles SW registration
}
