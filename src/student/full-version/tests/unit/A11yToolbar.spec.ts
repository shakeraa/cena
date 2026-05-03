// =============================================================================
// A11yToolbar.spec — covers the 2026-04-21 enrichment:
//   - Language radio renders the available locales in native script and
//     honors the Hebrew gate (FIND-ux-014).
//   - PRR-232 numerals radio reflects the store state and writes back
//     through `setNumeralsPreference`.
//   - Accessibility statement link + contact mailto are present in the
//     footer (IL Reg 5773-2013 §35(b)(4)).
//   - Existing pre-enrichment controls (contrast, motion, dyslexia,
//     reset, text-size slider) still render.
//
// We mount the drawer in `open: true` state via wrapper.vm so we can
// inspect its template without driving the handle click (Vuetify's
// VNavigationDrawer renders lazily on first-open).
// =============================================================================
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import enMessages from '@/plugins/i18n/locales/en.json'
import arMessages from '@/plugins/i18n/locales/ar.json'
import heMessages from '@/plugins/i18n/locales/he.json'
import A11yToolbar from '@/components/common/A11yToolbar.vue'
import { useOnboardingStore } from '@/stores/onboardingStore'

function makeHarness() {
  const vuetify = createVuetify({ components, directives })

  // Full messages so a11y.* keys resolve without fallback noise. The
  // global test setup ships only a subset; we need the actual a11y bundle.
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    fallbackLocale: 'en',
    messages: {
      en: enMessages as Record<string, unknown>,
      ar: arMessages as Record<string, unknown>,
      he: heMessages as Record<string, unknown>,
    },
  })

  return { vuetify, i18n }
}

// VNavigationDrawer requires a VLayout context that jsdom + isolated mount
// cannot provide. For in-drawer markup assertions we stub it with a
// transparent wrapper that always renders its default slot; for open/close
// assertions against the handle we mount the real component without the
// stub. Both paths share the same global plugins.
const drawerStub = {
  name: 'VNavigationDrawer',
  props: ['modelValue', 'location', 'temporary', 'width', 'role'],
  template: '<div data-testid="stub-drawer"><slot /></div>',
}

async function mountToolbarOpen() {
  const { vuetify, i18n } = makeHarness()

  const wrapper = mount(A11yToolbar, {
    global: {
      plugins: [vuetify, i18n],
      stubs: { teleport: true, VNavigationDrawer: drawerStub },
    },
  })

  // Force the drawer open so its slots render in jsdom.
  ;(wrapper.vm as any).open = true
  await flushPromises()

  return wrapper
}

