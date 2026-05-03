import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import SessionSummaryCard from '@/components/session/SessionSummaryCard.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          summary: {
            title: 'Complete!',
            subtitle: 'Nice work.',
            xpEarned: 'XP earned',
            accuracy: 'Accuracy',
            correctCount: 'Correct',
            duration: 'Duration',
            durationSecs: '{secs}s',
            durationMinsSecs: '{mins}m {secs}s',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('SessionSummaryCard', () => {
  it('renders xp, accuracy, correct-count, and duration stats', () => {
    const wrapper = mount(SessionSummaryCard, {
      props: {
        summary: {
          sessionId: 's-1',
          totalCorrect: 4,
          totalWrong: 1,
          totalXpAwarded: 40,
          accuracyPercent: 80,
          durationSeconds: 125,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="summary-xp"]').text()).toContain('40')
    expect(wrapper.find('[data-testid="summary-accuracy"]').text()).toContain('80%')
    expect(wrapper.find('[data-testid="summary-correct"]').text()).toContain('4 / 5')
    expect(wrapper.find('[data-testid="summary-duration"]').text()).toContain('2m 5s')
  })

  it('formats sub-minute durations as seconds only', () => {
    const wrapper = mount(SessionSummaryCard, {
      props: {
        summary: {
          sessionId: 's-1',
          totalCorrect: 5,
          totalWrong: 0,
          totalXpAwarded: 50,
          accuracyPercent: 100,
          durationSeconds: 45,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="summary-duration"]').text()).toContain('45s')
    expect(wrapper.find('[data-testid="summary-duration"]').text()).not.toContain('m')
  })
})
