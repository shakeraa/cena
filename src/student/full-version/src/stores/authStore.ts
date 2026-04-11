import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

/**
 * Auth store — student app wrapper over Firebase Auth state.
 * STU-W-02 ships a stub that starts unauthed and exposes a `__mockSignIn`
 * helper so E2E tests can simulate post-sign-in flow without real Firebase.
 * STU-W-04 will replace the internals with real Firebase Auth SDK wiring.
 *
 * FIND-ux-010: the original stub kept everything in in-memory refs, which
 * meant every hard navigation / page refresh wiped the mock session and
 * bounced the user to /login. The store now persists the mock identity
 * to `localStorage['cena-mock-auth']` so `src/plugins/firebase.ts` can
 * rehydrate it on boot *and* so URL-share / refresh flows survive.
 *
 * Persistence is intentionally gated on `typeof window !== 'undefined'`
 * so SSR / unit tests with a jsdom shim still work. The storage key is
 * the SAME one `firebase.ts` already reads on plugin init, which means
 * the hydration path is unchanged — we just start writing to it too.
 */

export interface MockAuthPayload {
  uid: string
  email?: string
  displayName?: string
}

/**
 * localStorage key for the mock auth payload. Intentionally matches the
 * key `src/plugins/firebase.ts` already reads so there is exactly ONE
 * source of truth for where the mock session lives.
 */
const MOCK_AUTH_STORAGE_KEY = 'cena-mock-auth'

function writeMockAuthToStorage(payload: MockAuthPayload): void {
  if (typeof window === 'undefined')
    return
  try {
    window.localStorage.setItem(MOCK_AUTH_STORAGE_KEY, JSON.stringify(payload))
  }
  catch {
    // Quota exceeded / privacy mode — silently ignore. The in-memory
    // session still works for the current tab; only the refresh-survives
    // guarantee is weakened.
  }
}

function clearMockAuthFromStorage(): void {
  if (typeof window === 'undefined')
    return
  try {
    window.localStorage.removeItem(MOCK_AUTH_STORAGE_KEY)
  }
  catch {
    // ignore
  }
}

function readMockAuthFromStorage(): MockAuthPayload | null {
  if (typeof window === 'undefined')
    return null
  try {
    const raw = window.localStorage.getItem(MOCK_AUTH_STORAGE_KEY)
    if (!raw)
      return null
    const parsed = JSON.parse(raw) as Partial<MockAuthPayload>
    if (typeof parsed?.uid !== 'string' || parsed.uid.length === 0)
      return null

    return {
      uid: parsed.uid,
      email: typeof parsed.email === 'string' ? parsed.email : undefined,
      displayName: typeof parsed.displayName === 'string' ? parsed.displayName : undefined,
    }
  }
  catch {
    return null
  }
}

export const useAuthStore = defineStore('auth', () => {
  const uid = ref<string | null>(null)
  const email = ref<string | null>(null)
  const displayName = ref<string | null>(null)
  const idToken = ref<string | null>(null)

  // The router guard must wait for the first auth-state resolution before
  // making decisions, otherwise the initial load redirects to /login for a
  // single frame. `ready` flips to true as soon as we know whether the user
  // is signed in.
  const ready = ref(false)

  // FIND-ux-010: eagerly hydrate from localStorage on store init. Without
  // this, the router guard would run BEFORE `firebase.ts` had a chance to
  // call `__mockSignIn`, and the first `to.meta.requiresAuth` check would
  // redirect to /login even though the mock session was present on disk.
  // We still leave `ready` false here — `firebase.ts` flips it on the next
  // microtask so the async Firebase boot flow stays unchanged.
  const hydrated = readMockAuthFromStorage()
  if (hydrated) {
    uid.value = hydrated.uid
    email.value = hydrated.email ?? null
    displayName.value = hydrated.displayName ?? null
    idToken.value = `mock-token-${hydrated.uid}`
  }

  const isSignedIn = computed(() => uid.value !== null)

  function __setReady() {
    ready.value = true
  }

  function __mockSignIn(payload: MockAuthPayload) {
    uid.value = payload.uid
    email.value = payload.email ?? null
    displayName.value = payload.displayName ?? null
    idToken.value = `mock-token-${payload.uid}`
    ready.value = true
    writeMockAuthToStorage(payload)
  }

  function __signOut() {
    uid.value = null
    email.value = null
    displayName.value = null
    idToken.value = null
    clearMockAuthFromStorage()
  }

  return {
    uid,
    email,
    displayName,
    idToken,
    ready,
    isSignedIn,
    __setReady,
    __mockSignIn,
    __signOut,
  }
})