describe('A11yToolbar', () => {
  beforeEach(() => {
    vi.unstubAllEnvs()
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true') // expose HE for full-row assertions
    localStorage.clear()
    document.documentElement.lang = ''
    document.documentElement.dir = ''
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
  })

  it('renders the persistent handle with an accessible label', () => {
    const { vuetify, i18n } = makeHarness()

    const wrapper = mount(A11yToolbar, {
      global: {
        plugins: [vuetify, i18n],
        stubs: { teleport: true, VNavigationDrawer: drawerStub },
      },
    })

    const handle = wrapper.find('[data-testid="a11y-toolbar-handle"]')

    expect(handle.exists()).toBe(true)
    expect(handle.attributes('aria-label')).toBeTruthy()
    expect(handle.attributes('aria-haspopup')).toBe('dialog')
  })

  it('renders the language radio with native-script labels when HE is enabled', async () => {
    const wrapper = await mountToolbarOpen()

    expect(wrapper.find('[data-testid="a11y-language-en"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-language-ar"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-language-he"]').exists()).toBe(true)

    const html = wrapper.html()

    // Native-script labels, never transliterated.
    expect(html).toContain('العربية')
    expect(html).toContain('עברית')
  })

  it('hides Hebrew from the language radio when VITE_ENABLE_HEBREW=false', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    vi.resetModules()

    // Re-import under the new env so useAvailableLocales re-reads the flag.
    const { default: Fresh } = await import('@/components/common/A11yToolbar.vue')
    const { vuetify, i18n } = makeHarness()

    const wrapper = mount(Fresh, {
      global: {
        plugins: [vuetify, i18n],
        stubs: { teleport: true, VNavigationDrawer: drawerStub },
      },
    })

    ;(wrapper.vm as any).open = true
    await flushPromises()

    expect(wrapper.find('[data-testid="a11y-language-en"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-language-ar"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-language-he"]').exists()).toBe(false)
  })

  it('switching language persists to localStorage and flips html dir', async () => {
    const wrapper = await mountToolbarOpen()

    // Default is English → LTR.
    const arRadio = wrapper.find('[data-testid="a11y-language-ar"] input[type="radio"]')

    await arRadio.setValue(true)
    await arRadio.trigger('change')
    await flushPromises()

    // Post-2026-04-21: A11yToolbar writes through useLocaleStore which
    // persists the v1 schema `{code, locked, version}` to localStorage,
    // not a bare string. The locale store's readPersisted() still upcasts
    // legacy bare strings so existing users aren't re-prompted.
    const raw = localStorage.getItem('cena-student-locale')!

    expect(JSON.parse(raw)).toMatchObject({ code: 'ar', locked: true, version: 1 })
    expect(document.documentElement.lang).toBe('ar')
    expect(document.documentElement.dir).toBe('rtl')
  })

  // PRR-232 — numerals preference.
  it('numerals radio reflects store state and writes through setNumeralsPreference', async () => {
    const wrapper = await mountToolbarOpen()
    const onboarding = useOnboardingStore()

    // Default: null → 'auto'.
    const autoRadio = wrapper.find('[data-testid="a11y-numerals-auto"]') as ReturnType<typeof wrapper.find>

    expect((autoRadio.element as HTMLInputElement).checked).toBe(true)
    expect(onboarding.numeralsPreference).toBeNull()

    // Flip to Eastern.
    const easternRadio = wrapper.find('[data-testid="a11y-numerals-eastern"]')

    await easternRadio.trigger('change')
    await flushPromises()

    expect(onboarding.numeralsPreference).toBe('eastern')

    // Flip back to Auto.
    await autoRadio.trigger('change')
    await flushPromises()

    expect(onboarding.numeralsPreference).toBeNull()
  })

  it('renders eastern-arabic sample digits inside an LTR bdi', async () => {
    const wrapper = await mountToolbarOpen()

    const easternRow = wrapper.find('[data-testid="a11y-numerals-eastern"]').element.parentElement

    expect(easternRow).toBeTruthy()

    const bdi = easternRow!.querySelector('bdi[dir="ltr"]')

    expect(bdi).toBeTruthy()
    expect(bdi!.textContent).toContain('٠١٢٣')
  })

  it('renders accessibility statement link + contact mailto in the footer', async () => {
    const wrapper = await mountToolbarOpen()

    const stmt = wrapper.find('[data-testid="a11y-statement-link"]')

    expect(stmt.exists()).toBe(true)
    expect(stmt.attributes('href')).toBe('/accessibility-statement')

    const contact = wrapper.find('[data-testid="a11y-contact-link"]')

    expect(contact.exists()).toBe(true)
    expect(contact.attributes('href')).toBe('mailto:accessibility@cena.app')
  })

  it('keeps the pre-enrichment a11y controls wired', async () => {
    const wrapper = await mountToolbarOpen()

    expect(wrapper.find('[data-testid="a11y-text-size-slider"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-contrast-toggle"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-motion-toggle"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-dyslexia-toggle"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-reset"]').exists()).toBe(true)
  })

  // PRR-A11Y-EXPANDED-CONTROLS: new surfaces.
  it('renders line-height slider, color-blind radio, and underline-links toggle', async () => {
    const wrapper = await mountToolbarOpen()

    expect(wrapper.find('[data-testid="a11y-line-height-slider"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-color-blind-section"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-color-blind-protanopia"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-color-blind-deuteranopia"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-color-blind-tritanopia"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="a11y-underline-links-toggle"]').exists()).toBe(true)
  })

  it('color-blind selection writes through store and flips html[data-a11y-color-blind]', async () => {
    const wrapper = await mountToolbarOpen()

    const proto = wrapper.find('[data-testid="a11y-color-blind-protanopia"]')

    await proto.trigger('change')
    await flushPromises()

    expect(document.documentElement.getAttribute('data-a11y-color-blind')).toBe('protanopia')
  })

  it('underline-links toggle flips html[data-a11y-underline-links]', async () => {
    const wrapper = await mountToolbarOpen()

    expect(document.documentElement.getAttribute('data-a11y-underline-links')).toBe('off')
    await (wrapper.vm as any).onUnderlineToggle()
    await flushPromises()

    expect(document.documentElement.getAttribute('data-a11y-underline-links')).toBe('on')
  })

  // PRR-A11Y-SEMANTICS-SHORTCUT.
  it('handle exposes id="a11y-toolbar-handle" so the skip link can target it', async () => {
    const { vuetify, i18n } = makeHarness()

    const wrapper = mount(A11yToolbar, {
      global: {
        plugins: [vuetify, i18n],
        stubs: { teleport: true, VNavigationDrawer: drawerStub },
      },
    })

    const handle = wrapper.find('[data-testid="a11y-toolbar-handle"]')

    expect(handle.attributes('id')).toBe('a11y-toolbar-handle')
  })

  it('Alt+A global keydown opens the toolbar', async () => {
    const { vuetify, i18n } = makeHarness()

    const wrapper = mount(A11yToolbar, {
      attachTo: document.body,
      global: {
        plugins: [vuetify, i18n],
        stubs: { teleport: true, VNavigationDrawer: drawerStub },
      },
    })

    expect((wrapper.vm as any).open).toBe(false)

    const evt = new KeyboardEvent('keydown', { key: 'a', altKey: true })

    window.dispatchEvent(evt)
    await flushPromises()

    expect((wrapper.vm as any).open).toBe(true)

    wrapper.unmount()
  })
})
