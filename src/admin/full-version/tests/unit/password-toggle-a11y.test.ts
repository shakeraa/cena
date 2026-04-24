/**
 * FIND-ux-027: Password eye-icon target-size regression tests.
 *
 * Validates that the password visibility toggle on auth pages uses an IconBtn
 * (Vuetify alias for VBtn with icon=true) inside a #append-inner slot instead
 * of the bare :append-inner-icon prop, which renders as a 16x16 icon with no
 * button wrapper. The IconBtn with size="small" renders at ~32x32 CSS px,
 * exceeding WCAG 2.2 AA minimum of 24x24.
 *
 * Also subsumes FIND-ux-018 (the "Append" accessible-name leak) because the
 * old :append-inner-icon created a button labelled "Append" by Vuetify default;
 * the new IconBtn carries an explicit aria-label.
 *
 * Task: t_4b64574b8e08
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const pagesDir = resolve(__dirname, '../../src/pages')

const loginContent = readFileSync(resolve(pagesDir, 'login.vue'), 'utf-8')
const registerContent = readFileSync(resolve(pagesDir, 'register.vue'), 'utf-8')

const authPages = [
  { name: 'login.vue', content: loginContent },
  { name: 'register.vue', content: registerContent },
]

describe('FIND-ux-027: password toggle uses IconBtn for WCAG target-size', () => {
  for (const { name, content } of authPages) {
    describe(name, () => {
      it('does NOT use :append-inner-icon for the eye toggle (old 16x16 pattern)', () => {
        // The old pattern was :append-inner-icon="isPasswordVisible ? 'tabler-eye-off' : 'tabler-eye'"
        // This must be absent after the fix.
        expect(content).not.toMatch(/:append-inner-icon=.*tabler-eye/)
      })

      it('does NOT use @click:append-inner for visibility toggle', () => {
        expect(content).not.toMatch(/@click:append-inner=.*isPasswordVisible/)
      })

      it('uses a #append-inner template slot', () => {
        expect(content).toContain('<template #append-inner>')
      })

      it('wraps the eye icon in an IconBtn', () => {
        expect(content).toContain('<IconBtn')
        expect(content).toMatch(/IconBtn[\s\S]*?tabler-eye/)
      })

      it('IconBtn has size="small" for >= 24x24 CSS px hit area', () => {
        // Vuetify VBtn size="small" = 32x32 (well above WCAG 24x24 minimum)
        expect(content).toMatch(/IconBtn[\s\S]*?size="small"/)
      })

      it('has a dynamic aria-label reflecting toggle state (Show/Hide password)', () => {
        expect(content).toMatch(/:aria-label=.*Show password/)
        expect(content).toMatch(/:aria-label=.*Hide password/)
      })

      it('has data-testid="password-toggle-btn" for test automation', () => {
        expect(content).toContain('data-testid="password-toggle-btn"')
      })
    })
  }
})
