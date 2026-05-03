import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import TimeBreakdownChart from '@/components/progress/TimeBreakdownChart.vue'

function makeI18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    messages: {
      en: {
        progress: {
          time: {
            chartTitle: 'Daily minutes',
            chartSubtitle: 'Last 30 days',
            chartAria: '{total} min, avg {avg}',
            avgPerDay: 'Avg / day',
            today: 'Today',
          },
        },
      },
    },
  })
}

function makeVuetify() {
  return createVuetify({ components, directives })
}

describe('TimeBreakdownChart', () => {
  it('renders one bar per input item', () => {
    const items = Array.from({ length: 5 }).map((_, i) => ({
      date: new Date(Date.now() - i * 86400_000).toISOString(),
      minutes: 20 + i * 5,
    }))

    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    const bars = wrapper.findAll('.time-breakdown-chart__column')

    expect(bars).toHaveLength(5)
  })

  it('computes average minutes per day', () => {
    const items = [
      { date: '2026-04-01T00:00:00Z', minutes: 30 },
      { date: '2026-04-02T00:00:00Z', minutes: 40 },
      { date: '2026-04-03T00:00:00Z', minutes: 50 },
      { date: '2026-04-04T00:00:00Z', minutes: 60 },
    ]

    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.find('[data-testid="time-avg"]').text()).toContain('45')
  })

  it('handles empty items gracefully', () => {
    const wrapper = mount(TimeBreakdownChart, {
      props: { items: [] },
      global: { plugins: [makeI18n(), makeVuetify()] },
    })

    expect(wrapper.findAll('.time-breakdown-chart__column')).toHaveLength(0)
    expect(wrapper.find('[data-testid="time-avg"]').text()).toContain('0')
  })
})
