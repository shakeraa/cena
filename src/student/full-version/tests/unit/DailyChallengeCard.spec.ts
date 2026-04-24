import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import DailyChallengeCard from '@/components/challenges/DailyChallengeCard.vue'
import type { DailyChallengeDto } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        challenges: {
          daily: {
            label: 'Today',
            start: 'Start',
            reattempt: 'Try again',
            attempted: 'Attempted',
            expired: 'Expired',
            timeLeft: '{hours}h {minutes}m left',
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

function makeChallenge(overrides: Partial<DailyChallengeDto> = {}): DailyChallengeDto {
  return {
    challengeId: 'c1',
    title: 'Mental Math Sprint',
    description: 'Five quick questions.',
    subject: 'math',
    difficulty: 'medium',
    expiresAt: new Date(Date.now() + 2 * 3600_000).toISOString(),
    attempted: false,
    bestScore: null,
    ...overrides,
  }
}

describe('DailyChallengeCard', () => {
  it('renders challenge title, description, and start button', () => {
    const wrapper = mount(DailyChallengeCard, {
      props: { challenge: makeChallenge() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Mental Math Sprint')
    expect(wrapper.text()).toContain('Five quick questions.')
    expect(wrapper.find('[data-testid="daily-start"]').text()).toBe('Start')
  })

  it('shows "Try again" when the challenge has already been attempted', () => {
    const wrapper = mount(DailyChallengeCard, {
      props: { challenge: makeChallenge({ attempted: true, bestScore: 85 }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-start"]').text()).toBe('Try again')
    expect(wrapper.find('[data-testid="daily-attempted"]').exists()).toBe(true)
  })

  it('renders difficulty chip with the correct label', () => {
    const wrapper = mount(DailyChallengeCard, {
      props: { challenge: makeChallenge({ difficulty: 'hard' }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-difficulty"]').text()).toBe('Hard')
  })

  it('shows "Expired" when the challenge has passed its expiry', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-10T12:00:00Z'))

    const wrapper = mount(DailyChallengeCard, {
      props: {
        challenge: makeChallenge({ expiresAt: '2026-04-10T11:00:00Z' }),
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-time-left"]').text()).toContain('Expired')

    vi.useRealTimers()
  })

  it('shows time remaining in hours/minutes when active', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-10T12:00:00Z'))

    const wrapper = mount(DailyChallengeCard, {
      props: {
        challenge: makeChallenge({ expiresAt: '2026-04-10T15:30:00Z' }),
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-time-left"]').text()).toContain('3h 30m')

    vi.useRealTimers()
  })
})
