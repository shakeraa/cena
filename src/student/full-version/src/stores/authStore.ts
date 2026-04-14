import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ability, studentAbilityRules } from '@/plugins/casl/ability'
import type { Rule } from '@/plugins/casl/ability'

/**
 * Auth store — student app wrapper over Firebase Auth state.
 *
 * FIND-ux-023: replaced the Phase-A stub with real Firebase Auth.
 * The store now tracks a real Firebase ID token via `__firebaseSignIn`
 * (called by the firebase.ts plugin's `onAuthStateChanged` listener).
 *
 * Mock auth (`__mockSignIn`) is retained for:
 *   - Dev mode with `VITE_USE_MOCK_AUTH=true`
 *   - Unit/E2E tests that seed `localStorage['cena-mock-auth']`
 *
 * FIND-ux-010: persistence to localStorage for mock path survives refresh.
 * FIND-ux-020: seeds CASL ability rules on sign-in.
 */

export interface MockAuthPayload {
  uid: string
  email?: string
  displayName?: string
}

export interface FirebaseAuthPayload {
  uid: string
  email?: string
  displayName?: string
  idToken: string
}

const MOCK_AUTH_STORAGE_KEY = 'cena-mock-auth'

function writeMockAuthToStorage(payload: MockAuthPayload): void {
  if (typeof window === 'undefined')
    return
  try {
    window.localStorage.setItem(MOCK_AUTH_STORAGE_KEY, JSON.stringify(payload))
  }
  catch {
    // Quota exceeded / privacy mode — silently ignore.
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

/**
 * FIND-ux-020: one-shot cookie write/clear for CASL ability rules.
 *
 * We do NOT use the reactive `useCookie` composable here because it
 * depends on Vue auto-imports (`ref`, `watch`) which are unavailable
 * in the vitest environment. A raw `document.cookie` write is
 * sufficient — the reactive cookie ref in the CASL plugin (index.ts)
 * reads the cookie on app boot and the sidebar only re-evaluates on
 * navigation, not mid-action.
 */
function writeAbilityCookie(rules: Rule[]): void {
  if (typeof document === 'undefined')
    return
  try {
    const value = encodeURIComponent(JSON.stringify(rules))
    const maxAge = 60 * 60 * 24 * 30 // 30 days

    document.cookie = `userAbilityRules=${value}; path=/; max-age=${maxAge}`
  }
  catch {
    // ignore — in-memory ability.update() already applied
  }
}

function clearAbilityCookie(): void {
  if (typeof document === 'undefined')
    return
  try {
    document.cookie = 'userAbilityRules=; path=/; max-age=-1'
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

  // FIND-ux-010: eagerly hydrate from localStorage on store init (mock path
  // only). For real Firebase, the `onAuthStateChanged` listener handles
  // hydration. We still leave `ready` false here — firebase.ts flips it.
  const hydrated = readMockAuthFromStorage()
  if (hydrated) {
    uid.value = hydrated.uid
    email.value = hydrated.email ?? null
    displayName.value = hydrated.displayName ?? null
    idToken.value = `mock-token-${hydrated.uid}`

    // FIND-ux-020: also seed CASL abilities on hydration so the sidebar
    // renders after a page refresh without waiting for __mockSignIn.
    ability.update(studentAbilityRules)
  }

  const isSignedIn = computed(() => uid.value !== null)

  function __setReady() {
    ready.value = true
  }

  /**
   * Called by the firebase.ts plugin when `onAuthStateChanged` fires
   * with a valid user. This is the REAL sign-in path.
   */
  function __firebaseSignIn(payload: FirebaseAuthPayload) {
    uid.value = payload.uid
    email.value = payload.email ?? null
    displayName.value = payload.displayName ?? null
    idToken.value = payload.idToken
    ready.value = true

    ability.update(studentAbilityRules)
    writeAbilityCookie(studentAbilityRules)

    console.info('[auth] Firebase sign-in complete for', payload.uid)
  }

  /**
   * Called by the firebase.ts plugin when `onAuthStateChanged` fires
   * with null (user signed out).
   */
  function __firebaseSignOut() {
    uid.value = null
    email.value = null
    displayName.value = null
    idToken.value = null

    ability.update([])
    clearAbilityCookie()

    console.info('[auth] Firebase sign-out complete')
  }

  /**
   * Update the stored ID token. Called after a forced token refresh
   * (e.g. by the API client on 401).
   */
  function __updateIdToken(token: string) {
    idToken.value = token
  }

  /**
   * Mock sign-in — for dev+mock mode and tests.
   */
  function __mockSignIn(payload: MockAuthPayload) {
    uid.value = payload.uid
    email.value = payload.email ?? null
    displayName.value = payload.displayName ?? null
    idToken.value = `mock-token-${payload.uid}`
    ready.value = true
    writeMockAuthToStorage(payload)

    // FIND-ux-020: seed CASL abilities so sidebar nav items render.
    ability.update(studentAbilityRules)
    writeAbilityCookie(studentAbilityRules)

    console.info('[auth] CASL ability rules seeded for student', payload.uid)
  }

  function __signOut() {
    uid.value = null
    email.value = null
    displayName.value = null
    idToken.value = null
    clearMockAuthFromStorage()

    // FIND-ux-020: clear CASL abilities so stale nav doesn't render
    // after sign-out. Mirrors the admin pattern.
    ability.update([])
    clearAbilityCookie()

    console.info('[auth] CASL ability rules cleared on sign-out')
  }

  return {
    uid,
    email,
    displayName,
    idToken,
    ready,
    isSignedIn,
    __setReady,
    __firebaseSignIn,
    __firebaseSignOut,
    __updateIdToken,
    __mockSignIn,
    __signOut,
  }
})
