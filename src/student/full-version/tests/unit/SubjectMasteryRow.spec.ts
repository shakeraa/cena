import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import SubjectMasteryRow from '@/components/progress/SubjectMasteryRow.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          setup: {
            subjects: {
              math: 'Math',
              physics: 'Physics',
            },
          },
        },
        mastery: {
          novice: 'Novice',
          learning: 'Learning',
          proficient: 'Proficient',
          mastered: 'Mastered',
        },
        progress: {
          mastery: {
            masteryLabel: 'Mastery',
            questionsAttempted: '{count} questions',
            accuracy: '{percent}% accuracy',
            rowAria: '{subject}: {percent}%',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('SubjectMasteryRow', () => {
  it('renders name, percent, questions, and accuracy', () => {
    const wrapper = mount(SubjectMasteryRow, {
      props: {
        subject: 'math',
        masteryPercent: 72,
        questionsAttempted: 48,
        accuracyPercent: 85,
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="mastery-row-math-name"]').text()).toBe('Math')
    expect(wrapper.find('[data-testid="mastery-row-math-percent"]').text()).toBe('72%')
    expect(wrapper.text()).toContain('48 questions')
    expect(wrapper.text()).toContain('85% accuracy')
  })

  it('categorizes mastery percent into tier labels', () => {
    const tests = [
      { percent: 10, label: 'Novice' },
      { percent: 40, label: 'Learning' },
      { percent: 70, label: 'Proficient' },
      { percent: 90, label: 'Mastered' },
    ]

    for (const { percent, label } of tests) {
      const wrapper = mount(SubjectMasteryRow, {
        props: {
          subject: 'physics',
          masteryPercent: percent,
          questionsAttempted: 10,
          accuracyPercent: 80,
        },
        global: { plugins: [makeI18n(), makeVuetify()] },
      })

      expect(wrapper.text()).toContain(label)
    }
  })
})
