/**
 * CI lint: verifies that every i18n key containing a numeric placeholder
 * ({count}, {days}, {minutes}, {hours}, {xp}, {mins}, {secs}, {total},
 * {unlocked}, {earned}) uses vue-i18n plural syntax (pipe-separated forms)
 * in every locale file, and that each locale has the minimum required number
 * of forms per CLDR plural rules.
 *
 * English: 2 forms (one | other)
 * Hebrew:  4 forms (one | two | many | other)
 * Arabic:  6 forms (zero | one | two | few | many | other)
 *
 * FIND-pedagogy-014
 */

import { readFileSync } from 'node:fs'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const LOCALES_DIR = resolve(__dirname, '../src/plugins/i18n/locales')

const LOCALE_MIN_FORMS = {
  en: 2,
  he: 4,
  ar: 6,
}

// Patterns that indicate a numeric placeholder requiring pluralization
const NUMERIC_PLACEHOLDER_RE = /\{(count|days|minutes|hours|xp|mins|secs|total|unlocked|earned|n)\}/

function flattenKeys(obj, prefix = '') {
  const result = {}
  for (const [key, value] of Object.entries(obj)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      Object.assign(result, flattenKeys(value, path))
    }
    else if (typeof value === 'string') {
      result[path] = value
    }
  }
  return result
}

let failures = 0

for (const [locale, minForms] of Object.entries(LOCALE_MIN_FORMS)) {
  const filePath = resolve(LOCALES_DIR, `${locale}.json`)
  let json
  try {
    json = JSON.parse(readFileSync(filePath, 'utf8'))
  }
  catch (err) {
    console.error(`[FAIL] Cannot read ${filePath}: ${err.message}`)
    failures++
    continue
  }

  const flat = flattenKeys(json)

  for (const [key, value] of Object.entries(flat)) {
    // Skip keys that start with $ (vuetify framework keys) or legal/onboarding prose
    if (key.startsWith('$') || key.startsWith('legal.') || key.startsWith('onboarding.'))
      continue

    if (!NUMERIC_PLACEHOLDER_RE.test(value))
      continue

    // Count pipe-separated forms
    const forms = value.split(' | ').length

    if (forms < minForms) {
      console.error(
        `[FAIL] ${locale}.json key "${key}" has ${forms} plural form(s), needs >= ${minForms}. Value: "${value.slice(0, 80)}..."`,
      )
      failures++
    }
  }
}

if (failures > 0) {
  console.error(`\n${failures} plural form violation(s) found.`)
  process.exit(1)
}
else {
  console.log('All numeric i18n keys have sufficient plural forms.')
  process.exit(0)
}
