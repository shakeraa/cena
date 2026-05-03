import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import DailyChallengeLeaderboard from '@/components/challenges/DailyChallengeLeaderboard.vue'
import type { DailyChallengeLeaderboardDto } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        challenges: {
          daily: {
            leaderboard: {
              title: 'Today’s leaderboard',
              subtitle: 'Live results.',
              rankHeader: '#',
              studentHeader: 'Student',
              scoreHeader: 'Score',
              timeHeader: 'Time',
              you: 'You',
              yourRank: 'Your rank: {rank}',
              noRankYet: 'Finish today’s challenge to see your rank.',
              empty: 'No one has finished today’s challenge yet — be first.',
            },
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

function makeData(overrides: Partial<DailyChallengeLeaderboardDto> = {}): DailyChallengeLeaderboardDto {
  return {
    entries: [
      { rank: 1, studentId: 'u1', displayName: 'Alex Chen', score: 100, timeSeconds: 42 },
      { rank: 2, studentId: 'u2', displayName: 'Priya Rao', score: 95, timeSeconds: 48 },
      { rank: 3, studentId: 'me', displayName: 'You', score: 88, timeSeconds: 60 },
    ],
    currentStudentRank: 3,
    ...overrides,
  }
}

describe('DailyChallengeLeaderboard', () => {
  it('renders all entries and the own-rank line', () => {
    const wrapper = mount(DailyChallengeLeaderboard, {
      props: { leaderboard: makeData() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-leaderboard-row-1"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="daily-leaderboard-row-2"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="daily-leaderboard-row-3"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="daily-leaderboard-own-rank"]').text()).toContain('3')
    expect(wrapper.find('[data-testid="daily-leaderboard-you-chip"]').exists()).toBe(true)
  })

  it('shows the empty state when there are no entries', () => {
    const wrapper = mount(DailyChallengeLeaderboard, {
      props: { leaderboard: makeData({ entries: [], currentStudentRank: null }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="daily-leaderboard-empty"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="daily-leaderboard-no-rank"]').exists()).toBe(true)
  })

  it('formats time in m:ss when at least one minute has passed', () => {
    const wrapper = mount(DailyChallengeLeaderboard, {
      props: {
        leaderboard: {
          entries: [{ rank: 1, studentId: 'u1', displayName: 'Alex', score: 100, timeSeconds: 75 }],
          currentStudentRank: null,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    // 75 seconds → 1:15
    expect(wrapper.find('[data-testid="daily-leaderboard-row-1"]').text()).toContain('1:15')
  })

  it('wraps numeric cells in <bdi dir="ltr"> for RTL safety', () => {
    const wrapper = mount(DailyChallengeLeaderboard, {
      props: { leaderboard: makeData() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const row = wrapper.find('[data-testid="daily-leaderboard-row-1"]')
    const bdiTags = row.findAll('bdi')
    expect(bdiTags.length).toBeGreaterThanOrEqual(3) // rank + score + time
    bdiTags.forEach(b => expect(b.attributes('dir')).toBe('ltr'))
  })
})
