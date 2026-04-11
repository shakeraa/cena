import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

// NOTE: the component is imported dynamically inside each test so we can
// reset modules and re-stub `import.meta.env.VITE_ENABLE_HEBREW` without
// the Vite transform baking the previous value into the module cache.
async function loadSwitcher() {
  const mod = await import('@/components/common/LanguageSwitcher.vue')

  return mod.default
}

// ─────────────────────────────────────────────────────────────────────────
// These tests cover both the original FIND-ux-005 contract (persistence,
// RTL flip, localStorage round-trip) AND the new FIND-ux-014 contract:
// Hebrew is hideable outside Israel via `VITE_ENABLE_HEBREW`. We stub the
// import.meta.env flag per-test so we can exercise both states without
// rebuilding the bundle.
// ─────────────────────────────────────────────────────────────────────────

describe('LanguageSwitcher', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.lang = ''
    document.documentElement.dir = ''

    // Default: Hebrew gate OFF (production default — user rule).
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('mounts and shows the current locale label (English by default)', async () => {
    const Switcher = await loadSwitcher()

    const wrapper = mount(Switcher, {
      global: { stubs: { teleport: true } },
    })

    expect(wrapper.text()).toContain('English')
  })

  it('persists locale change to localStorage and flips html dir', async () => {
    // Enable Hebrew for this test so we can exercise all three codes.
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    vi.resetModules()

    const { default: Fresh } = await import('@/components/common/LanguageSwitcher.vue')

    const wrapper = mount(Fresh, {
      global: { stubs: { teleport: true } },
    })

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

  it('restores locale from localStorage on mount when hebrew is enabled', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    localStorage.setItem('cena-student-locale', 'he')
    vi.resetModules()

    const { default: Fresh } = await import('@/components/common/LanguageSwitcher.vue')

    mount(Fresh, {
      global: { stubs: { teleport: true } },
    })

    await flushPromises()
    expect(document.documentElement.lang).toBe('he')
    expect(document.documentElement.dir).toBe('rtl')
  })

  // FIND-ux-014: gate contract.
  // Check visibility via the `hebrewEnabled` expose rather than relying
  // on the collapsed VMenu's inner text — in jsdom the menu contents are
  // not rendered until the activator is clicked, so wrapper.text() only
  // returns the button label. We check the exposed composable state and
  // validate the locales list directly.
  it('hides Hebrew from the available locale list when VITE_ENABLE_HEBREW is unset', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    vi.resetModules()

    const Switcher = await loadSwitcher()

    const wrapper = mount(Switcher, {
      global: { stubs: { teleport: true } },
    })

    await flushPromises()

    // exposed from defineExpose
    expect((wrapper.vm as any).hebrewEnabled).toBe(false)

    // Also verify via the composable directly (single source of truth)
    const { useAvailableLocales } = await import('@/composables/useAvailableLocales')
    const { codes } = useAvailableLocales()

    expect(codes.value).toEqual(['en', 'ar'])
    expect(codes.value).not.toContain('he')
  })

  it('shows Hebrew in the available locale list when VITE_ENABLE_HEBREW=true', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    vi.resetModules()

    const Switcher = await loadSwitcher()

    const wrapper = mount(Switcher, {
      global: { stubs: { teleport: true } },
    })

    await flushPromises()

    expect((wrapper.vm as any).hebrewEnabled).toBe(true)

    const { useAvailableLocales } = await import('@/composables/useAvailableLocales')
    const { codes } = useAvailableLocales()

    expect(codes.value).toEqual(['en', 'ar', 'he'])
  })

  it('gracefully falls back to English if persisted locale is Hebrew but the gate is off', async () => {
    localStorage.setItem('cena-student-locale', 'he')
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    vi.resetModules()

    const { default: Fresh } = await import('@/components/common/LanguageSwitcher.vue')

    mount(Fresh, {
      global: { stubs: { teleport: true } },
    })
    await flushPromises()

    // The mount-time guard flipped the persisted locale to en rather than
    // crashing on a disabled locale.
    expect(localStorage.getItem('cena-student-locale')).toBe('en')
    expect(document.documentElement.lang).toBe('en')
    expect(document.documentElement.dir).toBe('ltr')
  })
})
