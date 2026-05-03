import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import { createRouter, createWebHistory } from 'vue-router'
import ConceptTile from '@/components/knowledge/ConceptTile.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
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
        },
      },
    },
  })
}

function makeRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div/>' } }],
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

const baseConcept = {
  conceptId: 'math-algebra',
  name: 'Linear Algebra',
  subject: 'math',
  topic: 'algebra',
  difficulty: 'intermediate' as const,
  status: 'in-progress' as const,
}

describe('ConceptTile', () => {
  it('renders concept name, topic, status chip, difficulty chip', () => {
    const wrapper = mount(ConceptTile, {
      props: { concept: baseConcept },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('Linear Algebra')
    expect(wrapper.text()).toContain('algebra')
    expect(wrapper.text()).toContain('In progress')
    expect(wrapper.text()).toContain('Intermediate')
  })

  it('adds locked modifier class for locked concepts', () => {
    const wrapper = mount(ConceptTile, {
      props: { concept: { ...baseConcept, status: 'locked' } },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    const root = wrapper.find('[data-testid="concept-math-algebra"]')

    expect(root.classes()).toContain('concept-tile--locked')
    expect(root.attributes('data-status')).toBe('locked')
  })

  it('adds mastered modifier class for mastered concepts', () => {
    const wrapper = mount(ConceptTile, {
      props: { concept: { ...baseConcept, status: 'mastered' } },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    const root = wrapper.find('[data-testid="concept-math-algebra"]')

    expect(root.classes()).toContain('concept-tile--mastered')
  })

  it('falls back to subject when topic is null', () => {
    const wrapper = mount(ConceptTile, {
      props: { concept: { ...baseConcept, topic: null } },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('math')
  })
})
