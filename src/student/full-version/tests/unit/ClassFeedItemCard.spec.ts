import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import ClassFeedItemCard from '@/components/social/ClassFeedItemCard.vue'
import type { ClassFeedItem } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        social: {
          feed: {
            justNow: 'just now',
            minutesAgo: '{count}m ago',
            hoursAgo: '{count}h ago',
            kind: {
              achievement: 'Achievement',
              milestone: 'Milestone',
              question: 'Question',
              announcement: 'Announcement',
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

function makeItem(overrides: Partial<ClassFeedItem> = {}): ClassFeedItem {
  return {
    itemId: 'f1',
    kind: 'achievement',
    authorStudentId: 'u-alex',
    authorDisplayName: 'Alex Chen',
    authorAvatarUrl: null,
    title: 'Earned a badge',
    body: 'Great work!',
    postedAt: new Date(Date.now() - 15 * 60_000).toISOString(),
    reactionCount: 5,
    commentCount: 2,
    ...overrides,
  }
}

describe('ClassFeedItemCard', () => {
  it('renders author, title, body, reaction + comment counts', () => {
    const wrapper = mount(ClassFeedItemCard, {
      props: { item: makeItem() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Alex Chen')
    expect(wrapper.find('[data-testid="feed-item-title"]').text()).toBe('Earned a badge')
    expect(wrapper.text()).toContain('Great work!')
    expect(wrapper.text()).toContain('5')
    expect(wrapper.text()).toContain('2')
  })

  it('shows kind chip label', () => {
    const wrapper = mount(ClassFeedItemCard, {
      props: { item: makeItem({ kind: 'announcement' }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Announcement')
  })

  it('emits react event on heart button click', async () => {
    const wrapper = mount(ClassFeedItemCard, {
      props: { item: makeItem() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="react-f1"]').trigger('click')

    expect(wrapper.emitted('react')).toBeTruthy()
    expect(wrapper.emitted('react')![0]).toEqual(['f1'])
  })

  it('formats relative time for recent posts', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-11T12:00:00Z'))

    const wrapper = mount(ClassFeedItemCard, {
      props: {
        item: makeItem({ postedAt: '2026-04-11T11:30:00Z' }),
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('30m ago')
    vi.useRealTimers()
  })
})
