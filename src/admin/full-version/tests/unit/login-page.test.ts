/**
 * Tests for the Login page template and logic.
 *
 * Validates:
 *  - Template has correct brand title "Cena Admin"
 *  - Login form contains email + password fields
 *  - Google and Apple sign-in buttons exist
 *  - Forgot Password link points to the right route
 *  - Page meta declares unauthenticatedOnly
 *
 * FIND-qa-008: baseline admin test infrastructure
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const loginPage = resolve(__dirname, '../../src/pages/login.vue')
const content = readFileSync(loginPage, 'utf-8')

describe('Login page structure', () => {
  it('displays "Cena Admin" as the page title', () => {
    expect(content).toContain('Cena Admin')
  })

  it('has an email input field', () => {
    expect(content).toContain('type="email"')
    expect(content).toContain('v-model="credentials.email"')
  })

  it('has a password input field', () => {
    expect(content).toContain('v-model="credentials.password"')
    expect(content).toContain('autocomplete="current-password"')
  })

  it('has a Google sign-in button', () => {
    expect(content).toContain('Sign in with Google')
    expect(content).toContain('tabler-brand-google')
  })

  it('has an Apple sign-in button', () => {
    expect(content).toContain('Sign in with Apple')
    expect(content).toContain('tabler-brand-apple')
  })

  it('has a Forgot Password link', () => {
    expect(content).toContain('Forgot Password?')
    expect(content).toContain("name: 'forgot-password'")
  })

  it('uses useFirebaseAuth composable', () => {
    expect(content).toContain('useFirebaseAuth')
    expect(content).toContain('loginWithEmail')
    expect(content).toContain('loginWithGoogle')
    expect(content).toContain('loginWithApple')
  })

  it('declares unauthenticatedOnly meta', () => {
    expect(content).toContain('unauthenticatedOnly: true')
  })

  it('redirects to return-to path after login', () => {
    expect(content).toContain('route.query.to')
  })

  it('shows auth error alert', () => {
    expect(content).toContain('v-if="authError"')
    expect(content).toContain('color="error"')
  })
})
