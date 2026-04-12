import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { isHebrewEnabled, sanitizeLocale, useAvailableLocales } from '@/composables/useAvailableLocales'

// ─────────────────────────────────────────────────────────────────────────
// FIND-ux-014 — `useAvailableLocales()` is the single source of truth for
// which locales the UI exposes. Hebrew must be hidden unless
// VITE_ENABLE_HEBREW === 'true' (case-insensitive, with a few aliases).
//
// Since the composable reads the env via a two-source accessor
// (`import.meta.env` then `process.env` fallback) evaluated on every call,
// we do NOT need vi.resetModules() between tests — stubEnv writes to
// `process.env` and the accessor picks up the change immediately.
// ─────────────────────────────────────────────────────────────────────────

describe('useAvailableLocales', () => {
  beforeEach(() => {
    vi.unstubAllEnvs()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('hides Hebrew when the flag is unset', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', '')
    expect(isHebrewEnabled()).toBe(false)

    const { codes, locales } = useAvailableLocales()

    expect(codes.value).toEqual(['en', 'ar'])
    expect(locales.value.find(l => l.code === 'he')).toBeUndefined()
  })

  it('hides Hebrew when the flag is "false"', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(isHebrewEnabled()).toBe(false)

    const { codes } = useAvailableLocales()

    expect(codes.value).not.toContain('he')
  })

  it('shows Hebrew when the flag is "true"', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    expect(isHebrewEnabled()).toBe(true)

    const { codes } = useAvailableLocales()

    expect(codes.value).toEqual(['en', 'ar', 'he'])
  })

  it('accepts "1", "yes", "TRUE", and whitespace-padded "true" as truthy', () => {
    for (const truthy of ['1', 'yes', 'TRUE', '  true  ']) {
      vi.stubEnv('VITE_ENABLE_HEBREW', truthy)
      expect(isHebrewEnabled(), `flag="${truthy}"`).toBe(true)
    }
  })

  it('treats any other string as false', () => {
    for (const falsy of ['0', 'no', 'off', '', 'maybe']) {
      vi.stubEnv('VITE_ENABLE_HEBREW', falsy)
      expect(isHebrewEnabled(), `flag="${falsy}"`).toBe(false)
    }
  })
})

// ─────────────────────────────────────────────────────────────────────────
// FIND-pedagogy-010 — `sanitizeLocale()` is the runtime gate that prevents
// cookie-injection and localStorage-based Hebrew bypass. It must reject
// 'he' when the build flag is off and pass it through when on.
// ─────────────────────────────────────────────────────────────────────────

describe('sanitizeLocale', () => {
  beforeEach(() => {
    vi.unstubAllEnvs()
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('returns "en" when input is "he" and Hebrew is disabled (cookie bypass)', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(sanitizeLocale('he')).toBe('en')
  })

  it('returns "he" when input is "he" and Hebrew is enabled', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    expect(sanitizeLocale('he')).toBe('he')
  })

  it('passes through "en" regardless of Hebrew flag', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(sanitizeLocale('en')).toBe('en')

    vi.stubEnv('VITE_ENABLE_HEBREW', 'true')
    expect(sanitizeLocale('en')).toBe('en')
  })

  it('passes through "ar" regardless of Hebrew flag', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(sanitizeLocale('ar')).toBe('ar')
  })

  it('falls back to "en" for unknown locale codes', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(sanitizeLocale('fr')).toBe('en')
    expect(sanitizeLocale('')).toBe('en')
    expect(sanitizeLocale('zh')).toBe('en')
  })

  it('uses the custom fallback when provided', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    expect(sanitizeLocale('he', 'ar')).toBe('ar')
  })

  it('logs a warning when Hebrew is blocked', () => {
    vi.stubEnv('VITE_ENABLE_HEBREW', 'false')
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

    sanitizeLocale('he')
    expect(warnSpy).toHaveBeenCalledWith(
      expect.stringContaining('[cena-i18n] Hebrew locale requested but VITE_ENABLE_HEBREW is not enabled'),
    )

    warnSpy.mockRestore()
  })
})
