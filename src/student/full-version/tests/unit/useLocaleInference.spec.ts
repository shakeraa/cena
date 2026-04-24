// =============================================================================
// RDY-068 (F2) — inferLocale unit tests.
//
// Validates the Arabic-first, Hebrew-second, English-fallback preference
// chain and the Hebrew build-flag gate. No browser mocking needed because
// the tests inject `languagesOverride` directly into the pure function.
// =============================================================================

import { describe, expect, it } from 'vitest'
import { inferLocale } from '@/composables/useLocaleInference'
import type { SupportedLocale } from '@/stores/onboardingStore'

const ALL: ReadonlySet<SupportedLocale> = new Set(['en', 'ar', 'he'])
const NO_HEBREW: ReadonlySet<SupportedLocale> = new Set(['en', 'ar'])
const ENGLISH_ONLY: ReadonlySet<SupportedLocale> = new Set(['en'])

describe('inferLocale — Arabic-first preference chain', () => {
  it('picks Arabic for any Arabic BCP-47 tag (generic)', () => {
    expect(inferLocale({ languagesOverride: ['ar'], availableCodes: ALL })).toBe('ar')
  })

  it('picks Arabic for Palestine locale (ar-PS)', () => {
    expect(inferLocale({ languagesOverride: ['ar-PS'], availableCodes: ALL })).toBe('ar')
  })

  it('picks Arabic for Israeli Arabic locale (ar-IL)', () => {
    expect(inferLocale({ languagesOverride: ['ar-IL'], availableCodes: ALL })).toBe('ar')
  })

  it('picks Arabic for Levantine regional variants (LB/JO/SY)', () => {
    for (const tag of ['ar-LB', 'ar-JO', 'ar-SY']) {
      expect(inferLocale({ languagesOverride: [tag], availableCodes: ALL })).toBe('ar')
    }
  })

  it('Arabic-first bias: ar in a mixed list still wins over English', () => {
    // Common case: browser shipped English but user also speaks Arabic.
    // Cena's wedge is the Arab-sector — default to Arabic.
    expect(inferLocale({ languagesOverride: ['en-US', 'ar-PS'], availableCodes: ALL })).toBe('ar')
  })

  it('Arabic-first bias: ar in a mixed list still wins over Hebrew', () => {
    expect(inferLocale({ languagesOverride: ['he-IL', 'ar-IL'], availableCodes: ALL })).toBe('ar')
  })
})

describe('inferLocale — Hebrew handling', () => {
  it('picks Hebrew for he-IL', () => {
    expect(inferLocale({ languagesOverride: ['he-IL'], availableCodes: ALL })).toBe('he')
  })

  it('picks Hebrew for legacy iw tag (older browsers)', () => {
    expect(inferLocale({ languagesOverride: ['iw-IL'], availableCodes: ALL })).toBe('he')
  })

  it('falls through Hebrew when Hebrew build flag is off', () => {
    // FIND-ux-014 rule: Hebrew is hideable outside Israel. A he-IL device
    // on a non-Hebrew build must land on the next preferred locale, not 'he'.
    expect(inferLocale({ languagesOverride: ['he-IL'], availableCodes: NO_HEBREW })).toBe('ar')
  })

  it('he-IL on an English-only build falls to English', () => {
    expect(inferLocale({ languagesOverride: ['he-IL'], availableCodes: ENGLISH_ONLY })).toBe('en')
  })
})

describe('inferLocale — English and unknown locales', () => {
  it('picks English for en-US', () => {
    expect(inferLocale({ languagesOverride: ['en-US'], availableCodes: ALL })).toBe('en')
  })

  it('picks English for en-GB', () => {
    expect(inferLocale({ languagesOverride: ['en-GB'], availableCodes: ALL })).toBe('en')
  })

  it('falls through to Arabic for an unknown locale when ar is available', () => {
    // Design rule: "English is never the assumption for non-English devices."
    // A device broadcasting only fr-FR gets Arabic (Cena's market wedge),
    // not English.
    expect(inferLocale({ languagesOverride: ['fr-FR'], availableCodes: ALL })).toBe('ar')
  })

  it('falls through to Hebrew when ar unavailable and he is', () => {
    expect(inferLocale({ languagesOverride: ['fr-FR'], availableCodes: new Set(['en', 'he']) })).toBe('he')
  })

  it('final fallback to English when only en is served', () => {
    expect(inferLocale({ languagesOverride: ['fr-FR'], availableCodes: ENGLISH_ONLY })).toBe('en')
  })

  it('empty language list returns Arabic when available (preferred fallback)', () => {
    expect(inferLocale({ languagesOverride: [], availableCodes: ALL })).toBe('ar')
  })

  it('empty language list returns English when Arabic unavailable', () => {
    expect(inferLocale({ languagesOverride: [], availableCodes: ENGLISH_ONLY })).toBe('en')
  })
})

describe('inferLocale — robustness', () => {
  it('handles malformed BCP-47 tags gracefully', () => {
    expect(inferLocale({ languagesOverride: ['', 'ar-PS'], availableCodes: ALL })).toBe('ar')
  })

  it('is case-insensitive for primary language subtag', () => {
    expect(inferLocale({ languagesOverride: ['AR-PS'], availableCodes: ALL })).toBe('ar')
    expect(inferLocale({ languagesOverride: ['HE-IL'], availableCodes: ALL })).toBe('he')
  })

  it('handles preference-ordered lists correctly when first choice is unavailable', () => {
    // User's first preference is Hebrew, but build doesn't serve Hebrew.
    // Arabic is the next in their list, so we give them Arabic.
    expect(inferLocale({ languagesOverride: ['he-IL', 'ar-IL'], availableCodes: NO_HEBREW })).toBe('ar')
  })
})
