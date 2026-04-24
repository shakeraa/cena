import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import XpProgressCard from '@/components/progress/XpProgressCard.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        gamification: {
          xp: {
            levelLabel: 'Level',
            currentXp: '{current} / {target} XP',
            xpToGo: '{count} XP to go',
            totalEarned: 'Total: {total} XP',
            progressAria: 'Level progress: {percent} percent',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('XpProgressCard', () => {
  it('renders current level and XP progress', () => {
    const wrapper = mount(XpProgressCard, {
      props: {
        xp: {
          currentLevel: 7,
          currentXp: 180,
          xpToNextLevel: 250,
          totalXpEarned: 1680,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="xp-current-level"]').text()).toBe('7')
    expect(wrapper.find('[data-testid="xp-current-xp"]').text()).toContain('180')
    expect(wrapper.find('[data-testid="xp-current-xp"]').text()).toContain('250')
    expect(wrapper.find('[data-testid="xp-total-earned"]').text()).toContain('1680')
  })

  it('clamps progress bar at 100% when currentXp exceeds xpToNextLevel', () => {
    const wrapper = mount(XpProgressCard, {
      props: {
        xp: {
          currentLevel: 5,
          currentXp: 500,
          xpToNextLevel: 300,
          totalXpEarned: 2000,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const progress = wrapper.find('[data-testid="xp-progress-bar"]')

    expect(progress.attributes('aria-label')).toContain('100')
  })

  it('shows 0 xp remaining when currentXp === xpToNextLevel', () => {
    const wrapper = mount(XpProgressCard, {
      props: {
        xp: {
          currentLevel: 3,
          currentXp: 200,
          xpToNextLevel: 200,
          totalXpEarned: 800,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('0 XP to go')
  })
})
