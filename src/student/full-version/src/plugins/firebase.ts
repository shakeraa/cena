import type { App } from 'vue'
import { initializeApp } from 'firebase/app'
import type { FirebaseApp } from 'firebase/app'
import {
  GoogleAuthProvider,
  OAuthProvider,
  connectAuthEmulator,
  getAuth,
  onAuthStateChanged,
  signOut,
} from 'firebase/auth'
import type { Auth } from 'firebase/auth'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'
import { ability, studentAbilityRules } from '@/plugins/casl/ability'
import { router } from '@/plugins/1.router'

/**
 * Firebase Auth plugin — FIND-ux-023: real Firebase Auth.
 *
 * Replaces the Phase-A stub with real `initializeApp(...)` + `onAuthStateChanged`.
 *
 * Mock auth is available ONLY when:
 *   - `import.meta.env.DEV` is true (Vite dev mode)
 *   - `import.meta.env.VITE_USE_MOCK_AUTH` is explicitly set to `'true'`
 *
 * In all other cases (production, CI builds, dev without the flag),
 * real Firebase Auth is used.
 *
 * FIND-ux-020: CASL ability rules are seeded on sign-in and persisted
 * to the `userAbilityRules` cookie so sidebar nav items pass `can()`.
 */

/**
 * Whether the current environment should use mock auth instead of real Firebase.
 * Exported for use by other modules that need to check the mode.
 */
export const useMockAuth: boolean
  = import.meta.env.DEV === true
  && import.meta.env.VITE_USE_MOCK_AUTH === 'true'

let _firebaseApp: FirebaseApp | null = null
let _firebaseAuth: Auth | null = null

/**
 * Initialize Firebase. No-ops if mock auth is enabled or if already initialized.
 * Returns the Auth instance (or null when mocking).
 */
function initFirebase(): Auth | null {
  if (useMockAuth)
    return null

  if (_firebaseAuth)
    return _firebaseAuth

  const config = {
    apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
    authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
    projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
    storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET,
    messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
    appId: import.meta.env.VITE_FIREBASE_APP_ID,
  }

  // Validate that required Firebase config is present
  if (!config.apiKey || !config.projectId) {
    console.error(
      '[firebase] Missing VITE_FIREBASE_API_KEY or VITE_FIREBASE_PROJECT_ID.',
      'Set them in .env or enable mock auth with VITE_USE_MOCK_AUTH=true.',
    )
    throw new Error('Firebase config missing. Set VITE_FIREBASE_* env vars or enable VITE_USE_MOCK_AUTH=true.')
  }

  _firebaseApp = initializeApp(config)
  _firebaseAuth = getAuth(_firebaseApp)

  // Connect to Firebase Auth emulator when VITE_FIREBASE_AUTH_EMULATOR_HOST is set
  const emulatorHost = import.meta.env.VITE_FIREBASE_AUTH_EMULATOR_HOST
  if (emulatorHost) {
    connectAuthEmulator(_firebaseAuth, emulatorHost, { disableWarnings: true })

    console.info('[firebase] Connected to Auth emulator at', emulatorHost)
  }

  return _firebaseAuth
}

/**
 * Get the Firebase Auth instance. Throws if Firebase is not initialized
 * and mock auth is not enabled.
 */
export function getFirebaseAuth(): Auth {
  if (_firebaseAuth)
    return _firebaseAuth

  const auth = initFirebase()
  if (!auth) {
    throw new Error(
      '[firebase] Auth not initialized. This should not be called in mock auth mode.',
    )
  }

  return auth
}

/**
 * Exported providers for use by AuthProviderButtons and other modules.
 */
export const googleProvider = new GoogleAuthProvider()
export const appleProvider = (() => {
  const p = new OAuthProvider('apple.com')

  p.addScope('email')
  p.addScope('name')

  return p
})()
export const microsoftProvider = new OAuthProvider('microsoft.com')

/**
 * FIND-ux-020: one-shot cookie write/clear for CASL ability rules.
 */
