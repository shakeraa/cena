// =============================================================================
// FirstRunLanguageChooser.spec — mount conditions + keyboard parity + commit.
// PRR-A11Y-FIRST-RUN-CHOOSER.
// =============================================================================
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import enMessages from '@/plugins/i18n/locales/en.json'
import arMessages from '@/plugins/i18n/locales/ar.json'
import heMessages from '@/plugins/i18n/locales/he.json'

async function loadChooser() {
  vi.resetModules()
  const mod = await import('@/components/common/FirstRunLanguageChooser.vue')

  return mod.default
}

function makeHarness() {
  const vuetify = createVuetify({ components, directives })
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

describe('FirstRunLanguageChooser', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.lang = ''
    document.documentElement.dir = ''
    setActivePinia(createPinia())
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('renders three tiles with native-script headlines when HE is enabled', async () => {
    const Chooser = await loadChooser()
    const { vuetify, i18n } = makeHarness()
    const wrapper = mount(Chooser, { global: { plugins: [vuetify, i18n] } })

    expect(wrapper.find('[data-testid="first-run-tile-en"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="first-run-tile-ar"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="first-run-tile-he"]').exists()).toBe(true)

    const html = wrapper.html()

    expect(html).toContain('العربية')
    expect(html).toContain('עברית')
  })

  it('hides Hebrew tile when VITE_ENABLE_HEBREW=false', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    const Chooser = await loadChooser()
    const { vuetify, i18n } = makeHarness()
    const wrapper = mount(Chooser, { global: { plugins: [vuetify, i18n] } })

    expect(wrapper.find('[data-testid="first-run-tile-en"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="first-run-tile-ar"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="first-run-tile-he"]').exists()).toBe(false)
  })

  it('clicking a tile commits the locale and locks the store', async () => {
    const Chooser = await loadChooser()
    const { vuetify, i18n } = makeHarness()
    const wrapper = mount(Chooser, { global: { plugins: [vuetify, i18n] } })

    const { useLocaleStore } = await import('@/stores/localeStore')
    const store = useLocaleStore()

    expect(store.locked).toBe(false)

    const arTile = wrapper.find('[data-testid="first-run-tile-ar"]')

    await arTile.trigger('click')
    await flushPromises()

    expect(store.code).toBe('ar')
    expect(store.locked).toBe(true)
    expect(document.documentElement.dir).toBe('rtl')
    expect(document.documentElement.lang).toBe('ar')
  })

  it('Esc keypress does NOT close / commit — chooser is blocking', async () => {
    const Chooser = await loadChooser()
    const { vuetify, i18n } = makeHarness()
    const wrapper = mount(Chooser, { global: { plugins: [vuetify, i18n] } })

    const { useLocaleStore } = await import('@/stores/localeStore')
    const store = useLocaleStore()

    const panel = wrapper.find('[data-testid="first-run-chooser"]')

    await panel.trigger('keydown', { key: 'Escape' })
    await flushPromises()

    expect(store.locked).toBe(false)
    expect(wrapper.find('[data-testid="first-run-chooser"]').exists()).toBe(true)
  })

  it('ArrowDown/ArrowRight cycle tile selection and Enter commits', async () => {
    const Chooser = await loadChooser()
    const { vuetify, i18n } = makeHarness()
    const wrapper = mount(Chooser, { global: { plugins: [vuetify, i18n] } })

    const { useLocaleStore } = await import('@/stores/localeStore')
    const store = useLocaleStore()

    const panel = wrapper.find('[data-testid="first-run-chooser"]')

    // Start at index 0 (en), arrow down → ar.
    await panel.trigger('keydown', { key: 'ArrowDown' })
    await flushPromises()
    // Enter to commit.
    await panel.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect(store.code).toBe('ar')
    expect(store.locked).toBe(true)
  })
})
