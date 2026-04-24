// =============================================================================
// localeStore — single writer for the student's locale preference.
//
// Shipped as part of PRR-A11Y-FIRST-RUN-CHOOSER so the first-run full-screen
// chooser has one authoritative place to set the locale + lock-flag, and so
// the A11yToolbar / LanguageSwitcher can read the lock-flag to decide whether
// to re-surface the chooser.
//
// Persistence key: `cena-student-locale` (same key the legacy
// LanguageSwitcher.vue has been writing since 2026-03-27). The legacy writer
// persisted a bare string (e.g. 'ar'); the new writer persists a versioned
// object `{ code, locked, version }`. The loader transparently upcasts any
// legacy string-value it finds so users coming back from a previous release
// do not see the chooser again.
//
// This store deliberately does NOT dispatch side-effects (document.lang,
// vuetify.locale.current, i18n.locale). That responsibility lives in
// `useLocaleSideEffects()` so we have one seam, not two.
// =============================================================================
import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { sanitizeLocale } from '@/composables/useAvailableLocales'

export type SupportedLocaleCode = 'en' | 'ar' | 'he'

interface PersistedLocaleV1 {
  code: SupportedLocaleCode
  locked: boolean
  version: 1
}

export const LOCALE_STORAGE_KEY = 'cena-student-locale'
export const LOCALE_SCHEMA_VERSION = 1 as const

function readPersisted(): PersistedLocaleV1 | null {
  if (typeof localStorage === 'undefined')
    return null
  const raw = localStorage.getItem(LOCALE_STORAGE_KEY)
  if (!raw)
    return null

  // Legacy (pre-2026-04-21) format: a bare locale code like "ar". If the user
  // already picked a locale, treat the existing choice as locked so the
  // full-screen chooser does not re-surface after a version bump.
  if (raw === 'en' || raw === 'ar' || raw === 'he') {
    return {
      code: sanitizeLocale(raw) as SupportedLocaleCode,
      locked: true,
      version: LOCALE_SCHEMA_VERSION,
    }
  }

  // New format.
  try {
    const parsed = JSON.parse(raw)
    if (parsed && typeof parsed === 'object' && 'code' in parsed) {
      return {
        code: sanitizeLocale(parsed.code) as SupportedLocaleCode,
        locked: parsed.locked === true,
        version: LOCALE_SCHEMA_VERSION,
      }
    }
  }
  catch {
    // Corrupt value — treat as first-run.
  }

  return null
}

function writePersisted(state: PersistedLocaleV1 | null) {
  if (typeof localStorage === 'undefined')
    return
  try {
    if (!state) {
      localStorage.removeItem(LOCALE_STORAGE_KEY)

      return
    }
    localStorage.setItem(LOCALE_STORAGE_KEY, JSON.stringify(state))
  }
  catch {
    // Private mode / quota — surfacing the error would block onboarding.
  }
}

export const useLocaleStore = defineStore('locale', () => {
  const persisted = readPersisted()

  const code = ref<SupportedLocaleCode>(persisted?.code ?? 'en')
  const locked = ref<boolean>(persisted?.locked ?? false)

  /**
   * Store the chosen locale AND mark it as locked. Called by the first-run
   * chooser on commit and by the A11yToolbar language radio on user change.
   */
  function setLocale(next: SupportedLocaleCode, options: { lock?: boolean } = {}) {
    const sanitized = sanitizeLocale(next) as SupportedLocaleCode

    code.value = sanitized
    if (options.lock !== false)
      locked.value = true
  }

  /**
   * Test-only / diagnostic: clear the lock so the first-run chooser will
   * re-appear on next reload. Not exposed in the UI — users who want to
   * switch language do so from the toolbar, without re-opening the chooser.
   */
  function resetLock() {
    locked.value = false
  }

  watch([code, locked], () => {
    writePersisted({
      code: code.value,
      locked: locked.value,
      version: LOCALE_SCHEMA_VERSION,
    })
  })

  return {
    code,
    locked,
    setLocale,
    resetLock,
  }
})
