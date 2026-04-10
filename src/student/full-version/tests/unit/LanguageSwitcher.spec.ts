import { beforeEach, describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import LanguageSwitcher from '@/components/common/LanguageSwitcher.vue'

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.lang = ''
    document.documentElement.dir = ''
  })

  it('mounts and shows the current locale label (English by default)', () => {
    const wrapper = mount(LanguageSwitcher, {
      global: { stubs: { teleport: true } },
    })

    expect(wrapper.text()).toContain('English')
  })

  it('persists locale change to localStorage and flips html dir', async () => {
    const wrapper = mount(LanguageSwitcher, {
      global: { stubs: { teleport: true } },
    })

    // Trigger locale change via exposed setter — equivalent to clicking the
    // menu item, without pulling in Vuetify overlay teleport.
    ;(wrapper.vm as any).selected = 'ar'
    await flushPromises()

    expect(localStorage.getItem('cena-student-locale')).toBe('ar')
    expect(document.documentElement.lang).toBe('ar')
    expect(document.documentElement.dir).toBe('rtl')

    ;(wrapper.vm as any).selected = 'he'
    await flushPromises()

    expect(localStorage.getItem('cena-student-locale')).toBe('he')
    expect(document.documentElement.dir).toBe('rtl')

    ;(wrapper.vm as any).selected = 'en'
    await flushPromises()

    expect(document.documentElement.dir).toBe('ltr')
  })

  it('restores locale from localStorage on mount', async () => {
    localStorage.setItem('cena-student-locale', 'he')

    mount(LanguageSwitcher, {
      global: { stubs: { teleport: true } },
    })

    await flushPromises()
    expect(document.documentElement.lang).toBe('he')
    expect(document.documentElement.dir).toBe('rtl')
  })
})
