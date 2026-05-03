import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import LeaderboardPreview from '@/components/progress/LeaderboardPreview.vue'
import type { LeaderboardDto } from '@/api/types/common'

/**
 * FIND-qa-006 / FIND-ux-009: Leaderboard "you" row regression tests
 *
 * Verifies the "you" row is determined by currentStudentId prop matching
 * entry.studentId, NOT by hardcoded "Dev Student (You)" string.
 *
 * Background: Previously the leaderboard hardcoded the current user's
 * display name as "Dev Student (You)". The fix makes the component
 * accept a currentStudentId prop and highlights the matching entry.
 */

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

  /**
   * FIND-ux-009: The "you" row should resolve from currentStudentId prop,
   * not be hardcoded as "Dev Student (You)".
   */
  it('highlights entry matching currentStudentId regardless of displayName', () => {
    // Use a different display name - should still highlight based on studentId
    const customSample: LeaderboardDto = {
      scope: 'global',
      currentStudentRank: 2,
      entries: [
        { rank: 1, studentId: 'u-1', displayName: 'Alex', xp: 2400, avatarUrl: null },

        // Note: displayName is "Custom User", not "Dev Student"
        { rank: 2, studentId: 'u-custom', displayName: 'Custom User', xp: 2200, avatarUrl: null },
        { rank: 3, studentId: 'u-3', displayName: 'Sam', xp: 1800, avatarUrl: null },
      ],
    }

    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: customSample, currentStudentId: 'u-custom', limit: 3 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    // Should highlight rank 2 (the matching entry), not look for "Dev Student"
    const myEntry = wrapper.find('[data-testid="leaderboard-entry-2"]')

    expect(myEntry.classes()).toContain('leaderboard-preview__you')
    expect(myEntry.text()).toContain('Custom User') // NOT "Dev Student"
    expect(myEntry.text()).toContain('You') // The chip still says "You"
  })

  /**
   * Synthetic regression: if currentStudentId doesn't match any entry,
   * no row should have the "you" highlight class.
   */
  it('does not highlight any row when currentStudentId is not in entries', () => {
    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: sample, currentStudentId: 'u-nonexistent', limit: 5 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    // No entry should have the "you" class
    const youEntries = wrapper.findAll('.leaderboard-preview__you')

    expect(youEntries).toHaveLength(0)
  })

  /**
   * Synthetic regression test: verifies the "you" chip is shown based on
   * studentId match, not hardcoded display name check.
   */
  it('fails synthetic regression if displayName was hardcoded', () => {
    // If the component had hardcoded logic like:
    //   if (entry.displayName === 'Dev Student') { showYouChip() }
    // this test would fail because we're using a different display name.

    const regressionSample: LeaderboardDto = {
      scope: 'global',
      currentStudentRank: 1,
      entries: [
        // entry with different display name but matching studentId
        { rank: 1, studentId: 'u-test', displayName: 'Testy McTestface', xp: 1000, avatarUrl: null },
      ],
    }

    const wrapper = mount(LeaderboardPreview, {
      props: { leaderboard: regressionSample, currentStudentId: 'u-test', limit: 1 },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const myEntry = wrapper.find('[data-testid="leaderboard-entry-1"]')

    // The "you" chip should still appear even though displayName is NOT "Dev Student"
    expect(myEntry.classes()).toContain('leaderboard-preview__you')
    expect(myEntry.text()).toContain('You')
    expect(myEntry.text()).toContain('Testy McTestface')
    expect(myEntry.text()).not.toContain('Dev Student')
  })
})
