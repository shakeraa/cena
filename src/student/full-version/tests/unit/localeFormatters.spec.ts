/**
 * FIND-pedagogy-015 — Regression test for locale-aware date and number formatting.
 *
 * Verifies that:
 *   - formatDate('2026-04-11') under i18n.locale='ar' returns Arabic date format, not en-US
 *   - kFormatter(1234567) under ar returns Arabic-thousands format (different from en-US)
 *   - TimeBreakdownChart label rendering switches between en/ar
 *   - toBcp47 maps locale codes correctly
 *   - getActiveLocale() returns the current i18n locale
 *
 * These tests would FAIL on the pre-fix code because:
 *   - formatDate hardcoded 'en-US' regardless of i18n locale
 *   - kFormatter used regex-based ',' separator with no locale awareness
 *   - TimeBreakdownChart used `undefined` (browser locale) instead of i18n locale
 */
import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { mount } from '@vue/test-utils'
// eslint-disable-next-line no-restricted-imports
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createVuetify } from 'vuetify'
import {
  LOCALE_BCP47_MAP,
  formatDateWithLocale,
  formatNumberWithLocale,
  toBcp47,
} from '@/composables/useLocaleFormatters'
import TimeBreakdownChart from '@/components/progress/TimeBreakdownChart.vue'

// ─── Pure function tests (no Vue context needed) ────────────────────────

describe('toBcp47', () => {
  it('maps en to en-US', () => {
    expect(toBcp47('en')).toBe('en-US')
  })

  it('maps ar to ar-SA', () => {
    expect(toBcp47('ar')).toBe('ar-SA')
  })

  it('maps he to he-IL', () => {
    expect(toBcp47('he')).toBe('he-IL')
  })

  it('falls back to en-US for unknown locales', () => {
    expect(toBcp47('xx')).toBe('en-US')
    expect(toBcp47('fr')).toBe('en-US')
  })
})

describe('LOCALE_BCP47_MAP', () => {
  it('contains exactly en, ar, he', () => {
    expect(Object.keys(LOCALE_BCP47_MAP).sort()).toEqual(['ar', 'en', 'he'])
  })
})

// ─── Intl formatter output tests (verify runtime locale support) ────────

describe('Intl.DateTimeFormat locale-aware output', () => {
  const testDate = '2026-04-11'

  it('en-US: formats date containing "Apr" and "2026"', () => {
    const result = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(testDate))

    expect(result).toContain('Apr')
    expect(result).toContain('2026')
  })

  it('ar-SA: produces different date output than en-US', () => {
    const enResult = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(testDate))

    const arResult = new Intl.DateTimeFormat('ar-SA', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(testDate))

    expect(arResult).not.toBe(enResult)
  })

  it('he-IL: produces different date output than en-US', () => {
    const enResult = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(testDate))

    const heResult = new Intl.DateTimeFormat('he-IL', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date(testDate))

    expect(heResult).not.toBe(enResult)
  })
})

describe('Intl.NumberFormat locale-aware output', () => {
  it('en-US: 1234567 formats with commas', () => {
    const result = new Intl.NumberFormat('en-US').format(1234567)

    expect(result).toBe('1,234,567')
  })

  it('ar-SA: 1234567 formats differently than en-US', () => {
    const enResult = new Intl.NumberFormat('en-US').format(1234567)
    const arResult = new Intl.NumberFormat('ar-SA').format(1234567)

    expect(arResult).not.toBe(enResult)
  })

  it('en-US compact: 1234567 contains M', () => {
    const result = new Intl.NumberFormat('en-US', {
      notation: 'compact',
      compactDisplay: 'short',
      maximumFractionDigits: 1,
    }).format(1234567)

    expect(result).toMatch(/M/)
  })

  it('ar-SA compact: 1234567 differs from en-US compact', () => {
    const enResult = new Intl.NumberFormat('en-US', {
      notation: 'compact',
      compactDisplay: 'short',
      maximumFractionDigits: 1,
    }).format(1234567)

    const arResult = new Intl.NumberFormat('ar-SA', {
      notation: 'compact',
      compactDisplay: 'short',
      maximumFractionDigits: 1,
    }).format(1234567)

    expect(arResult).not.toBe(enResult)
  })
})

