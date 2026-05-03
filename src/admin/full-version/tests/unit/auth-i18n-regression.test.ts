/**
 * FIND-ux-033: Regression test — auth pages must not contain hardcoded English UI text.
 *
 * Scans the <template> section of login.vue, register.vue, and forgot-password.vue
 * for known hardcoded English strings that should be replaced with $t() calls.
 * Also verifies all auth.* i18n keys exist in en, ar, and he locale files.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const pagesDir = resolve(__dirname, '../../src/pages')
const localesDir = resolve(__dirname, '../../src/plugins/i18n/locales')

const authPages = ['login.vue', 'register.vue', 'forgot-password.vue']

/**
 * Extract the <template> block from a Vue SFC.
 * Only scan the template — script block may legitimately contain English strings
 * (variable names, comments, etc.).
 */
function extractTemplate(content: string): string {
  const start = content.indexOf('<template>')
  const end = content.lastIndexOf('</template>')
  if (start === -1 || end === -1) return ''
  return content.slice(start, end + '</template>'.length)
}

/**
 * Hardcoded English phrases that must NOT appear in auth page templates.
 * Each entry was previously hardcoded and must now be behind a $t() call.
 */
const forbiddenLiterals = [
  'Sign In',
  'Sign in with Google',
  'Sign in with Apple',
  'Forgot Password?',
  'Remember me',
  'Send Reset Link',
  'Back to login',
  'Sign up',
  'Already have an account?',
  'Sign in instead',
  'I agree to the',
  'Privacy Policy',
  'Terms of Service',
  'Hide password',
  'Show password',
  // Title-case variants that indicate label-drift
  'Email Address',
  'Password',
  'Username',
]

/**
 * Strings that are allowed even though they look English — they are inside
 * attributes like placeholder, alt, testid, class, or are structural HTML
 * that is not user-visible UI text.
 */
const allowedContexts = [
  'placeholder=',
  'alt=',
  'data-testid=',
  'autocomplete=',
  ':to=',
  'class=',
]

describe('FIND-ux-033: No hardcoded English in auth page templates', () => {
  for (const page of authPages) {
    describe(page, () => {
      const content = readFileSync(resolve(pagesDir, page), 'utf-8')
      const template = extractTemplate(content)

      it('uses $t() for all user-visible strings (no hardcoded English)', () => {
        for (const literal of forbiddenLiterals) {
          // Look for the literal as standalone text content (not inside an attribute)
          const regex = new RegExp(`>\\s*${literal.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*<`, 'gi')
          const matches = template.match(regex)
          if (matches) {
            // Verify it's not inside an allowed attribute context
            for (const match of matches) {
              const idx = template.indexOf(match)
              const preceding = template.slice(Math.max(0, idx - 100), idx)
              const inAllowedContext = allowedContexts.some(ctx => preceding.includes(ctx))
              expect(inAllowedContext, `"${literal}" is hardcoded in ${page} template — should use $t()`).toBe(true)
            }
          }
        }
      })

      it('contains $t() calls (i18n is wired)', () => {
        expect(template).toContain("$t('auth.")
      })

      it('does not have bare label= attributes with hardcoded English', () => {
        // label="Email", label="Password", label="Username" should be :label="$t(...)"
        const bareLabels = template.match(/\slabel="[A-Z][a-z]+.*?"/g) || []
        expect(bareLabels, `Found bare label= attributes in ${page}: ${bareLabels.join(', ')}`).toHaveLength(0)
      })
    })
  }
})

describe('FIND-ux-033: auth.* i18n keys present in all locales', () => {
  const en = JSON.parse(readFileSync(resolve(localesDir, 'en.json'), 'utf-8'))
  const ar = JSON.parse(readFileSync(resolve(localesDir, 'ar.json'), 'utf-8'))
  const he = JSON.parse(readFileSync(resolve(localesDir, 'he.json'), 'utf-8'))

  it('en.json has auth namespace', () => {
    expect(en.auth).toBeDefined()
    expect(typeof en.auth).toBe('object')
  })

  it('ar.json has auth namespace', () => {
    expect(ar.auth).toBeDefined()
    expect(typeof ar.auth).toBe('object')
  })

  it('he.json has auth namespace', () => {
    expect(he.auth).toBeDefined()
    expect(typeof he.auth).toBe('object')
  })

  it('all en.auth keys exist in ar.json', () => {
    const enKeys = Object.keys(en.auth)
    const arKeys = Object.keys(ar.auth)
    const missing = enKeys.filter(k => !arKeys.includes(k))
    expect(missing, `Missing ar.json auth keys: ${missing.join(', ')}`).toHaveLength(0)
  })

  it('all en.auth keys exist in he.json', () => {
    const enKeys = Object.keys(en.auth)
    const heKeys = Object.keys(he.auth)
    const missing = enKeys.filter(k => !heKeys.includes(k))
    expect(missing, `Missing he.json auth keys: ${missing.join(', ')}`).toHaveLength(0)
  })

  it('EN auth values use sentence-case (not title-case)', () => {
    const titleCaseViolations: string[] = []
    for (const [key, value] of Object.entries(en.auth as Record<string, string>)) {
      // Check for Title Case pattern: two consecutive capitalized words
      // Allow proper nouns: Google, Apple, Cena
      const cleaned = value
        .replace(/\bGoogle\b/g, 'google')
        .replace(/\bApple\b/g, 'apple')
        .replace(/\bCena\b/g, 'cena')
      // Title case = multiple words starting with uppercase (not after a period)
      if (/(?<!\.\s)[A-Z][a-z]+\s[A-Z][a-z]/.test(cleaned)) {
        titleCaseViolations.push(`auth.${key}: "${value}"`)
      }
    }
    expect(titleCaseViolations, `Title-case violations: ${titleCaseViolations.join('; ')}`).toHaveLength(0)
  })
})
