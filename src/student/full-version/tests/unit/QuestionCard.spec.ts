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
})
