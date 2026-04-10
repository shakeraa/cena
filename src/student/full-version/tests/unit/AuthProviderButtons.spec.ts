import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import AuthProviderButtons from '@/components/common/AuthProviderButtons.vue'
import { useAuthStore } from '@/stores/authStore'
import { useMeStore } from '@/stores/meStore'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/home', name: 'home', component: { template: '<div/>' } },
      { path: '/onboarding', name: 'onboarding', component: { template: '<div/>' } },
      { path: '/progress/mastery', name: 'progress-mastery', component: { template: '<div/>' } },
    ],
  })
}

describe('AuthProviderButtons', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders all four provider buttons', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    expect(wrapper.find('[data-testid="auth-provider-google"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="auth-provider-apple"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="auth-provider-microsoft"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="auth-provider-phone"]').exists()).toBe(true)
  })

  it('clicking Google in login mode signs the user in via mockSignIn and navigates to /home', async () => {
    const router = makeRouter()

    await router.push('/')

    const pushSpy = vi.spyOn(router, 'replace')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    await wrapper.find('[data-testid="auth-provider-google"]').trigger('click')

    // wait for the 150ms simulated latency + vue nextTick
    await new Promise(resolve => setTimeout(resolve, 200))

    const authStore = useAuthStore()

    expect(authStore.isSignedIn).toBe(true)
    expect(authStore.uid).toMatch(/^mock-google-/)

    const meStore = useMeStore()

    expect(meStore.isOnboarded).toBe(true)

    expect(pushSpy).toHaveBeenCalledWith('/home')
  })

  it('clicking Microsoft in register mode signs the user in with onboardedAt=null and navigates to /onboarding', async () => {
    const router = makeRouter()

    await router.push('/')

    const pushSpy = vi.spyOn(router, 'replace')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'register' },
      global: { plugins: [router] },
    })

    await wrapper.find('[data-testid="auth-provider-microsoft"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 200))

    const authStore = useAuthStore()

    expect(authStore.isSignedIn).toBe(true)
    expect(authStore.uid).toMatch(/^mock-microsoft-/)

    const meStore = useMeStore()

    expect(meStore.isOnboarded).toBe(false)

    expect(pushSpy).toHaveBeenCalledWith('/onboarding')
  })

  it('clicking Phone shows the coming-soon alert and does NOT sign in', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(AuthProviderButtons, {
      props: { mode: 'login' },
      global: { plugins: [router] },
    })

    await wrapper.find('[data-testid="auth-provider-phone"]').trigger('click')
    await new Promise(resolve => setTimeout(resolve, 50))

    expect(wrapper.find('[data-testid="auth-provider-phone-message"]').exists()).toBe(true)

    const authStore = useAuthStore()

    expect(authStore.isSignedIn).toBe(false)
  })
})
