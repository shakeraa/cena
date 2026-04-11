// ─────────────────────────────────────────────────────────────────────────
// useAvailableLocales() — admin
//
// Single source of truth for which locales are offered in the admin-web
// language switcher. Enforces FIND-ux-014: Hebrew is hideable outside
// Israel. Mirrors src/student/full-version/src/composables/useAvailableLocales.ts
// so student and admin stay in lockstep.
//
// User rule (2026-03-27): English is primary, Arabic / Hebrew secondary,
// Hebrew hideable outside Israel. The admin bundle additionally exposes
// French (fr) — kept always visible because admins are frequently
// multi-region staff, not end users in a specific tenant.
//
// Implementation: build-time `VITE_ENABLE_HEBREW` env flag. Defaults to
// OFF so deploys that don't explicitly opt in keep Hebrew hidden.
// ─────────────────────────────────────────────────────────────────────────

import { computed } from 'vue'

export type AdminLocaleCode = 'en' | 'ar' | 'he' | 'fr'

export interface LocaleDescriptor {
  code: AdminLocaleCode
  label: string
  dir: 'ltr' | 'rtl'
}

const ALL_LOCALES: readonly LocaleDescriptor[] = Object.freeze([
  { code: 'en', label: 'English', dir: 'ltr' },
  { code: 'fr', label: 'Français', dir: 'ltr' },
  { code: 'ar', label: 'العربية', dir: 'rtl' },
  { code: 'he', label: 'עברית', dir: 'rtl' },
])

/**
 * Two-source read (see sibling student composable for rationale):
 * Vite's `import.meta.env.VITE_ENABLE_HEBREW` is the production path;
 * `process.env.VITE_ENABLE_HEBREW` is a test-time fallback used by
 * Vitest's stubEnv.
 */
function readHebrewEnvFlag(): unknown {
  // eslint-disable-next-line ts/no-explicit-any
  const metaEnv = (import.meta as any).env as Record<string, unknown> | undefined
  const fromMeta = metaEnv ? metaEnv.VITE_ENABLE_HEBREW : undefined
  if (typeof fromMeta === 'string' && fromMeta.length > 0)
    return fromMeta

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

export function useAvailableLocales() {
  const hebrewEnabled = isHebrewEnabled()

  const locales = computed<readonly LocaleDescriptor[]>(() =>
    ALL_LOCALES.filter(l => l.code !== 'he' || hebrewEnabled),
  )

  const codes = computed<readonly AdminLocaleCode[]>(() =>
    locales.value.map(l => l.code),
  )

  return {
    locales,
    codes,
    hebrewEnabled,
  }
}
