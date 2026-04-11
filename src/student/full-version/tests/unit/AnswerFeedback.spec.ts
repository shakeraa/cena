import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import AnswerFeedback from '@/components/session/AnswerFeedback.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          runner: {
            correct: 'Correct!',
            wrong: 'Not quite.',
            xpAwarded: '+{xp} XP',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('AnswerFeedback', () => {
  it('renders the correct state with XP awarded', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: true,
          feedback: 'Great job!',
          xpAwarded: 10,
          masteryDelta: 0.05,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="answer-feedback"]')

    expect(root.attributes('data-correct')).toBe('true')
    expect(wrapper.text()).toContain('Correct!')
    expect(wrapper.text()).toContain('Great job!')
    expect(wrapper.find('[data-testid="feedback-xp"]').text()).toContain('10')
  })

  it('renders the wrong state without XP awarded', () => {
    const wrapper = mount(AnswerFeedback, {
      props: {
        feedback: {
          correct: false,
          feedback: 'Not quite — try again next time.',
          xpAwarded: 0,
          masteryDelta: -0.02,
          nextQuestionId: 'q_002',
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="answer-feedback"]')

    expect(root.attributes('data-correct')).toBe('false')
    expect(wrapper.text()).toContain('Not quite.')
    expect(wrapper.find('[data-testid="feedback-xp"]').exists()).toBe(false)
  })
})
