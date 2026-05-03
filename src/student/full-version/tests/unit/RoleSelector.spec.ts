import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import RoleSelector from '@/components/onboarding/RoleSelector.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        onboarding: {
          role: {
            student: 'Student',
            studentDescription: 'Enrolled',
            selfLearner: 'Self-learner',
            selfLearnerDescription: 'On my own',
            testPrep: 'Test prep',
            testPrepDescription: 'Prepping',
            homeschool: 'Homeschool',
            homeschoolDescription: 'At home',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('RoleSelector', () => {
  it('renders all four role tiles', () => {
    const wrapper = mount(RoleSelector, {
      props: { modelValue: null },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="role-student"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="role-self-learner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="role-test-prep"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="role-homeschool"]').exists()).toBe(true)
  })

  it('emits update:modelValue on click', async () => {
    const wrapper = mount(RoleSelector, {
      props: { modelValue: null },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="role-test-prep"]').trigger('click')

    const events = wrapper.emitted('update:modelValue')

    expect(events).toBeTruthy()
    expect(events![0]).toEqual(['test-prep'])
  })

  it('emits on Enter key press', async () => {
    const wrapper = mount(RoleSelector, {
      props: { modelValue: null },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="role-student"]').trigger('keydown.enter')

    const events = wrapper.emitted('update:modelValue')

    expect(events).toBeTruthy()
    expect(events![0]).toEqual(['student'])
  })

  it('sets aria-pressed on the currently selected role', () => {
    const wrapper = mount(RoleSelector, {
      props: { modelValue: 'homeschool' },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const homeschool = wrapper.find('[data-testid="role-homeschool"]')
    const student = wrapper.find('[data-testid="role-student"]')

    expect(homeschool.attributes('aria-pressed')).toBe('true')
    expect(student.attributes('aria-pressed')).toBe('false')
  })
})
