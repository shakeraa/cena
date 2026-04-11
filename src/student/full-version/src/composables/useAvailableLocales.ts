// ─────────────────────────────────────────────────────────────────────────
// useAvailableLocales()
//
// Single source of truth for which locales are offered in the student-web
// language switcher. Enforces FIND-ux-014: Hebrew is hideable outside Israel.
//
// User rule (2026-03-27): English is primary, Arabic and Hebrew are
// secondary, and Hebrew must be hideable outside Israel. Implementation
// here is a build-time env flag that defaults to OFF: any tenant that
// needs Hebrew visibility explicitly sets VITE_ENABLE_HEBREW=true at
// build time.
//
// The locale file `he.json` is NEVER deleted. Only the switcher UI hides
// Hebrew when the flag is off. Users whose persisted locale is 'he' but
// whose current tenant has the flag off fall back gracefully to English
// (see LanguageSwitcher.vue's mount-time guard).
//
// Why build-time, not runtime: the student-web bundle is deployed per
// tenant (or per region) via separate build pipelines, so a single flag
// in .env is simpler and cheaper than a runtime JWT claim check. Runtime
// override remains possible by mutating the returned list in a parent
// component if future tenant settings need to override the build default.
// ─────────────────────────────────────────────────────────────────────────

import { computed } from 'vue'

export interface LocaleDescriptor {
  code: 'en' | 'ar' | 'he'
  label: string
  dir: 'ltr' | 'rtl'
}

// The master list. Order is the display order in the menu.
const ALL_LOCALES: readonly LocaleDescriptor[] = Object.freeze([
  { code: 'en', label: 'English', dir: 'ltr' },
  { code: 'ar', label: 'العربية', dir: 'rtl' },
  { code: 'he', label: 'עברית', dir: 'rtl' },
])

/**
 * Read the Hebrew gate. Defaults to `false` per user rule ("Hebrew hideable
 * outside Israel") — tenants inside Israel flip this flag at build time.
 *
 * Values accepted as TRUE: `'true'`, `'1'`, `'yes'` (case-insensitive).
 * Anything else is treated as FALSE, including the unset/undefined case.
 *
 * Two sources are checked in priority order so the gate works in every
 * runtime mode:
 *   1. `import.meta.env.VITE_ENABLE_HEBREW` — Vite's build-time define.
 *      This is the production path — Vite substitutes a string literal
 *      at build time based on .env files.
 *   2. `process.env.VITE_ENABLE_HEBREW` — fallback for unit tests. Vitest's
 *      `vi.stubEnv()` writes to `process.env` reliably across
 *      `resetModules()` + dynamic import boundaries, whereas it does not
 *      propagate `import.meta.env.X` writes into modules imported after
 *      the stub call (a known Vitest quirk, see
 *      https://github.com/vitest-dev/vitest/issues/4022).
 */
function readHebrewEnvFlag(): unknown {
  const metaEnv = (import.meta as unknown as { env?: Record<string, unknown> }).env
  const fromMeta = metaEnv ? metaEnv.VITE_ENABLE_HEBREW : undefined
  if (typeof fromMeta === 'string' && fromMeta.length > 0)
    return fromMeta

  // Test / Node fallback — vitest's stubEnv writes here.
  if (typeof process !== 'undefined' && process.env)
    return process.env.VITE_ENABLE_HEBREW

  return undefined
}

export function isHebrewEnabled(): boolean {
  const raw = readHebrewEnvFlag()
  if (typeof raw !== 'string')
    return false
  const normalized = raw.trim().toLowerCase()

  return normalized === 'true' || normalized === '1' || normalized === 'yes'
}

/**
 * Returns the filtered list of locales the UI should expose in the
 * language switcher. Pure function of the build-time env — callers can
 * use it in script setup to populate menus, filter `langConfig`, etc.
 */
export function useAvailableLocales() {
  const hebrewEnabled = isHebrewEnabled()

  const locales = computed<readonly LocaleDescriptor[]>(() =>
    ALL_LOCALES.filter(l => l.code !== 'he' || hebrewEnabled),
  )

  const codes = computed<readonly ('en' | 'ar' | 'he')[]>(() =>
    locales.value.map(l => l.code),
  )

  return {
    locales,
    codes,
    hebrewEnabled,
  }
}
