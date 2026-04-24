import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import { createRouter, createWebHistory } from 'vue-router'
import TutorThreadListItem from '@/components/tutor/TutorThreadListItem.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        tutor: {
          threadList: {
            messageCount: '{count} messages',
            justNow: 'just now',
            minutesAgo: '{count}m ago',
            hoursAgo: '{count}h ago',
            daysAgo: '{count}d ago',
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

describe('TutorThreadListItem', () => {
  it('renders the title and subject chip', () => {
    const wrapper = mount(TutorThreadListItem, {
      props: {
        thread: {
          threadId: 'th-1',
          title: 'Help with quadratics',
          subject: 'math',
          topic: 'algebra',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          messageCount: 4,
          isArchived: false,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.find('[data-testid="tutor-thread-th-1"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Help with quadratics')
    expect(wrapper.text()).toContain('math')
    expect(wrapper.text()).toContain('4 messages')
  })

  it('shows relative "just now" for very recent updates', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-10T12:00:00Z'))

    const wrapper = mount(TutorThreadListItem, {
      props: {
        thread: {
          threadId: 'th-2',
          title: 'Brand new thread',
          subject: null,
          topic: null,
          createdAt: '2026-04-10T12:00:00Z',
          updatedAt: '2026-04-10T12:00:00Z',
          messageCount: 0,
          isArchived: false,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('just now')

    vi.useRealTimers()
  })

  it('shows relative hours for older updates', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-10T12:00:00Z'))

    const wrapper = mount(TutorThreadListItem, {
      props: {
        thread: {
          threadId: 'th-3',
          title: 'A few hours ago',
          subject: null,
          topic: null,
          createdAt: '2026-04-10T09:00:00Z',
          updatedAt: '2026-04-10T09:00:00Z',
          messageCount: 2,
          isArchived: false,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('3h ago')

    vi.useRealTimers()
  })
})
