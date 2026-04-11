import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import { createRouter, createWebHistory } from 'vue-router'
import SessionHistoryItem from '@/components/progress/SessionHistoryItem.vue'

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
        progress: {
          sessions: {
            justNow: 'just now',
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

describe('SessionHistoryItem', () => {
  it('renders subject, duration, accuracy chip, and XP', () => {
    const wrapper = mount(SessionHistoryItem, {
      props: {
        sessionId: 's-1',
        subject: 'math',
        startedAt: new Date(Date.now() - 3 * 3600_000).toISOString(),
        durationSeconds: 12 * 60,
        accuracyPercent: 87,
        xpAwarded: 45,
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('Math')
    expect(wrapper.text()).toContain('87%')
    expect(wrapper.text()).toContain('45 XP')
  })

  it('formats relative time for hours ago', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-11T12:00:00Z'))

    const wrapper = mount(SessionHistoryItem, {
      props: {
        sessionId: 's-2',
        subject: 'physics',
        startedAt: '2026-04-11T09:00:00Z',
        durationSeconds: 300,
        accuracyPercent: 75,
        xpAwarded: 20,
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    expect(wrapper.text()).toContain('3h ago')
    vi.useRealTimers()
  })

  it('uses success color for high accuracy', () => {
    const wrapper = mount(SessionHistoryItem, {
      props: {
        sessionId: 's-3',
        subject: 'math',
        startedAt: new Date().toISOString(),
        durationSeconds: 600,
        accuracyPercent: 95,
        xpAwarded: 50,
      },
      global: { plugins: [makeI18n(), makeVuetify(), makeRouter()] },
    })

    const chip = wrapper.find('.v-chip')

    expect(chip.classes().some(c => c.includes('success'))).toBe(true)
  })
})
