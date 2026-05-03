/**
 * FIND-privacy-001: Unit tests for the age gate composable.
 *
 * Tests cover:
 *   - Age calculation accuracy (including leap years)
 *   - Consent tier assignment per COPPA/GDPR/ICO thresholds
 *   - DOB validation edge cases
 */

import { describe, expect, it } from 'vitest'
import {
  calculateAge,
  evaluateAgeGate,
  validateDateOfBirth,
} from '@/composables/useAgeGate'

describe('calculateAge', () => {
  it('calculates age for a birthday that has already passed this year', () => {
    const dob = new Date(2010, 0, 15) // Jan 15, 2010
    const asOf = new Date(2026, 3, 11) // Apr 11, 2026

    expect(calculateAge(dob, asOf)).toBe(16)
  })

  it('calculates age for a birthday that has NOT yet passed this year', () => {
    const dob = new Date(2010, 11, 25) // Dec 25, 2010
    const asOf = new Date(2026, 3, 11) // Apr 11, 2026

    expect(calculateAge(dob, asOf)).toBe(15)
  })

  it('returns 0 for a baby born today', () => {
    const today = new Date()

    expect(calculateAge(today, today)).toBe(0)
  })

  it('handles leap year birthday (Feb 29) correctly on non-leap years', () => {
    const dob = new Date(2008, 1, 29) // Feb 29, 2008 (leap)
    const asOf = new Date(2025, 2, 1) // Mar 1, 2025 (non-leap, past birthday)

    expect(calculateAge(dob, asOf)).toBe(17)
  })

  it('handles leap year birthday (Feb 29) before birthday in non-leap year', () => {
    const dob = new Date(2008, 1, 29) // Feb 29, 2008
    const asOf = new Date(2025, 1, 28) // Feb 28, 2025 (before birthday)

    expect(calculateAge(dob, asOf)).toBe(16)
  })

  it('calculates exact boundary: 13th birthday', () => {
    const dob = new Date(2013, 3, 11) // Apr 11, 2013
    const asOf = new Date(2026, 3, 11) // Apr 11, 2026 = exactly 13

    expect(calculateAge(dob, asOf)).toBe(13)
  })

  it('calculates day before 13th birthday', () => {
    const dob = new Date(2013, 3, 12) // Apr 12, 2013
    const asOf = new Date(2026, 3, 11) // Apr 11, 2026 = 12 (not yet 13)

    expect(calculateAge(dob, asOf)).toBe(12)
  })
})

describe('evaluateAgeGate', () => {
  it('returns "child" tier for age < 13 (COPPA)', () => {
    const dob = new Date(2016, 0, 1) // 10 years old in 2026
    const result = evaluateAgeGate(dob)

    expect(result.consentTier).toBe('child')
    expect(result.consentPath).toBe('parent-required')
    expect(result.requiresParentalConsent).toBe(true)
  })

  it('returns "teen" tier for age 13-15 (GDPR Art 8)', () => {
    const dob = new Date(2012, 0, 1) // 14 in 2026
    const result = evaluateAgeGate(dob)

    expect(result.consentTier).toBe('teen')
    expect(result.consentPath).toBe('parent-required')
    expect(result.requiresParentalConsent).toBe(true)
  })

  it('returns "adult" tier for age >= 16', () => {
    const dob = new Date(2008, 0, 1) // 18 in 2026
    const result = evaluateAgeGate(dob)

    expect(result.consentTier).toBe('adult')
    expect(result.consentPath).toBe('none')
    expect(result.requiresParentalConsent).toBe(false)
  })

  it('boundary: exactly 13 is "teen" not "child"', () => {
    // Person who turns 13 today
    const today = new Date()
    const dob = new Date(today.getFullYear() - 13, today.getMonth(), today.getDate())
    const result = evaluateAgeGate(dob)

    expect(result.age).toBe(13)
    expect(result.consentTier).toBe('teen')
  })

  it('boundary: exactly 16 is "adult"', () => {
    const today = new Date()
    const dob = new Date(today.getFullYear() - 16, today.getMonth(), today.getDate())
    const result = evaluateAgeGate(dob)

    expect(result.age).toBe(16)
    expect(result.consentTier).toBe('adult')
  })

  it('boundary: day before 13th birthday is "child"', () => {
    const today = new Date()
    const dob = new Date(today.getFullYear() - 13, today.getMonth(), today.getDate() + 1)
    const result = evaluateAgeGate(dob)

    expect(result.age).toBe(12)
    expect(result.consentTier).toBe('child')
  })
})

describe('validateDateOfBirth', () => {
  it('returns error key for null input', () => {
    expect(validateDateOfBirth(null)).toBe('auth.ageGate.dobRequired')
  })

  it('returns error key for empty string', () => {
    expect(validateDateOfBirth('')).toBe('auth.ageGate.dobRequired')
  })

  it('returns error key for invalid date string', () => {
    expect(validateDateOfBirth('not-a-date')).toBe('auth.ageGate.dobInvalid')
  })

  it('returns error key for future date', () => {
    const future = new Date()

    future.setFullYear(future.getFullYear() + 1)
    expect(validateDateOfBirth(future.toISOString().split('T')[0])).toBe('auth.ageGate.dobFuture')
  })

  it('returns error key for impossibly old date', () => {
    expect(validateDateOfBirth('1800-01-01')).toBe('auth.ageGate.dobInvalid')
  })

  it('returns error key for too young (under 4)', () => {
    const tooRecent = new Date()

    tooRecent.setFullYear(tooRecent.getFullYear() - 2)
    expect(validateDateOfBirth(tooRecent.toISOString().split('T')[0])).toBe('auth.ageGate.tooYoung')
  })

  it('returns null for a valid date of birth', () => {
    expect(validateDateOfBirth('2010-06-15')).toBeNull()
  })

  it('returns null for an adult date of birth', () => {
    expect(validateDateOfBirth('1995-03-20')).toBeNull()
  })
})
