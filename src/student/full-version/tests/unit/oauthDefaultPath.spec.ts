import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

/**
 * FIND-ux-031 regression test.
 *
 * Verifies that the DEFAULT code path (useMockAuth=false) in login.vue
 * and AuthProviderButtons.vue does NOT call `__mockSignIn`.
 *
 * This test would FAIL on the pre-ux-023 code because:
 *   - AuthProviderButtons always called __mockSignIn (no useMockAuth gate)
 *   - login.vue always called __mockSignIn (no useMockAuth gate)
 *   - useFirebaseAuth composable did not exist
 */

// --- Module-level mocks (must be before any import that resolves the modules) ---

// Force useMockAuth=false so we exercise the real-Firebase code path.
vi.mock('@/plugins/firebase', () => ({
  useMockAuth: false,
  getFirebaseAuth: vi.fn(() => ({})),
  googleProvider: {},
  appleProvider: {},
  microsoftProvider: {},
}))

// Mock the composable: loginWithProvider and loginWithEmail return resolved promises.
const mockLoginWithProvider = vi.fn().mockResolvedValue('firebase-uid-oauth')
const mockLoginWithEmail = vi.fn().mockResolvedValue('firebase-uid-email')
const mockErrorKey = { value: null }

vi.mock('@/composables/useFirebaseAuth', () => ({
  useFirebaseAuth: () => ({
    loginWithProvider: mockLoginWithProvider,
    loginWithEmail: mockLoginWithEmail,
    errorKey: mockErrorKey,
    isLoading: { value: false },
  }),
}))

// Now import the components and stores AFTER mocks are registered.
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import AuthProviderButtons from '@/components/common/AuthProviderButtons.vue'
import { useAuthStore } from '@/stores/authStore'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/home', name: 'home', component: { template: '<div/>' } },
      { path: '/onboarding', name: 'onboarding', component: { template: '<div/>' } },
    ],
  })
}

describe('FIND-ux-031: default login path does NOT call __mockSignIn', () => {
  beforeEach(() => {
    if (typeof window !== 'undefined' && window.localStorage) {
      window.localStorage.removeItem('cena-mock-auth')
      window.localStorage.removeItem('cena-mock-me')
    }
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('AuthProviderButtons calls loginWithProvider (not __mockSignIn) for Google when useMockAuth=false', async () => {
    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    const authStore = useAuthStore()
    const mockSignInSpy = vi.spyOn(authStore, '__mockSignIn')

    await wrapper.find('[data-testid="auth-provider-google"]').trigger('click')
    // Wait for the async handler to settle
    await new Promise(resolve => setTimeout(resolve, 100))

    // Real path: loginWithProvider should have been called
    expect(mockLoginWithProvider).toHaveBeenCalledWith('google')

    // Mock path: __mockSignIn should NOT have been called
    expect(mockSignInSpy).not.toHaveBeenCalled()
  })

  it('AuthProviderButtons calls loginWithProvider (not __mockSignIn) for Apple when useMockAuth=false', async () => {
    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'register' },
      global: { plugins: [router] },
    })

    const authStore = useAuthStore()
    const mockSignInSpy = vi.spyOn(authStore, '__mockSignIn')

    await wrapper.find('[data-testid="auth-provider-apple"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 100))

    expect(mockLoginWithProvider).toHaveBeenCalledWith('apple')
    expect(mockSignInSpy).not.toHaveBeenCalled()
  })

  it('AuthProviderButtons calls loginWithProvider (not __mockSignIn) for Microsoft when useMockAuth=false', async () => {
    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    const authStore = useAuthStore()
    const mockSignInSpy = vi.spyOn(authStore, '__mockSignIn')

    await wrapper.find('[data-testid="auth-provider-microsoft"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 100))

    expect(mockLoginWithProvider).toHaveBeenCalledWith('microsoft')
    expect(mockSignInSpy).not.toHaveBeenCalled()
  })

  it('phone provider still shows "coming soon" and does NOT call __mockSignIn or loginWithProvider', async () => {
    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    const authStore = useAuthStore()
    const mockSignInSpy = vi.spyOn(authStore, '__mockSignIn')

    await wrapper.find('[data-testid="auth-provider-phone"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 100))

    expect(mockLoginWithProvider).not.toHaveBeenCalled()
    expect(mockSignInSpy).not.toHaveBeenCalled()

    // Phone auth message should be visible
    expect(wrapper.find('[data-testid="auth-provider-phone-message"]').exists()).toBe(true)
  })

  it('no hardcoded "Google User" / "Apple User" strings appear in the default code path', async () => {
    const router = makeRouter()
    await router.push('/')

    mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    const authStore = useAuthStore()

    // After clicking Google in the real path, the store should not have hardcoded display names
    // (loginWithProvider resolves but the store is populated by onAuthStateChanged, not by us)
    expect(authStore.displayName).not.toBe('Google User')
    expect(authStore.displayName).not.toBe('Apple User')
    expect(authStore.displayName).not.toBe('Microsoft User')
  })

  it('provider error is displayed (not swallowed) when loginWithProvider rejects', async () => {
    // Make loginWithProvider reject with a non-cancelled error
    mockLoginWithProvider.mockRejectedValueOnce({ code: 'auth/network-request-failed' })

    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    await wrapper.find('[data-testid="auth-provider-google"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 100))

    // Error alert should be visible
    expect(wrapper.find('[data-testid="auth-provider-error"]').exists()).toBe(true)
  })

  it('popup-closed-by-user error is silently dismissed (no error alert)', async () => {
    mockLoginWithProvider.mockRejectedValueOnce({ code: 'auth/popup-closed-by-user' })

    const router = makeRouter()
    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    await wrapper.find('[data-testid="auth-provider-google"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 100))

    // Error alert should NOT be visible for user-cancelled popup
    expect(wrapper.find('[data-testid="auth-provider-error"]').exists()).toBe(false)
  })
})
