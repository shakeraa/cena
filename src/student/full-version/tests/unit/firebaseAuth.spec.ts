import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/authStore'
import { mapFirebaseErrorToI18nKey } from '@/composables/useFirebaseAuth'

/**
 * FIND-ux-023 regression tests.
 *
 * These tests verify:
 * 1. The auth store no longer uses `mock-token-*` in the real sign-in path
 * 2. __firebaseSignIn stores a real token, not a mock one
 * 3. __firebaseSignOut properly clears state
 * 4. __updateIdToken works for token refresh flows
 * 5. Firebase error codes are mapped to i18n keys correctly
 * 6. The `__mockSignIn` path is separate from the Firebase path
 *
 * These tests would FAIL on the old code because:
 *   - `__firebaseSignIn` did not exist
 *   - The store only had `__mockSignIn` which generated `mock-token-*`
 *   - There was no way to store a real Firebase ID token
 */

describe('FIND-ux-023: real Firebase Auth in authStore', () => {
  beforeEach(() => {
    if (typeof window !== 'undefined' && window.localStorage)
      window.localStorage.removeItem('cena-mock-auth')
    setActivePinia(createPinia())
  })

  it('__firebaseSignIn stores a REAL ID token (not mock-token-*)', () => {
    const store = useAuthStore()

    // Simulate what firebase.ts's onAuthStateChanged does
    store.__firebaseSignIn({
      uid: 'firebase-uid-abc123',
      email: 'student@school.edu',
      displayName: 'Alice Student',
      idToken: 'eyJhbGciOiJSUzI1NiIsImtpZCI6ImZpcmViYXNlIn0.real-jwt-payload.signature',
    })

    expect(store.isSignedIn).toBe(true)
    expect(store.uid).toBe('firebase-uid-abc123')
    expect(store.email).toBe('student@school.edu')
    expect(store.displayName).toBe('Alice Student')
    expect(store.ready).toBe(true)

    // THE KEY ASSERTION: the token is the REAL JWT, not a mock-token-* string
    expect(store.idToken).toBe('eyJhbGciOiJSUzI1NiIsImtpZCI6ImZpcmViYXNlIn0.real-jwt-payload.signature')
    expect(store.idToken).not.toMatch(/^mock-token-/)
  })

  it('__firebaseSignOut clears all auth state', () => {
    const store = useAuthStore()

    store.__firebaseSignIn({
      uid: 'firebase-uid-abc123',
      email: 'student@school.edu',
      displayName: 'Alice Student',
      idToken: 'real-jwt-token',
    })

    expect(store.isSignedIn).toBe(true)

    store.__firebaseSignOut()

    expect(store.isSignedIn).toBe(false)
    expect(store.uid).toBeNull()
    expect(store.email).toBeNull()
    expect(store.displayName).toBeNull()
    expect(store.idToken).toBeNull()
  })

  it('__updateIdToken refreshes the stored token without changing user info', () => {
    const store = useAuthStore()

    store.__firebaseSignIn({
      uid: 'firebase-uid-abc123',
      email: 'student@school.edu',
      displayName: 'Alice Student',
      idToken: 'original-jwt-token',
    })

    store.__updateIdToken('refreshed-jwt-token')

    expect(store.idToken).toBe('refreshed-jwt-token')
    expect(store.uid).toBe('firebase-uid-abc123')
    expect(store.email).toBe('student@school.edu')
  })

  it('mock sign-in path is separate from Firebase sign-in path', () => {
    const store = useAuthStore()

    // Mock path: generates mock-token-*
    store.__mockSignIn({ uid: 'mock-user', email: 'mock@test.com' })
    expect(store.idToken).toBe('mock-token-mock-user')

    // Firebase path: stores real token
    store.__firebaseSignIn({
      uid: 'real-user',
      email: 'real@school.edu',
      idToken: 'real-firebase-jwt',
    })
    expect(store.idToken).toBe('real-firebase-jwt')
    expect(store.uid).toBe('real-user')
  })
})

describe('FIND-ux-023: Firebase error code mapping', () => {
  it('maps auth/wrong-password to invalidCredentials', () => {
    expect(mapFirebaseErrorToI18nKey('auth/wrong-password')).toBe('auth.invalidCredentials')
  })

  it('maps auth/user-not-found to invalidCredentials', () => {
    expect(mapFirebaseErrorToI18nKey('auth/user-not-found')).toBe('auth.invalidCredentials')
  })

  it('maps auth/invalid-credential to invalidCredentials', () => {
    expect(mapFirebaseErrorToI18nKey('auth/invalid-credential')).toBe('auth.invalidCredentials')
  })

  it('maps auth/user-disabled to accountDisabled', () => {
    expect(mapFirebaseErrorToI18nKey('auth/user-disabled')).toBe('auth.accountDisabled')
  })

  it('maps auth/too-many-requests to tooManyAttempts', () => {
    expect(mapFirebaseErrorToI18nKey('auth/too-many-requests')).toBe('auth.tooManyAttempts')
  })

  it('maps auth/email-already-in-use to emailAlreadyExists', () => {
    expect(mapFirebaseErrorToI18nKey('auth/email-already-in-use')).toBe('auth.emailAlreadyExists')
  })

  it('maps auth/popup-closed-by-user to providerSignInCancelled', () => {
    expect(mapFirebaseErrorToI18nKey('auth/popup-closed-by-user')).toBe('auth.providerSignInCancelled')
  })

  it('maps auth/network-request-failed to networkError', () => {
    expect(mapFirebaseErrorToI18nKey('auth/network-request-failed')).toBe('auth.networkError')
  })

  it('maps unknown code to signInFailed', () => {
    expect(mapFirebaseErrorToI18nKey('auth/some-unknown-error')).toBe('auth.signInFailed')
    expect(mapFirebaseErrorToI18nKey(undefined)).toBe('auth.signInFailed')
  })
})
