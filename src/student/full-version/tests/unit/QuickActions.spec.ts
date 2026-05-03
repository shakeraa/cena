import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import QuickActions from '@/components/home/QuickActions.vue'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div/>' } },
      { path: '/session', name: 'session', component: { template: '<div/>' } },
      { path: '/tutor', name: 'tutor', component: { template: '<div/>' } },
      { path: '/challenges/daily', name: 'challenges-daily', component: { template: '<div/>' } },
      { path: '/progress', name: 'progress', component: { template: '<div/>' } },
    ],
  })
}

describe('QuickActions', () => {
  it('renders all four quick action tiles', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(QuickActions, {
      global: { plugins: [router] },
    })

    expect(wrapper.find('[data-testid="quick-actions"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="quick-action-session"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="quick-action-tutor"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="quick-action-challenge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="quick-action-progress"]').exists()).toBe(true)
  })

  it('each tile has an accessible aria-label', async () => {
    const router = makeRouter()

    await router.push('/')

    const wrapper = mount(QuickActions, {
      global: { plugins: [router] },
    })

    const tiles = wrapper.findAll('[data-testid^="quick-action-"]')

    expect(tiles.length).toBe(4)
    for (const tile of tiles)
      expect(tile.attributes('aria-label')).toBeTruthy()
  })
})
