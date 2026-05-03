import {
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
  signInWithPopup,
  signOut,
  updateProfile,
} from 'firebase/auth'
import type { AuthProvider } from 'firebase/auth'
import { ref } from 'vue'
import {
  appleProvider,
  getFirebaseAuth,
  googleProvider,
  microsoftProvider,
  useMockAuth,
} from '@/plugins/firebase'

/**
 * Student Firebase Auth composable — FIND-ux-023.
 *
 * Mirrors `src/admin/.../composables/useFirebaseAuth.ts` but WITHOUT
 * the ADMIN_ROLES gate. Students are not role-gated; any authenticated
 * user may proceed. Custom claims (locale, school_id) will be consumed
 * by the backend, not checked at sign-in time.
 *
 * Error handling maps Firebase error codes to i18n keys so the UI
 * never shows raw Firebase messages.
 */

export type FirebaseErrorI18nKey =
  | 'auth.invalidCredentials'
  | 'auth.accountDisabled'
  | 'auth.tooManyAttempts'
  | 'auth.emailAlreadyExists'
  | 'auth.signInFailed'
  | 'auth.providerSignInCancelled'
  | 'auth.networkError'

/**
 * Map a Firebase Auth error code to an i18n key.
 * Falls back to a generic message for unrecognized codes.
 */
export function mapFirebaseErrorToI18nKey(code: string | undefined): FirebaseErrorI18nKey {
  switch (code) {
    case 'auth/user-not-found':
    case 'auth/wrong-password':
    case 'auth/invalid-credential':
    case 'auth/invalid-email':
      return 'auth.invalidCredentials'

    case 'auth/user-disabled':
      return 'auth.accountDisabled'

    case 'auth/too-many-requests':
      return 'auth.tooManyAttempts'

    case 'auth/email-already-in-use':
      return 'auth.emailAlreadyExists'

    case 'auth/popup-closed-by-user':
    case 'auth/cancelled-popup-request':
      return 'auth.providerSignInCancelled'

    case 'auth/network-request-failed':
      return 'auth.networkError'

    default:
      return 'auth.signInFailed'
  }
}

