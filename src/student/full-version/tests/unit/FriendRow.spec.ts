import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import FriendRow from '@/components/social/FriendRow.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        social: {
          friends: {
            level: 'Level {level}',
            streak: '{days}d streak',
            online: 'Online',
            offline: 'Offline',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('FriendRow', () => {
  it('renders name, level, streak, online state', () => {
    const wrapper = mount(FriendRow, {
      props: {
        friend: {
          studentId: 'u-alex',
          displayName: 'Alex Chen',
          avatarUrl: null,
          level: 10,
          streakDays: 15,
          isOnline: true,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Alex Chen')
    expect(wrapper.text()).toContain('Level 10')
    expect(wrapper.text()).toContain('15d streak')
    expect(wrapper.text()).toContain('Online')
  })

  it('renders offline state when isOnline is false', () => {
    const wrapper = mount(FriendRow, {
      props: {
        friend: {
          studentId: 'u-sam',
          displayName: 'Sam Park',
          avatarUrl: null,
          level: 8,
          streakDays: 4,
          isOnline: false,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Offline')
    expect(wrapper.text()).not.toContain('Online ·')
  })
})
