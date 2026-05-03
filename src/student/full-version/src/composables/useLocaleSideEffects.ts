// =============================================================================
// useLocaleSideEffects — the single seam that applies a locale change to the
// runtime (document, vue-i18n, vuetify). Used by the new `useLocaleStore` and
// by any legacy caller that needs to keep side-effects in lockstep.
//
// Problem it solves (prr-a11y-first-run-chooser): before this composable,
// three places wrote locale side-effects independently:
//   - onboarding.vue (wizard "confirm" step)
//   - LanguageSwitcher.vue (top-right menu)
//   - A11yToolbar.vue (language radio)
// All three had subtly-different rules: one forgot to flip `vuetifyLocale`,
// another forgot to persist, a third mis-handled the Hebrew gate. This
// composable centralises the five-step sequence so callers cannot drift.
//
// Non-goals: persistence. The locale store (`useLocaleStore`) persists and
// routes through this composable. Legacy callers that want the old
// localStorage key (`cena-student-locale`) still read from it directly; we
// keep the key name stable on purpose so an upgrade does not force users
// back through the first-run chooser.
// =============================================================================
import type { ComposerTranslation } from 'vue-i18n'
import { useI18n } from 'vue-i18n'
import { useLocale as useVuetifyLocale } from 'vuetify'
import type { LocaleDescriptor } from '@/composables/useAvailableLocales'
import { sanitizeLocale, useAvailableLocales } from '@/composables/useAvailableLocales'

export type SupportedLocaleCode = 'en' | 'ar' | 'he'

export interface LocaleSideEffects {
  /**
   * Apply a locale change to vue-i18n, vuetify, and <html lang/dir>. Does
   * NOT persist to localStorage or touch the locale store — the caller is
   * expected to drive those (separation of concerns: state vs side-effects).
   *
   * Returns the descriptor actually applied (in case the input was gated off
   * by the Hebrew build flag and got normalised to the fallback).
   */
  apply: (code: SupportedLocaleCode) => LocaleDescriptor | null

  /** Strongly-typed i18n translate — exposed so callers don't need a second useI18n(). */
  t: ComposerTranslation
}

/**
 * Vue composable — MUST be called from setup() or another composable. Internally
 * calls `useI18n()` / `useLocale()` which rely on the Vue app context.
 */
export function useLocaleSideEffects(): LocaleSideEffects {
  const { locale: i18nLocale, t } = useI18n()
  const vuetifyLocale = useVuetifyLocale()
  const { locales: availableLocales, hebrewEnabled } = useAvailableLocales()

  function apply(code: SupportedLocaleCode): LocaleDescriptor | null {
    // Defence in depth against a stray caller or injected cookie: funnel
    // everything through `sanitizeLocale` so a disabled Hebrew never reaches
    // the runtime even if a caller forgot.
    const sanitized = sanitizeLocale(code) as SupportedLocaleCode
    if (sanitized === 'he' && !hebrewEnabled)
      return null

    const descriptor = availableLocales.value.find(l => l.code === sanitized)
    if (!descriptor)
      return null

    // 1. vue-i18n — translate() now resolves against the new locale.
    i18nLocale.value = sanitized
    // 2. Vuetify — its internal `isRtl` derives from `current`, so flipping
    //    `current` auto-propagates to `VLocaleProvider`.
    vuetifyLocale.current.value = sanitized
    // 3. <html lang/dir> — the source of truth for CSS :lang() selectors
    //    and the right-to-left cascade.
    if (typeof document !== 'undefined') {
      document.documentElement.lang = sanitized
      document.documentElement.dir = descriptor.dir
    }

    return descriptor
  }

  return { apply, t }
}