function writeAbilityCookie(rules: Array<{ action: string; subject: string }>): void {
  if (typeof document === 'undefined')
    return
  try {
    const value = encodeURIComponent(JSON.stringify(rules))
    const maxAge = 60 * 60 * 24 * 30

    document.cookie = `userAbilityRules=${value}; path=/; max-age=${maxAge}`
  }
  catch {
    // ignore
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

/**
 * Vue plugin entrypoint. Installed by the app's plugin registration.
 */
export default function install(_app: App) {
  const authStore = useAuthStore()
  const meStore = useMeStore()

  // SSR guard
  if (typeof window === 'undefined') {
    authStore.__setReady()

    return
  }

  // E2E-K-04 test seam: in dev/test only, expose meStore.__setActiveSession
  // on `window` so a Playwright spec can flip `hasActiveSession` true/false
  // without first having to /POST /api/sessions/start. Statically tree-
  // shaken from any production build by Vite (DEV / MODE='test' guards).
  if (import.meta.env.DEV || import.meta.env.MODE === 'test') {
    ;(window as unknown as { __cenaTestSetActiveSession?: (id: string | null) => void })
      .__cenaTestSetActiveSession = (id) => meStore.__setActiveSession(id)
  }

  // ── Mock auth path (dev only, explicit opt-in) ──
  if (useMockAuth) {
    console.info('[firebase] Mock auth mode enabled (VITE_USE_MOCK_AUTH=true)')

    const mockAuth = localStorage.getItem('cena-mock-auth')
    if (mockAuth) {
      try {
        const parsed = JSON.parse(mockAuth)
        if (parsed?.uid)
          authStore.__mockSignIn(parsed)
      }
      catch {
        // ignore malformed mocks
      }
    }

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

    queueMicrotask(() => {
      authStore.__setReady()
    })

    return
  }

  // ── Real Firebase Auth path ──
  const auth = initFirebase()
  if (!auth) {
    // Should not happen — initFirebase throws when config is missing
    // and useMockAuth is false. Belt-and-suspenders.
    authStore.__setReady()

    return
  }

  onAuthStateChanged(auth, async firebaseUser => {
    if (firebaseUser) {
      try {
        const tokenResult = await firebaseUser.getIdTokenResult()
        const idToken = tokenResult.token

        authStore.__firebaseSignIn({
          uid: firebaseUser.uid,
          email: firebaseUser.email ?? undefined,
          displayName: firebaseUser.displayName ?? undefined,
          idToken,
        })

        // Seed CASL abilities for student
        ability.update(studentAbilityRules)
        writeAbilityCookie(studentAbilityRules)

        // Hydrate meStore from /api/me so the router guard's
        // `isOnboarded` check reflects backend reality. Without this the
        // guard sees `profile == null` for every fresh sign-in and
        // bounces the user to /onboarding even though the projection
        // already has `onboardedAt`. 404 is a normal "no snapshot yet"
        // state — leave the profile minimal so /onboarding is the
        // landing route.
        await hydrateMeFromApi(idToken, firebaseUser, meStore)

        console.info('[firebase] Auth state: signed in as', firebaseUser.uid)
      }
      catch (err) {
        console.error('[firebase] Failed to process auth state change:', err)
        await signOut(auth)
        authStore.__firebaseSignOut()
        meStore.__setProfile(null)
        ability.update([])
        clearAbilityCookie()
      }
    }
    else {
      // Snapshot whether THIS tab thought it was signed in BEFORE we
      // clear authStore. The two scenarios that reach this branch:
      //   (a) Initial app boot with no Firebase session — wasSignedIn
      //       is false here, this is normal init, do NOT redirect.
      //   (b) User signed out (this tab OR a sibling tab via IDB sync)
      //       — wasSignedIn was true, push to /login if we're on a
      //       requiresAuth route so the user doesn't sit on stale
      //       signed-in chrome.
      const wasSignedIn = authStore.isSignedIn

      authStore.__firebaseSignOut()
      meStore.__setProfile(null)
      ability.update([])
      clearAbilityCookie()

      console.info('[firebase] Auth state: signed out')

      // Cross-tab sign-out propagation. Skip on initial boot
      // (wasSignedIn=false) — the route guard already handles
      // unauthenticated requiresAuth navigations on the next routing
      // decision; redirecting here on boot incorrectly bounces
      // public routes like /register and /forgot-password to /login.
      if (wasSignedIn) {
        const current = router.currentRoute.value
        const requiresAuth = current.meta?.requiresAuth !== false
        const isPublic = current.path === '/login'
          || current.path === '/register'
          || current.path === '/forgot-password'
          || current.path.startsWith('/_dev/')
        if (requiresAuth && !isPublic) {
          router.replace({
            path: '/login',
            query: { returnTo: current.fullPath },
          }).catch(() => { /* navigation cancelled — guard will retry */ })
        }
      }
    }

    authStore.__setReady()
  })
}

/**
 * Fetch /api/me with the freshly issued Firebase idToken and populate
 * meStore.profile. The route guard reads `meStore.isOnboarded` to decide
 * /home vs /onboarding; without this hydration the guard always sees
 * `profile == null` and bounces signed-in users to /onboarding.
 *
 * Failure modes:
 *   - 404: no StudentProfileSnapshot yet → seed profile with
 *     onboardedAt=null so guard correctly routes to /onboarding
 *   - 401/5xx/network: leave the previous profile in place — auth state
 *     already changed, the next render will surface the error elsewhere
 */
async function hydrateMeFromApi(
  idToken: string,
  firebaseUser: { uid: string; email: string | null; displayName: string | null },
  meStore: ReturnType<typeof useMeStore>,
): Promise<void> {
  // Use a relative path so the request goes through vite's /api proxy
  // in dev (same-origin) and through the same-origin path in prod.
  // Earlier this read VITE_STUDENT_API_BASE_URL=http://localhost:5050,
  // which is cross-origin from the SPA at localhost:5175; that triggered
  // CORS net::ERR_FAILED, the catch swallowed it, meStore stayed
  // unhydrated with onboardedAt:null, and the router guard bounced
  // every signed-in student back to /onboarding.
  const url = '/api/me'

  try {
    const resp = await fetch(url, {
      method: 'GET',
      headers: {
        Authorization: `Bearer ${idToken}`,
        Accept: 'application/json',
      },
    })

    if (resp.status === 404) {
      // Brand-new user — projection hasn't been seeded yet. The next
      // /onboarding submit will write the snapshot.
      meStore.__setProfile({
        uid: firebaseUser.uid,
        displayName: firebaseUser.displayName ?? '',
        email: firebaseUser.email ?? '',
        locale: 'en',
        onboardedAt: null,
      })
      return
    }

    if (!resp.ok) {
      console.warn(`[firebase] GET /api/me ${resp.status} — meStore left as-is`)
      return
    }

    const body = await resp.json() as {
      displayName?: string
      locale?: string
      onboardedAt?: string | null
    }
    meStore.__setProfile({
      uid: firebaseUser.uid,
      displayName: body.displayName ?? firebaseUser.displayName ?? '',
      email: firebaseUser.email ?? '',
      locale: (body.locale === 'ar' || body.locale === 'he') ? body.locale : 'en',
      onboardedAt: typeof body.onboardedAt === 'string' ? body.onboardedAt : null,
    })
  }
  catch (err) {
    console.warn('[firebase] /api/me hydration failed:', err)
  }
}
