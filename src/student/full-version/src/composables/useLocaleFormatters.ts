// ─────────────────────────────────────────────────────────────────────────
// useLocaleFormatters()
//
// FIND-pedagogy-015: Locale-aware date/number formatters that track the
// active vue-i18n locale. Replaces hardcoded 'en-US' Intl calls.
//
// Maps vue-i18n locale codes to BCP 47 tags:
//   en → en-US
//   ar → ar-SA
//   he → he-IL
//
// Per Unicode TR35 (https://unicode.org/reports/tr35/tr35-numbers.html),
// number formatting is one of the most heavily-locale-sensitive UI surfaces.
// Per Sweller 1988 (DOI: 10.1207/s15516709cog1202_4), mismatched locale
// formatting imposes extraneous cognitive load.
// ─────────────────────────────────────────────────────────────────────────

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * Mapping from vue-i18n short locale codes to BCP 47 tags used by Intl.
 * Falls back to 'en-US' for unknown codes.
 */
export const LOCALE_BCP47_MAP: Record<string, string> = {
  en: 'en-US',
  ar: 'ar-SA',
  he: 'he-IL',
}

/**
 * Resolves a vue-i18n locale code to a BCP 47 tag.
 * Pure function — usable outside Vue component context.
 */
export function toBcp47(localeCode: string): string {
  return LOCALE_BCP47_MAP[localeCode] ?? 'en-US'
}

/**
 * Reactive composable for use inside Vue `<script setup>` blocks.
 * Tracks the current i18n locale and provides locale-aware formatting.
 */
export function useLocaleFormatters() {
  const { locale } = useI18n()

  const bcp47 = computed(() => toBcp47(locale.value))

  /**
   * Format a date using Intl.DateTimeFormat with the active locale.
   */
  function formatDate(
    date: Date | string,
    options: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' },
  ): string {
    const d = typeof date === 'string' ? new Date(date) : date
    if (Number.isNaN(d.getTime()))
      return ''

    return new Intl.DateTimeFormat(bcp47.value, options).format(d)
  }

  /**
   * Format a number using Intl.NumberFormat with the active locale.
   * For values > 9999, uses compact notation (e.g. "10K" in en, "١٠ ألف" in ar).
   */
  function formatNumber(n: number, options?: Intl.NumberFormatOptions): string {
    const effectiveOptions: Intl.NumberFormatOptions = options ?? (
      Math.abs(n) > 9999
        ? { notation: 'compact', compactDisplay: 'short' }
        : {}
    )

    return new Intl.NumberFormat(bcp47.value, effectiveOptions).format(n)
  }

  /**
   * Format a currency value using Intl.NumberFormat with the active locale.
   */
  function formatCurrency(n: number, currency = 'USD'): string {
    return new Intl.NumberFormat(bcp47.value, {
      style: 'currency',
      currency,
    }).format(n)
  }

  /**
   * Format a relative time (e.g. "3 days ago") using Intl.RelativeTimeFormat.
   */
  function formatRelativeTime(
    date: Date | string,
    now: Date = new Date(),
  ): string {
    const d = typeof date === 'string' ? new Date(date) : date
    if (Number.isNaN(d.getTime()))
      return ''

    const diffMs = d.getTime() - now.getTime()
    const diffSec = Math.round(diffMs / 1000)
    const diffMin = Math.round(diffSec / 60)
    const diffHour = Math.round(diffMin / 60)
    const diffDay = Math.round(diffHour / 24)

    const rtf = new Intl.RelativeTimeFormat(bcp47.value, { numeric: 'auto' })

    if (Math.abs(diffDay) >= 1)
      return rtf.format(diffDay, 'day')
    if (Math.abs(diffHour) >= 1)
      return rtf.format(diffHour, 'hour')
    if (Math.abs(diffMin) >= 1)
      return rtf.format(diffMin, 'minute')

    return rtf.format(diffSec, 'second')
  }

  return {
    bcp47,
    formatDate,
    formatNumber,
    formatCurrency,
    formatRelativeTime,
  }
}

// ─────────────────────────────────────────────────────────────────────────
// Non-reactive helpers for use outside Vue component context.
// These accept an explicit locale code to keep the module free of
// side-effect-heavy imports (like @layouts) at parse time.
// ─────────────────────────────────────────────────────────────────────────

/**
 * Format a date string using a given locale code.
 * For use in utility functions (e.g. formatters.ts) that resolve the
 * locale themselves via getActiveLocale().
 */
export function formatDateWithLocale(
  value: string,
  formatting: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' },
  localeCode = 'en',
): string {
  if (!value) return value

  const bcp47 = toBcp47(localeCode)

  return new Intl.DateTimeFormat(bcp47, formatting).format(new Date(value))
}

/**
 * Format a number using a given locale code with compact notation
 * for large values (>9999), or locale-aware grouping for smaller values.
 * Direct replacement for the old kFormatter that hardcoded ',' separator.
 */
export function formatNumberWithLocale(num: number, localeCode = 'en'): string {
  const bcp47 = toBcp47(localeCode)

  if (Math.abs(num) > 9999) {
    return new Intl.NumberFormat(bcp47, {
      notation: 'compact',
      compactDisplay: 'short',
      maximumFractionDigits: 1,
    }).format(num)
  }

  return new Intl.NumberFormat(bcp47).format(Math.abs(num))
}
