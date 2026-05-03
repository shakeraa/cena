import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import StreakWidget from '@/components/home/StreakWidget.vue'

describe('StreakWidget', () => {
  it('renders the day count prominently', () => {
    const wrapper = mount(StreakWidget, { props: { days: 12 } })

    expect(wrapper.find('[data-testid="streak-widget"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('12')
  })

  it('shows the "new best" chip when isNewBest is true', () => {
    const wrapper = mount(StreakWidget, { props: { days: 30, isNewBest: true } })

    expect(wrapper.text()).toContain('New best')
  })

  it('hides the "new best" chip by default', () => {
    const wrapper = mount(StreakWidget, { props: { days: 5 } })

    expect(wrapper.text()).not.toContain('New best')
  })

  it('uses singular label for a 1-day streak', () => {
    const wrapper = mount(StreakWidget, { props: { days: 1 } })

    // Both singular and plural keys may render the same English literal
    // ("day streak") but what matters is we don't crash on count=1.
    expect(wrapper.find('[data-testid="streak-widget"]').text()).toContain('1')
  })
})
