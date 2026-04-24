import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import TutorMessageBubble from '@/components/tutor/TutorMessageBubble.vue'

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('TutorMessageBubble', () => {
  it('renders user message with user modifier class', () => {
    const wrapper = mount(TutorMessageBubble, {
      props: {
        message: {
          messageId: 'm1',
          role: 'user',
          content: 'Hello',
          createdAt: '2026-04-10T00:00:00Z',
          model: null,
        },
      },
      global: { plugins: [makeVuetify()] },
    })

    expect(wrapper.classes()).toContain('tutor-message--user')
    expect(wrapper.attributes('data-role')).toBe('user')
    expect(wrapper.text()).toBe('Hello')
  })

  it('renders assistant message with assistant modifier class', () => {
    const wrapper = mount(TutorMessageBubble, {
      props: {
        message: {
          messageId: 'm2',
          role: 'assistant',
          content: 'Hi there!',
          createdAt: '2026-04-10T00:00:00Z',
          model: 'stub-llm-v1',
        },
      },
      global: { plugins: [makeVuetify()] },
    })

    expect(wrapper.classes()).toContain('tutor-message--assistant')
    expect(wrapper.attributes('data-role')).toBe('assistant')
    expect(wrapper.text()).toBe('Hi there!')
  })
})
