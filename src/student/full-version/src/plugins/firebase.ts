import type { App } from 'vue'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'

/**
 * Firebase Auth plugin — STU-W-02 stub.
 *
 * STU-W-04 will replace this with real `initializeApp(...)` + `onAuthStateChanged`
 * wiring. For now we defer initialization to give E2E tests a controllable
 * starting state and to avoid crashing local dev without Firebase env vars.
 *
 * The auth store flips `ready = true` after the next microtask so the global
 * router guard sees a resolved state instead of hanging.
 *
 * FIND-ux-020: __mockSignIn now seeds CASL ability rules and writes the
 * userAbilityRules cookie internally, so no extra work needed here.
 */
export default function (__: App) {
  const authStore = useAuthStore()
  const meStore = useMeStore()

  if (typeof window === 'undefined') {
    authStore.__setReady()

    return
  }

  // Allow tests (and local dev) to preload a signed-in user via
  // `localStorage['cena-mock-auth']` before the SPA boots.
  const mockAuth = localStorage.getItem('cena-mock-auth')
  if (mockAuth) {
    try {
      const parsed = JSON.parse(mockAuth)
      if (parsed?.uid) {
        // FIND-ux-020: __mockSignIn now also calls ability.update()
        // and writes the userAbilityRules cookie, so we don't need
        // to do anything extra here.
        authStore.__mockSignIn(parsed)
      }
    }
    catch {
      // ignore malformed mocks
    }
  }

  // Allow tests to seed the `meStore` profile (onboardedAt etc.) via
  // `localStorage['cena-mock-me']`. STU-W-03 will replace this with a real
  // `/api/me` fetch on login.
  const mockMe = localStorage.getItem('cena-mock-me')
  if (mockMe) {
    try {
      const parsed = JSON.parse(mockMe)
      if (parsed?.uid)
        meStore.__setProfile(parsed)
    }
    catch {
      // ignore malformed mocks
    }
  }

  // Simulate Firebase's async onAuthStateChanged — resolves on the next tick.
  queueMicrotask(() => {
    authStore.__setReady()
  })
}
