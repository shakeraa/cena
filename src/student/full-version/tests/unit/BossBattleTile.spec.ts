import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import BossBattleTile from '@/components/challenges/BossBattleTile.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        challenges: {
          boss: {
            requiresLevel: 'Requires mastery level {level}',
          },
          difficulty: {
            easy: 'Easy',
            medium: 'Medium',
            hard: 'Hard',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('BossBattleTile', () => {
  it('renders an available boss without the lock reason', () => {
    const wrapper = mount(BossBattleTile, {
      props: {
        boss: {
          bossBattleId: 'boss-1',
          name: 'Algebra Overlord',
          subject: 'math',
          difficulty: 'hard',
          requiredMasteryLevel: 5,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Algebra Overlord')
    expect(wrapper.text()).toContain('Hard')
    expect(wrapper.find('[data-testid="boss-lock-reason"]').exists()).toBe(false)
    expect(wrapper.classes()).not.toContain('boss-tile--locked')
  })

  it('renders a locked boss with the required-level reason', () => {
    const wrapper = mount(BossBattleTile, {
      props: {
        boss: {
          bossBattleId: 'boss-2',
          name: 'Calculus King',
          subject: 'math',
          difficulty: 'hard',
          requiredMasteryLevel: 8,
        },
        locked: true,
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Calculus King')
    expect(wrapper.find('[data-testid="boss-lock-reason"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="boss-lock-reason"]').text()).toContain('8')
  })

  it('emits "select" with the boss id when an unlocked tile is activated', async () => {
    const wrapper = mount(BossBattleTile, {
      props: {
        boss: {
          bossBattleId: 'boss-9',
          name: 'Motion Master',
          subject: 'physics',
          difficulty: 'medium',
          requiredMasteryLevel: 4,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="boss-boss-9"]').trigger('click')

    expect(wrapper.emitted('select')).toEqual([['boss-9']])
  })

  it('does not emit "select" for a locked tile', async () => {
    const wrapper = mount(BossBattleTile, {
      props: {
        boss: {
          bossBattleId: 'boss-locked',
          name: 'Calculus King',
          subject: 'math',
          difficulty: 'hard',
          requiredMasteryLevel: 8,
        },
        locked: true,
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="boss-boss-locked"]').trigger('click')

    expect(wrapper.emitted('select')).toBeUndefined()
  })

  it('does not emit while a previous start is still pending', async () => {
    const wrapper = mount(BossBattleTile, {
      props: {
        boss: {
          bossBattleId: 'boss-busy',
          name: 'Algebra Overlord',
          subject: 'math',
          difficulty: 'hard',
          requiredMasteryLevel: 5,
        },
        starting: true,
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="boss-boss-busy"]').trigger('click')

    expect(wrapper.emitted('select')).toBeUndefined()
  })
})
