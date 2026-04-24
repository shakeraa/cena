import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import ConceptDetailCard from '@/components/knowledge/ConceptDetailCard.vue'
import type { ConceptDetailDto } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: { setup: { subjects: { math: 'Math' } } },
        knowledgeGraph: {
          status: {
            'locked': 'Locked',
            'available': 'Available',
            'in-progress': 'In progress',
            'mastered': 'Mastered',
          },
          difficulty: {
            beginner: 'Beginner',
            intermediate: 'Intermediate',
            advanced: 'Advanced',
          },
          detail: {
            masteryLabel: 'Your mastery',
            estimatedMinutes: '~{minutes} min',
            questionCount: 'Questions',
            subject: 'Subject',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

function makeConcept(overrides: Partial<ConceptDetailDto> = {}): ConceptDetailDto {
  return {
    conceptId: 'math-algebra',
    name: 'Linear Algebra',
    description: 'Variables and linear equations.',
    subject: 'math',
    topic: 'algebra',
    difficulty: 'intermediate',
    status: 'in-progress',
    currentMastery: 0.55,
    prerequisites: [],
    dependencies: [],
    estimatedMinutes: 60,
    questionCount: 48,
    ...overrides,
  }
}

describe('ConceptDetailCard', () => {
  it('renders name, description, status, difficulty, estimated time', () => {
    const wrapper = mount(ConceptDetailCard, {
      props: { concept: makeConcept() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="concept-name"]').text()).toBe('Linear Algebra')
    expect(wrapper.text()).toContain('Variables and linear equations.')
    expect(wrapper.text()).toContain('In progress')
    expect(wrapper.text()).toContain('Intermediate')
    expect(wrapper.text()).toContain('~60 min')
  })

  it('shows mastery percent when currentMastery is set', () => {
    const wrapper = mount(ConceptDetailCard, {
      props: { concept: makeConcept({ currentMastery: 0.72 }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const mastery = wrapper.find('[data-testid="concept-mastery"]')

    expect(mastery.exists()).toBe(true)
    expect(mastery.text()).toContain('72%')
  })

  it('hides mastery block when currentMastery is null', () => {
    const wrapper = mount(ConceptDetailCard, {
      props: { concept: makeConcept({ currentMastery: null }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="concept-mastery"]').exists()).toBe(false)
  })

  it('renders question count + subject stats', () => {
    const wrapper = mount(ConceptDetailCard, {
      props: { concept: makeConcept({ questionCount: 48 }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="concept-question-count"]').text()).toContain('48')
    expect(wrapper.find('[data-testid="concept-subject"]').text()).toContain('Math')
  })
})
