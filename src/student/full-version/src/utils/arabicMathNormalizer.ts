/**
 * RDY-026: Client-side Arabic math input normalizer.
 * Mirrors the C# ArabicMathNormalizer for real-time preview.
 * The backend is the authoritative normalizer — this is for display only.
 */

/** Arabic-Indic digit mapping (٠-٩ → 0-9) */
const EASTERN_DIGITS: Record<string, string> = {
  '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4',
  '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9',
}

/** Arabic variable → Latin variable */
const VARIABLE_MAP: Record<string, string> = {
  'س': 'x', 'ص': 'y', 'ع': 'z',
  'ن': 'n', 'م': 'm', 'ل': 'l',
  'ك': 'k', 'ر': 'r', 'ت': 't',
}

/** Arabic operator → ASCII operator */
const OPERATOR_MAP: Record<string, string> = {
  '×': '*', '÷': '/', '−': '-',
  '٫': '.', // Arabic decimal separator
}

/** Arabic math terms → LaTeX (multi-char, greedy match) */
const TERM_MAP: [string, string][] = [
  ['جذر', 'sqrt'],
  ['جيب', 'sin'],
  ['جتا', 'cos'],
  ['ظل', 'tan'],
  ['لو', 'log'],
  ['لن', 'ln'],
]

// Pre-sorted by length descending for greedy matching
const SORTED_TERMS = [...TERM_MAP].sort((a, b) => b[0].length - a[0].length)

/** Returns true if input contains Arabic characters that need normalization. */
export function needsNormalization(input: string): boolean {
  if (!input) return false
  // Check for any Arabic Unicode block character (U+0600–U+06FF) or Eastern Arabic digits
  return /[\u0600-\u06FF\u0660-\u0669]/.test(input) || /[×÷−٫]/.test(input)
}

/** Normalize Arabic math input to ASCII/LaTeX. Single-pass, bidi-safe. */
export function normalize(input: string): string {
  if (!input || !needsNormalization(input)) return input

  const result: string[] = []
  let i = 0

  while (i < input.length) {
    // Try multi-char terms first (greedy)
    let matched = false
    for (const [arabic, latin] of SORTED_TERMS) {
      if (input.startsWith(arabic, i)) {
        result.push(latin)
        i += arabic.length
        matched = true
        break
      }
    }
    if (matched) continue

    const ch = input[i]

    // Eastern Arabic digits
    if (EASTERN_DIGITS[ch]) {
      result.push(EASTERN_DIGITS[ch])
      i++
      continue
    }

    // Operators
    if (OPERATOR_MAP[ch]) {
      result.push(OPERATOR_MAP[ch])
      i++
      continue
    }

    // Single Arabic variable letters
    if (VARIABLE_MAP[ch]) {
      result.push(VARIABLE_MAP[ch])
      i++
      continue
    }

    // Pass through unchanged (Latin chars, spaces, parentheses, etc.)
    result.push(ch)
    i++
  }

  return result.join('')
}
