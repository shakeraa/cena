import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import QuestionCard from '@/components/session/QuestionCard.vue'
import type { SessionQuestionDto } from '@/api/types/common'

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
  questionId: 'q_001',
  questionIndex: 0,
  totalQuestions: 5,
  prompt: 'What is 12 × 8?',
  questionType: 'multiple-choice',
  choices: ['92', '96', '104', '108'],
  subject: 'Mathematics',
  expectedTimeSeconds: 30,
}

describe('QuestionCard', () => {
  it('renders prompt, subject, progress, and all 4 choices', () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="question-prompt"]').text()).toBe('What is 12 × 8?')
    expect(wrapper.find('[data-testid="question-progress"]').text()).toContain('1 of 5')
    expect(wrapper.find('[data-testid="choice-92"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="choice-96"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="choice-108"]').exists()).toBe(true)
  })

  it('submit is disabled until a choice is picked', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const submit = wrapper.find('[data-testid="question-submit"]')

    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="choice-96"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(submit.attributes('disabled')).toBeUndefined()
  })

  it('emits submit with the picked choice on click', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="choice-96"]').trigger('click')
    await wrapper.find('[data-testid="question-submit"]').trigger('click')

    const emitted = wrapper.emitted('submit')

    expect(emitted).toBeTruthy()
    expect(emitted![0][0]).toBe('96')
    expect(typeof emitted![0][1]).toBe('number')
  })

  it('resets selection when the question prop changes', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="choice-96"]').trigger('click')

    // Swap to a new question
    await wrapper.setProps({
      question: { ...question, questionId: 'q_002', questionIndex: 1, prompt: 'What is 2+2?' },
    })

    const submit = wrapper.find('[data-testid="question-submit"]')

    expect(submit.attributes('disabled')).toBeDefined()
  })

  it('locks interaction when locked prop is true', async () => {
    const wrapper = mount(QuestionCard, {
      props: { question, locked: true },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="choice-96"]').trigger('click')

    // Still not selected because click was a no-op
    await wrapper.find('[data-testid="question-submit"]').trigger('click')

    expect(wrapper.emitted('submit')).toBeFalsy()
  })

  // ─────────────────────────────────────────────────────────────────────
  // FIND-pedagogy-006 — Scaffolding surface: worked example + hint button
  //
  // Cite: Sweller, van Merriënboer & Paas (1998) Cognitive Architecture
  // and Instructional Design, Educational Psychology Review 10(3),
  // 251-296. DOI: 10.1023/A:1022193728205 (worked example effect)
  // Kalyuga et al. (2003) Educational Psychologist 38(1), 23-31.
  // DOI: 10.1207/S15326985EP3801_4 (expertise reversal effect)
  // ─────────────────────────────────────────────────────────────────────

  it('renders a worked example block at ScaffoldingLevel.Full', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'Full',
          workedExample: 'Step 1: 12 × 8 is the same as (10 × 8) + (2 × 8). Step 2: that\u2019s 80 + 16 = 96.',
          hintsAvailable: 3,
          hintsRemaining: 3,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const worked = wrapper.find('[data-testid="question-worked-example"]')

    expect(worked.exists()).toBe(true)
    expect(worked.text()).toContain('Worked example')
    expect(worked.text()).toContain('Step 1')
  })

  it('does not render a worked example when ScaffoldingLevel is not Full', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'HintsOnly',
          workedExample: null,
          hintsAvailable: 1,
          hintsRemaining: 1,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="question-worked-example"]').exists()).toBe(false)
  })

  it('renders the hint button for Full / Partial / HintsOnly levels', () => {
    for (const level of ['Full', 'Partial', 'HintsOnly'] as const) {
      const wrapper = mount(QuestionCard, {
        props: {
          question: {
            ...question,
            scaffoldingLevel: level,
            hintsAvailable: 2,
            hintsRemaining: 2,
          },
        },
        global: { plugins: [makeI18n(), makeVuetify()] },
      })

      expect(
        wrapper.find('[data-testid="question-hint-request"]').exists(),
        `hint button should exist at level ${level}`,
      ).toBe(true)
    }
  })

  it('hides the hint button at ScaffoldingLevel.None (expertise reversal)', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'None',
          hintsAvailable: 0,
          hintsRemaining: 0,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="question-hint-request"]').exists()).toBe(false)
  })

  it('hides the hint button when hintsRemaining reaches zero', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'HintsOnly',
          hintsAvailable: 1,
          hintsRemaining: 0,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="question-hint-request"]').exists()).toBe(false)
  })

  it('emits "hint" when the hint button is clicked', async () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'HintsOnly',
          hintsAvailable: 1,
          hintsRemaining: 1,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="question-hint-request"]').trigger('click')

    expect(wrapper.emitted('hint')).toBeTruthy()
  })

  it('renders the last hint block when lastHint prop is supplied', () => {
    const wrapper = mount(QuestionCard, {
      props: {
        question: {
          ...question,
          scaffoldingLevel: 'HintsOnly',
          hintsAvailable: 1,
          hintsRemaining: 0,
        },
        lastHint: {
          hintLevel: 1,
          hintText: 'Re-read the question carefully.',
          hasMoreHints: false,
          hintsRemaining: 0,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const hint = wrapper.find('[data-testid="question-hint-display"]')

    expect(hint.exists()).toBe(true)
    expect(hint.text()).toContain('Re-read')
  })
})
