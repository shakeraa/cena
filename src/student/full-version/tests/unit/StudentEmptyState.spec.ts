import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import StudentEmptyState from '@/components/common/StudentEmptyState.vue'

describe('StudentEmptyState', () => {
  it('renders title and subtitle', () => {
    const wrapper = mount(StudentEmptyState, {
      props: {
        title: 'No sessions yet',
        subtitle: 'Start your first learning session.',
      },
    })

    expect(wrapper.text()).toContain('No sessions yet')
    expect(wrapper.text()).toContain('Start your first learning session.')
  })

  it('renders actions slot content', () => {
    const wrapper = mount(StudentEmptyState, {
      props: { title: 'Empty' },
      slots: {
        actions: '<button data-test="cta">Go</button>',
      },
    })

    expect(wrapper.find('[data-test="cta"]').exists()).toBe(true)
  })

  it('has an accessible live-region role', () => {
    const wrapper = mount(StudentEmptyState, { props: { title: 'Empty' } })
    const card = wrapper.find('[role="status"]')

    expect(card.exists()).toBe(true)
    expect(card.attributes('aria-live')).toBe('polite')
  })
})
