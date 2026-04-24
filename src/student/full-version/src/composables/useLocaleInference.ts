// =============================================================================
// RDY-068 (F2) — Locale inference for Arabic-first onboarding.
//
// Goal: when a student opens Cena for the first time, pre-select the locale
// that matches their device instead of forcing a flag-picker default of
// English. Dr. Lior's critique (panel review, 2026-04-17): "Arabic students
// must start in Arabic — asking Amir to click a flag to choose Arabic is
// friction that says 'you are the secondary audience.'"
//
// Design rules:
//   - Pure function `inferLocale(options)` — no side effects, fully testable.
//   - Biased toward Arabic-first: any Arabic language tag → 'ar'.
//   - Hebrew-only when device explicitly says so.
//   - English is the fallback, never the assumption for non-English devices.
//   - Respects the Hebrew gate (`availableCodes`) — if Hebrew is disabled by
//     build flag, 'he' devices fall back to English, not Arabic.
//   - No surveillance: does not call any geo-IP service; relies on the
//     browser's locale hints the user already provides.
//
// This composable is the wiring point for `onboardingStore.locale`'s
// initial value when no locale has been persisted yet. Once a user has
// explicitly picked a locale, their persisted choice always wins.
//
// The Levantine Arabic math terminology (س, ص, ع variable names, etc.)
// is handled separately by ArabicMathNormalizer.cs on the backend. This
// file only decides which UI language the student sees on first visit.
// =============================================================================

import type { SupportedLocale } from '@/stores/onboardingStore'

export interface LocaleInferenceOptions {
  /**
   * Override the browser's locale list, primarily for unit tests.
   * In production, leave unset to let the composable read navigator.languages.
   */
  readonly languagesOverride?: readonly string[]

  /**
   * Which locales the current build actually serves. Pass the output of
   * useAvailableLocales().locales.value so the Hebrew build-flag gate
   * is honored: if 'he' is disabled, a he-IL device falls back through
   * the preference chain, never lands on 'he'.
   */
  readonly availableCodes: ReadonlySet<SupportedLocale>
}

/**
 * Pure inference: given a list of BCP-47 language tags (in browser-preferred
 * order) and the set of locales the build serves, return the best default.
 *
 * Preference chain:
 *   1. First Arabic tag → 'ar' (if available)
 *   2. First Hebrew tag → 'he' (if available)
 *   3. First English tag → 'en' (if available)
 *   4. Any available locale, in preference order: 'ar' > 'he' > 'en'
 *
 * Rationale: the Arabic-first bias is intentional. Cena's wedge is the
 * Arab-sector market. A device with mixed languages ("en-US, ar-PS") is
 * most likely an Arabic-native speaker whose browser was installed with
 * an English default — and we should meet them in Arabic, not English.
 */
export function inferLocale(options: LocaleInferenceOptions): SupportedLocale {
  const { languagesOverride, availableCodes } = options

  const languages = languagesOverride ?? readBrowserLanguages()

  // Map each BCP-47 tag to its primary-language subtag.
  const primaryTags = languages
    .map(tag => tag.toLowerCase().split('-')[0])
    .filter((t): t is string => typeof t === 'string' && t.length > 0)

  // Arabic first (any regional variant: ar, ar-PS, ar-IL, ar-EG, etc.).
  if (primaryTags.includes('ar') && availableCodes.has('ar'))
    return 'ar'

  // Then Hebrew, if the build serves it.
  if (primaryTags.includes('he') && availableCodes.has('he'))
    return 'he'
  // Legacy tag for Hebrew was 'iw'; handle both for older browsers.
  if (primaryTags.includes('iw') && availableCodes.has('he'))
    return 'he'

  if (primaryTags.includes('en') && availableCodes.has('en'))
    return 'en'

  // Fallback: prefer Arabic if available, then Hebrew, then English.
  if (availableCodes.has('ar'))
    return 'ar'
  if (availableCodes.has('he'))
    return 'he'

  return 'en'
}

function readBrowserLanguages(): readonly string[] {
  if (typeof navigator === 'undefined')
    return []

  // navigator.languages is the preference-ordered list; fall back to
  // navigator.language for older engines.
  const list = navigator.languages && navigator.languages.length > 0
    ? navigator.languages
    : navigator.language
      ? [navigator.language]
      : []

  return list
}

/**
 * Composable wrapper — reads the current environment + available locales
 * and returns a stable default. Call once at store init time.
 */
export function useLocaleInference(availableCodes: ReadonlySet<SupportedLocale>): SupportedLocale {
  return inferLocale({ availableCodes })
}
