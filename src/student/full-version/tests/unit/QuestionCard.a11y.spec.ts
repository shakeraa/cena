// =============================================================================
// RDY-030: Component-level a11y test for QuestionCard via vitest-axe.
// Runs axe on the mounted component's DOM, asserts zero serious/critical
// WCAG 2.1 AA violations on both default and answered states.
// =============================================================================

import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import { axe, toHaveNoViolations } from 'vitest-axe'
import QuestionCard from '@/components/session/QuestionCard.vue'
import type { SessionQuestionDto } from '@/api/types/common'

expect.extend({ toHaveNoViolations })

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          runner: {
            questionProgress: 'Question {current} of {total}',
            progressAria: 'Question {current} of {total}',
            choicesAria: 'Choices',
            submitAnswer: 'Submit',
            workedExampleLabel: 'Worked example',
            hintLevel: 'Hint {level}',
            requestHint: 'Show a hint ({remaining} left)',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const question: SessionQuestionDto = {
  questionId: 'q_a11y_001',
  questionIndex: 0,
  totalQuestions: 5,
  prompt: 'What is 12 × 8?',
  questionType: 'multiple-choice',
  choices: ['92', '96', '104', '108'],
  subject: 'Mathematics',
  expectedTimeSeconds: 30,
}

describe('QuestionCard a11y (RDY-030)', () => {
  it('has no WCAG 2.1 AA violations in initial state', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
      attachTo: document.body,
    })

    const results = await axe(wrapper.element, {
      runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'],
    })

    expect(results).toHaveNoViolations()
    wrapper.unmount()
  })

  it('has no WCAG 2.1 AA violations after selecting a choice', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
      attachTo: document.body,
    })

    await wrapper.find('[data-testid="choice-96"]').trigger('click')
    await wrapper.vm.$nextTick()

    const results = await axe(wrapper.element, {
      runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'],
    })

    expect(results).toHaveNoViolations()
    wrapper.unmount()
  })
})
