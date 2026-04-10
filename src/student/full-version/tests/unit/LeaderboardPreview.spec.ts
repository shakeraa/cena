import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import LeaderboardPreview from '@/components/progress/LeaderboardPreview.vue'
import type { LeaderboardDto } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        gamification: {
          leaderboard: {
            title: 'Leaderboard',
            yourRank: 'Your rank: #{rank}',
            youChip: 'You',
            xpLabel: '{xp} XP',
            xpValue: '{xp} XP',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const sample: LeaderboardDto = {
  scope: 'global',
  currentStudentRank: 3,
  entries: [
    { rank: 1, studentId: 'u-1', displayName: 'Alex', xp: 2400, avatarUrl: null },
    { rank: 2, studentId: 'u-2', displayName: 'Priya', xp: 2200, avatarUrl: null },
    { rank: 3, studentId: 'u-me', displayName: 'Dev Student', xp: 2000, avatarUrl: null },
    { rank: 4, studentId: 'u-4', displayName: 'Sam', xp: 1800, avatarUrl: null },
    { rank: 5, studentId: 'u-5', displayName: 'Jordan', xp: 1600, avatarUrl: null },
    { rank: 6, studentId: 'u-6', displayName: 'Riley', xp: 1400, avatarUrl: null },
  ],
}

describe('LeaderboardPreview', () => {
  it('renders top N entries based on limit prop', () => {
    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: sample, currentStudentId: 'u-me', limit: 5 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.findAll('[data-testid^="leaderboard-entry-"]')).toHaveLength(5)
    expect(wrapper.find('[data-testid="leaderboard-entry-1"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="leaderboard-entry-5"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="leaderboard-entry-6"]').exists()).toBe(false)
  })

  it('shows the current student rank chip', () => {
    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: sample, currentStudentId: 'u-me', limit: 5 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const chip = wrapper.find('[data-testid="leaderboard-your-rank"]')

    expect(chip.text()).toContain('3')
  })

  it('highlights the entry matching currentStudentId with a "You" chip', () => {
    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: sample, currentStudentId: 'u-me', limit: 5 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const myEntry = wrapper.find('[data-testid="leaderboard-entry-3"]')

    expect(myEntry.classes()).toContain('leaderboard-preview__you')
    expect(myEntry.text()).toContain('You')
  })

  it('defaults to limit 5 when prop omitted', () => {
    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: sample, currentStudentId: 'u-me' },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.findAll('[data-testid^="leaderboard-entry-"]')).toHaveLength(5)
  })
})
