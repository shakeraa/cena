import { computed, onMounted, onUnmounted, ref } from 'vue'
// eslint-disable-next-line import/no-unresolved
import { useRegisterSW } from 'virtual:pwa-register/vue'
import { useMeStore } from '@/stores/meStore'

/**
 * PWA-001: reactive service worker composable.
 *
 * Wraps vite-plugin-pwa's `useRegisterSW` and adds:
 *  - `isOffline` — tracks `navigator.onLine`
 *  - session-aware `updateApp()` — refuses to reload while a session is active
 *  - `dismissUpdate()` — hides the update notification
 */
export function useServiceWorker() {
  const {
    needRefresh,
    offlineReady,
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(
      _: string,
      __: ServiceWorkerRegistration | undefined,
    ) {
      console.info('[pwa] service worker registered')
    },
    onRegisterError(error: unknown) {
      console.warn('[pwa] service worker registration failed', error)
    },
  })

  const isOffline = ref(!navigator.onLine)

  function handleOnline() {
    isOffline.value = false
  }

  function handleOffline() {
    isOffline.value = true
  }

  onMounted(() => {
    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)
  })

  onUnmounted(() => {
    window.removeEventListener('online', handleOnline)
    window.removeEventListener('offline', handleOffline)
  })

  const meStore = useMeStore()
  const hasActiveSession = computed(() => meStore.hasActiveSession)

  async function updateApp(): Promise<void> {
    if (hasActiveSession.value) {
      // Don't reload while the student is mid-session — the update will
      // apply automatically once the session ends and they navigate away.
      return
    }
    await updateServiceWorker(true)
  }

  function dismissUpdate(): void {
    needRefresh.value = false
  }

  return {
    needRefresh,
    offlineReady,
    isOffline,
    hasActiveSession,
    updateApp,
    dismissUpdate,
  }
}