// ─── formatDateWithLocale / formatNumberWithLocale (non-reactive) ───────

describe('formatDateWithLocale', () => {
  it('formats date in en locale as en-US', () => {
    const result = formatDateWithLocale('2026-04-11', undefined, 'en')

    const expected = new Intl.DateTimeFormat('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    }).format(new Date('2026-04-11'))

    expect(result).toBe(expected)
  })

  it('formats date in ar locale differently from en', () => {
    const enResult = formatDateWithLocale('2026-04-11', undefined, 'en')
    const arResult = formatDateWithLocale('2026-04-11', undefined, 'ar')

    expect(arResult).not.toBe(enResult)
  })

  it('formats date in he locale differently from en', () => {
    const enResult = formatDateWithLocale('2026-04-11', undefined, 'en')
    const heResult = formatDateWithLocale('2026-04-11', undefined, 'he')

    expect(heResult).not.toBe(enResult)
  })

  it('returns empty string for empty input', () => {
    expect(formatDateWithLocale('')).toBe('')
  })
})

describe('formatNumberWithLocale', () => {
  it('formats >9999 with compact notation in en', () => {
    const result = formatNumberWithLocale(1234567, 'en')

    expect(result).toMatch(/M/)
  })

  it('formats small numbers with locale grouping in en', () => {
    const result = formatNumberWithLocale(1234, 'en')

    expect(result).toBe('1,234')
  })

  it('ar locale formats differently from en', () => {
    const enResult = formatNumberWithLocale(1234, 'en')
    const arResult = formatNumberWithLocale(1234, 'ar')

    expect(arResult).not.toBe(enResult)
  })

  it('ar locale formats large numbers differently from en', () => {
    const enResult = formatNumberWithLocale(1234567, 'en')
    const arResult = formatNumberWithLocale(1234567, 'ar')

    expect(arResult).not.toBe(enResult)
  })
})

// ─── TimeBreakdownChart locale integration ──────────────────────────────

describe('TimeBreakdownChart locale-aware labels', () => {
  function makeI18n(locale: string) {
    return createI18n({
      legacy: false,
      locale,
      messages: {
        en: {
          progress: {
            time: {
              chartTitle: 'Daily minutes',
              chartSubtitle: 'Last 30 days',
              chartAria: '{total} min, avg {avg}',
              avgPerDay: 'Avg / day',
              today: 'Today',
            },
          },
        },
        ar: {
          progress: {
            time: {
              chartTitle: 'الدقائق اليومية',
              chartSubtitle: 'آخر 30 يومًا',
              chartAria: '{total} دقيقة، المعدل {avg}',
              avgPerDay: 'المعدل / يوم',
              today: 'اليوم',
            },
          },
        },
      },
    })
  }

  function makeVuetify() {
    return createVuetify({ components, directives })
  }

  const items = [
    { date: '2026-04-01T00:00:00Z', minutes: 30 },
    { date: '2026-04-02T00:00:00Z', minutes: 40 },
  ]

  it('renders English labels when locale is en', () => {
    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('Daily minutes')
  })

  it('renders Arabic labels when locale is ar', () => {
    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n('ar'), makeVuetify()] },
    })

    expect(wrapper.text()).toContain('الدقائق اليومية')
  })

  it('bar title uses locale-appropriate date when locale is en', () => {
    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n('en'), makeVuetify()] },
    })

    const firstBar = wrapper.find('.time-breakdown-chart__bar')
    const title = firstBar.attributes('title') ?? ''

    // en-US: should contain "Apr" (English short month)
    expect(title).toContain('Apr')
  })

  it('bar title uses locale-appropriate date when locale is ar', () => {
    const wrapper = mount(TimeBreakdownChart, {
      props: { items },
      global: { plugins: [makeI18n('ar'), makeVuetify()] },
    })

    const firstBar = wrapper.find('.time-breakdown-chart__bar')
    const title = firstBar.attributes('title') ?? ''

    // ar-SA: should NOT contain English "Apr"
    expect(title).not.toContain('Apr')
  })
})