export function useFirebaseAuth() {
  const isLoading = ref(false)
  const errorKey = ref<FirebaseErrorI18nKey | null>(null)

  /**
   * Sign in with email and password.
   * Returns the user's UID on success, throws on failure.
   */
  async function loginWithEmail(email: string, password: string): Promise<string> {
    if (useMockAuth)
      throw new Error('[useFirebaseAuth] loginWithEmail called in mock auth mode. This should not happen.')

    errorKey.value = null
    isLoading.value = true

    try {
      const auth = getFirebaseAuth()
      const credential = await signInWithEmailAndPassword(auth, email, password)

      console.info('[useFirebaseAuth] loginWithEmail success:', credential.user.uid)

      return credential.user.uid
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      errorKey.value = mapFirebaseErrorToI18nKey(err.code)

      console.error('[useFirebaseAuth] loginWithEmail error:', err.code, err.message)

      throw error
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * Register with email and password.
   * Returns the user's UID on success.
   */
  async function registerWithEmail(email: string, password: string, displayName?: string): Promise<string> {
    if (useMockAuth)
      throw new Error('[useFirebaseAuth] registerWithEmail called in mock auth mode.')

    errorKey.value = null
    isLoading.value = true

    try {
      const auth = getFirebaseAuth()
      const credential = await createUserWithEmailAndPassword(auth, email, password)

      if (displayName)
        await updateProfile(credential.user, { displayName })

      console.info('[useFirebaseAuth] registerWithEmail success:', credential.user.uid)

      return credential.user.uid
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      errorKey.value = mapFirebaseErrorToI18nKey(err.code)

      console.error('[useFirebaseAuth] registerWithEmail error:', err.code, err.message)

      throw error
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * Sign in with an OAuth provider (Google, Apple, Microsoft).
   * Returns the user's UID on success.
   */
  async function loginWithProvider(providerName: 'google' | 'apple' | 'microsoft'): Promise<string> {
    if (useMockAuth)
      throw new Error(`[useFirebaseAuth] loginWithProvider(${providerName}) called in mock auth mode.`)

    errorKey.value = null
    isLoading.value = true

    let provider: AuthProvider

    switch (providerName) {
      case 'google':
        provider = googleProvider
        break
      case 'apple':
        provider = appleProvider
        break
      case 'microsoft':
        provider = microsoftProvider
        break
      default:
        throw new Error(`Unknown provider: ${providerName}`)
    }

    try {
      const auth = getFirebaseAuth()
      const credential = await signInWithPopup(auth, provider)

      console.info('[useFirebaseAuth] loginWithProvider success:', providerName, credential.user.uid)

      return credential.user.uid
    }
    catch (error: unknown) {
      const err = error as { code?: string; message?: string }

      errorKey.value = mapFirebaseErrorToI18nKey(err.code)

      console.error('[useFirebaseAuth] loginWithProvider error:', providerName, err.code, err.message)

      throw error
    }
    finally {
      isLoading.value = false
    }
  }

  /**
   * Sign out the current user.
   */
  async function logout(): Promise<void> {
    if (useMockAuth)
      return

    const auth = getFirebaseAuth()

    await signOut(auth)
  }

  /**
   * Get a fresh ID token from Firebase Auth. Used by the API client
   * for Bearer token attachment and 401 retry flows.
   */
  async function getIdToken(forceRefresh = false): Promise<string | null> {
    if (useMockAuth)
      return null

    try {
      const auth = getFirebaseAuth()
      const user = auth.currentUser

      if (!user)
        return null

      return await user.getIdToken(forceRefresh)
    }
    catch (err) {
      console.error('[useFirebaseAuth] getIdToken error:', err)

      return null
    }
  }

  /**
   * TASK-E2E-A-01-BE-01: bridge a freshly-registered Firebase user into Cena.
   *
   * Calls `POST /api/auth/on-first-sign-in` with the just-issued idToken so the
   * backend can: (1) set Cena custom claims (role, tenant_id, school_id),
   * (2) create the StudentProfile + AdminUser docs, and (3) emit
   * `StudentOnboardedV1` on NATS. Then forces an idToken refresh so the new
   * claims land on the client side BEFORE any /api/me call fires.
   *
   * Tenant resolution:
   *   - E2E: read `cena-e2e-tenant-id` from localStorage (Playwright fixture
   *     injects it via addInitScript before the register flow runs).
   *   - Production: invite-code path (TODO — endpoint returns 501 until that
   *     follow-up ships, see task TASK-E2E-A-04-BE-* in the queue).
   *
   * Throws on non-2xx so the caller can surface the failure to the user
   * rather than silently proceeding with a half-bootstrapped account.
   */
  async function onFirstSignIn(opts: {
    apiBaseUrl: string
    displayName?: string
  }): Promise<{ tenantId: string; schoolId: string; role: string; wasNewlyOnboarded: boolean }> {
    if (useMockAuth)
      throw new Error('[useFirebaseAuth] onFirstSignIn called in mock auth mode.')

    const idToken = await getIdToken(false)
    if (!idToken)
      throw new Error('[useFirebaseAuth] onFirstSignIn: no idToken available — register/sign-in must run first.')

    // Per the BE task body: E2E reads tenantId from a known localStorage key
    // populated by the Playwright fixture's `addInitScript`. Production-mode
    // callers (no env-var on backend) will get a 501 from the endpoint and the
    // SPA surfaces that error verbatim.
    const tenantId = window.localStorage.getItem('cena-e2e-tenant-id') ?? ''

    const resp = await fetch(`${opts.apiBaseUrl}/api/auth/on-first-sign-in`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${idToken}`,
      },
      body: JSON.stringify({
        tenantId,
        schoolId: null,
        displayName: opts.displayName ?? null,
      }),
    })

    if (!resp.ok) {
      const text = await resp.text().catch(() => '')
      console.error('[useFirebaseAuth] on-first-sign-in failed:', resp.status, text)
      throw new Error(`on-first-sign-in returned ${resp.status}: ${text}`)
    }

    const body = await resp.json() as {
      uid: string
      tenantId: string
      schoolId: string
      role: string
      wasNewlyOnboarded: boolean
    }

    // Force-refresh so the next /api/me call carries the new custom claims.
    await getIdToken(true)

    return {
      tenantId: body.tenantId,
      schoolId: body.schoolId,
      role: body.role,
      wasNewlyOnboarded: body.wasNewlyOnboarded,
    }
  }

  return {
    isLoading,
    errorKey,
    loginWithEmail,
    registerWithEmail,
    loginWithProvider,
    logout,
    getIdToken,
    onFirstSignIn,
    mapFirebaseErrorToI18nKey,
  }
}
