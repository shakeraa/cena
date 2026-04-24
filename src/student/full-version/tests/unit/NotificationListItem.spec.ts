import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import NotificationListItem from '@/components/notifications/NotificationListItem.vue'
import type { NotificationItem } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        notifications: {
          justNow: 'just now',
          minutesAgo: '{count}m ago',
          hoursAgo: '{count}h ago',
          daysAgo: '{count}d ago',
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

function make(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    notificationId: 'n-1',
    kind: 'badge',
    priority: 'normal',
    title: 'Quiz Master earned',
    body: 'Tap to see your new badge',
    iconName: 'tabler-brain',
    deepLinkUrl: '/progress',
    read: false,
    createdAt: new Date(Date.now() - 20 * 60_000).toISOString(),
    ...overrides,
  }
}

describe('NotificationListItem', () => {
  it('renders title, body, and unread class when not read', () => {
    const wrapper = mount(NotificationListItem, {
      props: { notification: make() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="notification-n-1"]')

    expect(root.attributes('data-read')).toBe('false')
    expect(root.classes()).toContain('notification-item--unread')
    expect(wrapper.text()).toContain('Quiz Master earned')
    expect(wrapper.text()).toContain('Tap to see your new badge')
  })

  it('does not add unread class when read', () => {
    const wrapper = mount(NotificationListItem, {
      props: { notification: make({ read: true }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const root = wrapper.find('[data-testid="notification-n-1"]')

    expect(root.attributes('data-read')).toBe('true')
    expect(root.classes()).not.toContain('notification-item--unread')
  })

  it('emits markRead on click only when unread', async () => {
    const wrapper = mount(NotificationListItem, {
      props: { notification: make() },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="notification-n-1"]').trigger('click')
    expect(wrapper.emitted('markRead')).toBeTruthy()
    expect(wrapper.emitted('markRead')![0]).toEqual(['n-1'])
  })

  it('does not emit markRead when already read', async () => {
    const wrapper = mount(NotificationListItem, {
      props: { notification: make({ read: true }) },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    await wrapper.find('[data-testid="notification-n-1"]').trigger('click')
    expect(wrapper.emitted('markRead')).toBeFalsy()
  })
})
