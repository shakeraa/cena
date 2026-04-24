import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import FlowAmbientBackground from '@/components/common/FlowAmbientBackground.vue'
import { studentLight } from '@/plugins/vuetify/theme'

describe('FlowAmbientBackground', () => {
  it('sets the inFlow color when flowState=inFlow', () => {
    const wrapper = mount(FlowAmbientBackground, {
      props: { flowState: 'inFlow' },
    })

    const el = wrapper.find('.flow-ambient-background').element as HTMLElement

    expect(el.style.backgroundColor).not.toBe('')
    expect(el.getAttribute('data-flow-state')).toBe('inFlow')
    expect(el.getAttribute('data-transparent')).toBe('false')
    expect(studentLight.flow.inFlow).toBe('#FFB300')
  })

  it('renders fatigued as transparent', () => {
    const wrapper = mount(FlowAmbientBackground, {
      props: { flowState: 'fatigued' },
    })

    const el = wrapper.find('.flow-ambient-background').element as HTMLElement

    expect(el.getAttribute('data-transparent')).toBe('true')
    expect(el.style.backgroundColor).toBe('transparent')
  })

  it('reacts to prop changes between flow states', async () => {
    const wrapper = mount(FlowAmbientBackground, {
      props: { flowState: 'warming' },
    })

    expect(wrapper.find('.flow-ambient-background').attributes('data-flow-state')).toBe('warming')
    await wrapper.setProps({ flowState: 'disrupted' })
    expect(wrapper.find('.flow-ambient-background').attributes('data-flow-state')).toBe('disrupted')
  })

  it('is aria-hidden (pure decoration)', () => {
    const wrapper = mount(FlowAmbientBackground, {
      props: { flowState: 'warming' },
    })

    expect(wrapper.find('.flow-ambient-background').attributes('aria-hidden')).toBe('true')
  })
})
