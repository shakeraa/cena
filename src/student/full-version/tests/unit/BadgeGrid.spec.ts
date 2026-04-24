import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import BadgeGrid from '@/components/progress/BadgeGrid.vue'
import type { BadgeListResponse } from '@/api/types/common'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        gamification: {
          badges: {
            title: 'Badges',
            counter: '{earned} of {total}',
            earnedHeading: 'Earned',
            lockedHeading: 'Locked',
            tier: {
              bronze: 'Bronze',
              silver: 'Silver',
              gold: 'Gold',
              platinum: 'Platinum',
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

const sample: BadgeListResponse = {
  earned: [
    {
      badgeId: 'first-steps',
      name: 'First Steps',
      description: 'Your first session.',
      iconName: 'tabler-shoe',
      tier: 'bronze',
      earnedAt: '2026-03-28T00:00:00Z',
    },
  ],
  locked: [
    {
      badgeId: 'perfectionist',
      name: 'Perfectionist',
      description: '5 perfect sessions in a row.',
      iconName: 'tabler-star',
      tier: 'platinum',
      earnedAt: null,
    },
    {
      badgeId: 'night-owl',
      name: 'Night Owl',
      description: '10 late sessions.',
      iconName: 'tabler-moon',
      tier: 'bronze',
      earnedAt: null,
    },
  ],
}

describe('BadgeGrid', () => {
  it('renders earned and locked sections with correct counter', () => {
    const wrapper = mount(BadgeGrid, {
      props: { badges: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="badge-counter"]').text()).toBe('1 of 3')
    expect(wrapper.find('[data-testid="badge-earned-first-steps"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="badge-locked-perfectionist"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="badge-locked-night-owl"]').exists()).toBe(true)
  })

  it('renders badge names and tier labels for earned entries', () => {
    const wrapper = mount(BadgeGrid, {
      props: { badges: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const text = wrapper.text()

    expect(text).toContain('First Steps')
    expect(text).toContain('Bronze')
  })

  it('renders locked descriptions', () => {
    const wrapper = mount(BadgeGrid, {
      props: { badges: sample },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('5 perfect sessions in a row.')
    expect(wrapper.text()).toContain('10 late sessions.')
  })

  it('handles empty earned list', () => {
    const wrapper = mount(BadgeGrid, {
      props: {
        badges: {
          earned: [],
          locked: sample.locked,
        },
      },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="badge-counter"]').text()).toBe('0 of 2')
    expect(wrapper.findAll('[data-testid^="badge-earned-"]')).toHaveLength(0)
  })
})
