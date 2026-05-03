// =============================================================================
// localeStore.spec — covers the first-run upcasting path and the lock-flag
// behaviour for PRR-A11Y-FIRST-RUN-CHOOSER.
// =============================================================================
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

async function freshStore() {
  // Reset the module cache so the store's initial readPersisted() picks up
  // the latest localStorage state for each test.
  vi.resetModules()
  const mod = await import('@/stores/localeStore')

  return mod
}

describe('localeStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('reports locked=false and code=en on a completely fresh load', async () => {
    const { useLocaleStore } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    expect(store.locked).toBe(false)
    expect(store.code).toBe('en')
  })

  it('setLocale with lock writes the v1 schema and flips locked=true', async () => {
    const { useLocaleStore, LOCALE_STORAGE_KEY } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    store.setLocale('ar')
    await Promise.resolve()
    await Promise.resolve()

    expect(store.code).toBe('ar')
    expect(store.locked).toBe(true)
    const raw = localStorage.getItem(LOCALE_STORAGE_KEY)!
    const parsed = JSON.parse(raw)

    expect(parsed).toEqual({ code: 'ar', locked: true, version: 1 })
  })

  it('upcasts a legacy bare-string ("ar") into locked=true on next load', async () => {
    localStorage.setItem('cena-student-locale', 'ar')
    const { useLocaleStore } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    expect(store.code).toBe('ar')
    // Legacy users already picked once — don't re-prompt them.
    expect(store.locked).toBe(true)
  })

  it('resetLock clears the lock so the chooser will re-surface', async () => {
    const { useLocaleStore } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    store.setLocale('he')
    expect(store.locked).toBe(true)
    store.resetLock()
    expect(store.locked).toBe(false)
  })

  it('sanitises a disabled-Hebrew legacy value back to English on load', async () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    localStorage.setItem('cena-student-locale', 'he')
    const { useLocaleStore } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    expect(store.code).toBe('en')
  })

  it('ignores corrupt JSON and treats the user as first-run', async () => {
    localStorage.setItem('cena-student-locale', '{not-valid}')
    const { useLocaleStore } = await freshStore()
    setActivePinia(createPinia())
    const store = useLocaleStore()

    expect(store.locked).toBe(false)
    expect(store.code).toBe('en')
  })
})
