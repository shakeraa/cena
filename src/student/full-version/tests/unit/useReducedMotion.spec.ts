import { describe, expect, it } from 'vitest'
import { defineComponent, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { getMockMql } from './setup'
import { useReducedMotion } from '@/composables/useReducedMotion'

describe('useReducedMotion', () => {
  it('returns false when matchMedia reports no reduce preference', () => {
    const TestComp = defineComponent({
      setup() {
        const reduced = useReducedMotion()

        return { reduced }
      },
      template: '<div>{{ reduced }}</div>',
    })

    const wrapper = mount(TestComp)

    expect(wrapper.vm.reduced).toBe(false)
  })

  it('updates reactively when matchMedia dispatches a change event', async () => {
    const TestComp = defineComponent({
      setup() {
        const reduced = useReducedMotion()

        return { reduced }
      },
      template: '<div>{{ reduced }}</div>',
    })

    const wrapper = mount(TestComp)

    expect(wrapper.vm.reduced).toBe(false)

    const mql = getMockMql('(prefers-reduced-motion: reduce)')

    mql.dispatchEvent(true)
    await nextTick()
    expect(wrapper.vm.reduced).toBe(true)

    mql.dispatchEvent(false)
    await nextTick()
    expect(wrapper.vm.reduced).toBe(false)
  })

  it('removes the listener on unmount (no leak)', async () => {
    const TestComp = defineComponent({
      setup() {
        const reduced = useReducedMotion()

        return { reduced }
      },
      template: '<div>{{ reduced }}</div>',
    })

    const wrapper = mount(TestComp)
    const mql = getMockMql('(prefers-reduced-motion: reduce)')

    expect(mql._listeners.size).toBe(1)
    wrapper.unmount()
    expect(mql._listeners.size).toBe(0)
  })
})
