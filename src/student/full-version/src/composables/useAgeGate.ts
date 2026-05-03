/**
 * FIND-privacy-001: Age gate composable.
 *
 * Calculates age from date of birth and determines the consent path
 * required for registration per COPPA/GDPR/ICO/Israel PPL.
 *
 * Consent tiers:
 *   - age < 13:  "child"  → parent-required (COPPA §312.5)
 *   - age 13-15: "teen"   → parent-required (GDPR Art 8, ICO Std 7)
 *   - age >= 16: "adult"  → no consent required (GDPR default)
 */

export type ConsentTier = 'child' | 'teen' | 'adult'
export type ConsentPath = 'none' | 'parent-required'

export interface AgeGateResult {
  age: number
  consentTier: ConsentTier
  consentPath: ConsentPath
  requiresParentalConsent: boolean
  isBlocked: boolean // true if age < 13 and no parent consent flow available
}

/**
 * Calculates age in complete years. Handles leap-year birthdays correctly.
 */
export function calculateAge(dateOfBirth: Date, asOf: Date = new Date()): number {
  let age = asOf.getFullYear() - dateOfBirth.getFullYear()

  const birthdayThisYear = new Date(
    asOf.getFullYear(),
    dateOfBirth.getMonth(),
    dateOfBirth.getDate(),
  )

  if (asOf < birthdayThisYear)
    age--

  return age
}

/**
 * Determines the consent tier and path for a given date of birth.
 */
export function evaluateAgeGate(dateOfBirth: Date): AgeGateResult {
  const age = calculateAge(dateOfBirth)

  if (age < 13) {
    return {
      age,
      consentTier: 'child',
      consentPath: 'parent-required',
      requiresParentalConsent: true,
      isBlocked: false,
    }
  }

  if (age < 16) {
    return {
      age,
      consentTier: 'teen',
      consentPath: 'parent-required',
      requiresParentalConsent: true,
      isBlocked: false,
    }
  }

  return {
    age,
    consentTier: 'adult',
    consentPath: 'none',
    requiresParentalConsent: false,
    isBlocked: false,
  }
}

/**
 * Validates that a DOB string is a plausible date of birth.
 * Returns null if valid, or an error key for i18n if invalid.
 */
export function validateDateOfBirth(dob: string | null | undefined): string | null {
  if (!dob)
    return 'auth.ageGate.dobRequired'

  const parsed = new Date(dob)
  if (Number.isNaN(parsed.getTime()))
    return 'auth.ageGate.dobInvalid'

  const today = new Date()
  if (parsed > today)
    return 'auth.ageGate.dobFuture'

  // Reject ages over 120 years
  const minDate = new Date(today.getFullYear() - 120, today.getMonth(), today.getDate())
  if (parsed < minDate)
    return 'auth.ageGate.dobInvalid'

  // Reject ages under 4 (obviously wrong)
  const maxRecentDate = new Date(today.getFullYear() - 4, today.getMonth(), today.getDate())
  if (parsed > maxRecentDate)
    return 'auth.ageGate.tooYoung'

  return null
}
