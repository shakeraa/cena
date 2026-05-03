/**
 * Shared Firebase Auth test doubles.
 *
 * Every unit test that touches authentication imports from here so no test
 * makes a real network call to Firebase.
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { vi } from 'vitest'

/* ---------- Mock Firebase User ---------- */

export interface MockFirebaseUser {
  uid: string
  email: string | null
  displayName: string | null
  photoURL: string | null
  getIdToken: ReturnType<typeof vi.fn>
  getIdTokenResult: ReturnType<typeof vi.fn>
}

export function createMockFirebaseUser(overrides: Partial<MockFirebaseUser> = {}): MockFirebaseUser {
  return {
    uid: 'test-uid-001',
    email: 'admin@cena.edu',
    displayName: 'Test Admin',
    photoURL: null,
    getIdToken: vi.fn().mockResolvedValue('mock-id-token-abc'),
    getIdTokenResult: vi.fn().mockResolvedValue({
      token: 'mock-id-token-abc',
      claims: {
        role: 'ADMIN',
        school_id: 'school-001',
        locale: 'en',
        plan: 'premium',
      },
    }),
    ...overrides,
  }
}

/* ---------- Mock Firebase Auth instance ---------- */

export interface MockFirebaseAuth {
  currentUser: MockFirebaseUser | null
}

export function createMockFirebaseAuth(user: MockFirebaseUser | null = null): MockFirebaseAuth {
  return { currentUser: user }
}

/* ---------- firebase/auth module mock ---------- */

let _onAuthStateChangedCallback: ((user: MockFirebaseUser | null) => void) | null = null
let _mockCurrentUser: MockFirebaseUser | null = null

export function setMockCurrentUser(user: MockFirebaseUser | null) {
  _mockCurrentUser = user
}

export function getMockCurrentUser(): MockFirebaseUser | null {
  return _mockCurrentUser
}

export function triggerAuthStateChanged(user: MockFirebaseUser | null) {
  if (_onAuthStateChangedCallback)
    _onAuthStateChangedCallback(user)
}

export const mockSignInWithEmailAndPassword = vi.fn()
export const mockSignInWithPopup = vi.fn()
export const mockSignOut = vi.fn().mockResolvedValue(undefined)
export const mockSendPasswordResetEmail = vi.fn().mockResolvedValue(undefined)
export const mockOnAuthStateChanged = vi.fn((auth: unknown, callback: (user: MockFirebaseUser | null) => void) => {
  _onAuthStateChangedCallback = callback
  callback(_mockCurrentUser)
  return vi.fn() // unsubscribe
})

/**
 * Returns the mock factory for firebase/auth module.
 * Use at the top level of your test file with vi.mock('firebase/auth', firebaseAuthFactory).
 */
export function firebaseAuthFactory() {
  return {
    signInWithEmailAndPassword: mockSignInWithEmailAndPassword,
    signInWithPopup: mockSignInWithPopup,
    signOut: mockSignOut,
    sendPasswordResetEmail: mockSendPasswordResetEmail,
    onAuthStateChanged: mockOnAuthStateChanged,
    GoogleAuthProvider: vi.fn(),
    OAuthProvider: vi.fn().mockImplementation(() => ({
      addScope: vi.fn(),
    })),
    getAuth: vi.fn(() => ({ currentUser: _mockCurrentUser })),
  }
}

/**
 * Returns the mock factory for @/plugins/firebase module.
 * Use at the top level of your test file with vi.mock('@/plugins/firebase', firebasePluginFactory).
 */
export function firebasePluginFactory() {
  return {
    firebaseAuth: { currentUser: _mockCurrentUser },
    firebaseApp: {},
  }
}

/**
 * Reset all mock state between tests.
 */
export function resetFirebaseMocks() {
  _onAuthStateChangedCallback = null
  _mockCurrentUser = null
  mockSignInWithEmailAndPassword.mockReset()
  mockSignInWithPopup.mockReset()
  mockSignOut.mockReset().mockResolvedValue(undefined)
  mockSendPasswordResetEmail.mockReset().mockResolvedValue(undefined)
  mockOnAuthStateChanged.mockReset()
}
