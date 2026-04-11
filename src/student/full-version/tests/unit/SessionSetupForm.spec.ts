import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import SessionSetupForm from '@/components/session/SessionSetupForm.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        session: {
          setup: {
            subjectsLabel: 'Subjects',
            durationLabel: 'Duration',
            durationMinutes: '{minutes} min',
            modeLabel: 'Mode',
            startCta: 'Start',
            subjects: {
              math: 'Math',
              physics: 'Physics',
              chemistry: 'Chemistry',
              biology: 'Biology',
              english: 'English',
              history: 'History',
            },
            modes: {
              practice: 'Practice',
              challenge: 'Challenge',
              review: 'Review',
              diagnostic: 'Diagnostic',
            },
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('SessionSetupForm', () => {
  it('renders all 6 subjects, 6 durations, 4 modes', () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="setup-subject-math"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-subject-history"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-duration-5"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-duration-60"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-mode-practice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="setup-mode-diagnostic"]').exists()).toBe(true)
  })

  it('defaults to math + 15 min + practice and emits that on submit', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const emitted = wrapper.emitted('submit')

    expect(emitted).toBeTruthy()
    expect(emitted![0][0]).toEqual({
      subjects: ['math'],
      durationMinutes: 15,
      mode: 'practice',
    })
  })

  it('toggles subject chips add/remove correctly', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="setup-subject-physics"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const first = wrapper.emitted('submit')![0][0] as any

    expect(first.subjects).toContain('math')
    expect(first.subjects).toContain('physics')

    // Toggle math off
    await wrapper.find('[data-testid="setup-subject-math"]').trigger('click')
    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')

    const second = wrapper.emitted('submit')![1][0] as any

    expect(second.subjects).not.toContain('math')
    expect(second.subjects).toContain('physics')
  })

  it('disables submit when no subjects are selected', async () => {
    const wrapper = mount(SessionSetupForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    // Remove the default math selection
    await wrapper.find('[data-testid="setup-subject-math"]').trigger('click')

    const submit = wrapper.find('[data-testid="setup-start"]')

    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="session-setup-form"]').trigger('submit.prevent')
    expect(wrapper.emitted('submit')).toBeFalsy()
  })
})
