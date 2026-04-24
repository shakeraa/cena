import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import TutorComposeForm from '@/components/tutor/TutorComposeForm.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        tutor: {
          compose: {
            placeholder: 'Ask your tutor anything…',
            sendAria: 'Send message',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('TutorComposeForm', () => {
  it('disables submit when input is empty', () => {
    const wrapper = mount(TutorComposeForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const submit = wrapper.find('[data-testid="tutor-compose-submit"]')

    expect(submit.attributes('disabled')).toBeDefined()
  })

  it('emits submit with trimmed content', async () => {
    const wrapper = mount(TutorComposeForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const textarea = wrapper.find('[data-testid="tutor-compose-input"] textarea')

    await textarea.setValue('  Hello tutor  ')
    await wrapper.find('[data-testid="tutor-compose-form"]').trigger('submit.prevent')

    expect(wrapper.emitted('submit')).toBeTruthy()
    expect(wrapper.emitted('submit')![0]).toEqual(['Hello tutor'])
  })

  it('clears input after submit', async () => {
    const wrapper = mount(TutorComposeForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const textarea = wrapper.find('[data-testid="tutor-compose-input"] textarea')

    await textarea.setValue('Hello')
    await wrapper.find('[data-testid="tutor-compose-form"]').trigger('submit.prevent')

    expect((textarea.element as HTMLTextAreaElement).value).toBe('')
  })

  it('does not emit submit when loading', async () => {
    const wrapper = mount(TutorComposeForm, {
      props: { loading: true },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const textarea = wrapper.find('[data-testid="tutor-compose-input"] textarea')

    await textarea.setValue('Hello')
    await wrapper.find('[data-testid="tutor-compose-form"]').trigger('submit.prevent')

    expect(wrapper.emitted('submit')).toBeFalsy()
  })

  it('does not emit submit for whitespace-only input', async () => {
    const wrapper = mount(TutorComposeForm, {
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const textarea = wrapper.find('[data-testid="tutor-compose-input"] textarea')

    await textarea.setValue('     ')
    await wrapper.find('[data-testid="tutor-compose-form"]').trigger('submit.prevent')

    expect(wrapper.emitted('submit')).toBeFalsy()
  })
})
