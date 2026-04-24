/**
 * Tests for useFirebaseAuth composable.
 *
 * Validates:
 *  - loginWithEmail delegates to Firebase and extracts CenaUser
 *  - Non-admin roles are rejected with sign-out
 *  - Error codes map to user-friendly messages
 *  - logout clears user state
 *  - resetPassword delegates to Firebase
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'

// --- Inline mock fns (must be defined before vi.mock hoisting) ---
const mockSignInWithEmailAndPassword = vi.fn()
const mockSignOut = vi.fn().mockResolvedValue(undefined)
const mockSendPasswordResetEmail = vi.fn().mockResolvedValue(undefined)

// Top-level vi.mock calls
vi.mock('firebase/auth', () => ({
  signInWithEmailAndPassword: mockSignInWithEmailAndPassword,
  signInWithPopup: vi.fn(),
  signOut: mockSignOut,
  sendPasswordResetEmail: mockSendPasswordResetEmail,
  onAuthStateChanged: vi.fn((_auth: unknown, cb: (u: null) => void) => { cb(null); return vi.fn() }),
  GoogleAuthProvider: vi.fn(),
  OAuthProvider: vi.fn().mockImplementation(() => ({ addScope: vi.fn() })),
  getAuth: vi.fn(() => ({ currentUser: null })),
}))

vi.mock('@/plugins/firebase', () => ({
  firebaseAuth: { currentUser: null },
  firebaseApp: {},
}))

vi.mock('@/plugins/casl/ability', () => ({
  ability: {
    update: vi.fn(),
    rules: [],
    can: vi.fn(() => true),
  },
}))

vi.mock('@/plugins/casl/role-abilities', () => ({
  mapRoleToAbilities: vi.fn((role: string) => {
    if (role === 'SUPER_ADMIN') return [{ action: 'manage', subject: 'all' }]
    if (role === 'ADMIN') return [{ action: 'manage', subject: 'Users' }]
    return []
  }),
}))

// Mock useCookie (Vuexy auto-import)
const cookieStore: Record<string, unknown> = {}
vi.stubGlobal('useCookie', (name: string) => ({
  get value() { return cookieStore[name] ?? null },
  set value(v: unknown) { cookieStore[name] = v },
}))

// Mock Vue ref
vi.stubGlobal('ref', (v: unknown) => ({ value: v }))

function createMockUser(overrides: Record<string, unknown> = {}) {
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

describe('useFirebaseAuth', () => {
  beforeEach(() => {
    mockSignInWithEmailAndPassword.mockReset()
    mockSignOut.mockReset().mockResolvedValue(undefined)
    mockSendPasswordResetEmail.mockReset().mockResolvedValue(undefined)
    Object.keys(cookieStore).forEach(k => delete cookieStore[k])
    vi.resetModules()
  })

  it('loginWithEmail returns CenaUser for ADMIN role', async () => {
    const mockUser = createMockUser()
    mockSignInWithEmailAndPassword.mockResolvedValue({ user: mockUser })

    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { loginWithEmail } = useFirebaseAuth()
    const result = await loginWithEmail('admin@cena.edu', 'password123')

    expect(result).toBeDefined()
    expect(result!.uid).toBe('test-uid-001')
    expect(result!.email).toBe('admin@cena.edu')
    expect(result!.role).toBe('ADMIN')
    expect(result!.schoolId).toBe('school-001')
    expect(mockSignInWithEmailAndPassword).toHaveBeenCalledOnce()
  })

  it('loginWithEmail rejects STUDENT role with sign-out', async () => {
    const mockUser = createMockUser({
      getIdTokenResult: vi.fn().mockResolvedValue({
        token: 'mock-token',
        claims: { role: 'STUDENT' },
      }),
    })
    mockSignInWithEmailAndPassword.mockResolvedValue({ user: mockUser })

    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { loginWithEmail } = useFirebaseAuth()

    await expect(loginWithEmail('student@cena.edu', 'pass')).rejects.toThrow()
    expect(mockSignOut).toHaveBeenCalled()
  })

  it('loginWithEmail maps auth/invalid-credential to friendly message', async () => {
    mockSignInWithEmailAndPassword.mockRejectedValue({
      code: 'auth/invalid-credential',
      message: 'Firebase: Error',
    })

    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { loginWithEmail, authError } = useFirebaseAuth()

    await expect(loginWithEmail('wrong@cena.edu', 'wrong')).rejects.toBeDefined()
    expect(authError.value).toBe('Invalid email or password')
  })

  it('loginWithEmail maps auth/too-many-requests to rate-limit message', async () => {
    mockSignInWithEmailAndPassword.mockRejectedValue({
      code: 'auth/too-many-requests',
      message: 'Firebase: Error',
    })

    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { loginWithEmail, authError } = useFirebaseAuth()

    await expect(loginWithEmail('a@b.com', 'x')).rejects.toBeDefined()
    expect(authError.value).toBe('Too many failed attempts. Please try again later.')
  })

  it('logout clears currentUser and calls signOut', async () => {
    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { logout, currentUser } = useFirebaseAuth()

    await logout()

    expect(currentUser.value).toBeNull()
    expect(mockSignOut).toHaveBeenCalled()
    expect(cookieStore.userData).toBeNull()
    expect(cookieStore.accessToken).toBeNull()
  })

  it('resetPassword delegates to sendPasswordResetEmail', async () => {
    const { useFirebaseAuth } = await import('@/composables/useFirebaseAuth')
    const { resetPassword } = useFirebaseAuth()

    await resetPassword('admin@cena.edu')

    expect(mockSendPasswordResetEmail).toHaveBeenCalledOnce()
  })
})
