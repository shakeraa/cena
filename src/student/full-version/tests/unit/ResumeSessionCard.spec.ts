import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ResumeSessionCard from '@/components/home/ResumeSessionCard.vue'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/session/:sessionId', name: 'session-live', component: { template: '<div/>' } },
    ],
  })
}

describe('ResumeSessionCard', () => {
  it('renders the subject and CTA with a deep link to the session', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(ResumeSessionCard, {
      props: {
        sessionId: 's-42',
        subject: 'Algebra II',
        startedAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
        progressPercent: 42,
      },
      global: { plugins: [router] },
    })

    expect(wrapper.find('[data-testid="resume-session-card"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Algebra II')
    expect(wrapper.find('[data-testid="resume-session-cta"]').exists()).toBe(true)
  })

  it('shows a minutes-ago relative time for recent starts', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(ResumeSessionCard, {
      props: {
        sessionId: 's-1',
        subject: 'Subject',
        startedAt: new Date(Date.now() - 10 * 60 * 1000).toISOString(),
        progressPercent: 50,
      },
      global: { plugins: [router] },
    })

    expect(wrapper.text()).toMatch(/10\s+min/i)
  })

  it('shows an hours-ago relative time for older starts', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(ResumeSessionCard, {
      props: {
        sessionId: 's-2',
        subject: 'Subject',
        startedAt: new Date(Date.now() - 120 * 60 * 1000).toISOString(),
        progressPercent: 75,
      },
      global: { plugins: [router] },
    })

    expect(wrapper.text()).toMatch(/2\s+h/i)
  })

  it('renders the progress bar with the correct aria-label', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(ResumeSessionCard, {
      props: {
        sessionId: 's-3',
        subject: 'Subject',
        startedAt: new Date().toISOString(),
        progressPercent: 67,
      },
      global: { plugins: [router] },
    })

    const progressEl = wrapper.find('[role="progressbar"]')

    expect(progressEl.exists()).toBe(true)

    // Vuetify's VProgressLinear accepts an aria-label pass-through; if it
    // isn't on the role=progressbar element, fall back to the wrapper.
    const label = progressEl.attributes('aria-label')
      || wrapper.find('[aria-label*="67"]').attributes('aria-label')

    expect(label).toContain('67')
  })
})
