import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import KpiCard from '@/components/common/KpiCard.vue'

describe('KpiCard', () => {
  it('renders label and value', () => {
    const wrapper = mount(KpiCard, {
      props: { label: 'Minutes', value: 42 },
    })

    expect(wrapper.text()).toContain('Minutes')
    expect(wrapper.text()).toContain('42')
  })

  it('shows green trend icon for positive trend', () => {
    const wrapper = mount(KpiCard, {
      props: { label: 'X', value: 1, trend: 12 },
    })

    // The semantic color is carried by the VIcon (not the text) — find an
    // icon with the `text-success` class applied via vuetify's color prop.
    expect(wrapper.find('.v-icon.text-success').exists()).toBe(true)
    expect(wrapper.text()).toContain('+12%')
  })

  it('shows red trend icon for negative trend', () => {
    const wrapper = mount(KpiCard, {
      props: { label: 'X', value: 1, trend: -5 },
    })

    expect(wrapper.find('.v-icon.text-error').exists()).toBe(true)
    expect(wrapper.text()).toContain('-5%')
  })

  it('shows grey trend icon for zero', () => {
    const wrapper = mount(KpiCard, {
      props: { label: 'X', value: 1, trend: 0 },
    })

    expect(wrapper.find('.v-icon.text-grey').exists()).toBe(true)
  })

  it('hides trend section when trend is not provided', () => {
    const wrapper = mount(KpiCard, {
      props: { label: 'X', value: 1 },
    })

    expect(wrapper.find('.kpi-trend').exists()).toBe(false)
  })
})
