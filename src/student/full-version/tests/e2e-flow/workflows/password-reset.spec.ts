// =============================================================================
// TASK-E2E-A-03 — Password reset
//
// Journey: /forgot-password → email entered → SPA POSTs
// /api/auth/password-reset → backend asks Firebase Admin SDK to send the
// reset email → emulator captures the email at /emulator/v1/projects/{p}/oobCodes
// → test fetches the OOB code → uses Firebase emu's resetPassword endpoint
// to set a new password → sign-in with the new password succeeds.
//
// Boundary coverage:
//   * DOM       — success card visible after submit (forgot-success-icon)
//   * API       — POST /api/auth/password-reset returns 204 (success path)
//                 OR 200; never returns a 4xx for a known email (the
//                 backend explicitly returns 204 for both known and unknown
//                 to defeat account enumeration — OWASP)
//   * Firebase  — OOB password-reset code emitted to the emu's oobCodes
//                 endpoint within 5s; resetting via that code unlocks
//                 sign-in with the NEW password and rejects the OLD one
//
// Regressions caught:
//   * /api/auth/password-reset silently dropped (500/404 — looks fine to
//     user but no email is sent)
//   * Reset-link is signed but accepts the OLD password (reset didn't
//     actually take)
//   * 429 lockout firing on a single legitimate attempt
// =============================================================================

import { test, expect } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

interface OobCodeEntry {
  email: string
  oobCode: string
  oobLink: string
  requestType: 'PASSWORD_RESET' | 'EMAIL_SIGNIN' | 'VERIFY_EMAIL' | string
}

async function createFreshEmuUser(email: string, password: string): Promise<string> {
  const resp = await fetch(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, returnSecureToken: true }),
    },
  )
  if (!resp.ok)
    throw new Error(`Firebase emu signUp failed ${resp.status}: ${await resp.text()}`)
  const body = await resp.json() as { localId: string }
  return body.localId
}

async function fetchPasswordResetCode(email: string, sinceMs: number, timeoutMs = 10_000): Promise<OobCodeEntry> {
  const url = `http://${EMU_HOST}/emulator/v1/projects/${PROJECT_ID}/oobCodes`
  const deadline = Date.now() + timeoutMs
  let lastSeen: OobCodeEntry[] = []
  while (Date.now() < deadline) {
    const resp = await fetch(url, { headers: { Authorization: `Bearer ${EMU_BEARER}` } })
    if (resp.ok) {
      const body = await resp.json() as { oobCodes?: OobCodeEntry[] }
      lastSeen = body.oobCodes ?? []
      const match = lastSeen.find(
        c => c.email === email && c.requestType === 'PASSWORD_RESET',
      )
      if (match)
        return match
    }
    await new Promise(r => setTimeout(r, 250))
  }
  throw new Error(
    `[A-03] Timed out waiting for PASSWORD_RESET oobCode for ${email} since ${sinceMs}ms. ` +
    `Last oobCodes seen: ${JSON.stringify(lastSeen)}. ` +
    'If empty, the backend never reached Firebase — check student-api logs for ' +
    'POST /api/auth/password-reset.',
  )
}

async function resetEmuPassword(oobCode: string, newPassword: string): Promise<void> {
  const resp = await fetch(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:resetPassword?key=fake-api-key`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ oobCode, newPassword }),
    },
  )
  if (!resp.ok)
    throw new Error(`Firebase emu resetPassword failed ${resp.status}: ${await resp.text()}`)
}

async function emuSignIn(email: string, password: string): Promise<Response> {
  return fetch(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, returnSecureToken: true }),
    },
  )
}

test.describe('E2E-A-03 password reset', () => {
  // Dismiss FirstRunLanguageChooser modal — see sign-in.spec.ts comment.
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })
  })

  test('forgot → emu OOB → reset → sign-in with new password @auth @p1', async ({ page }) => {
    // Use a fresh user so we control both old and new passwords without
    // disturbing the seeded fixtures (other specs depend on student1's
    // seeded password).
    const email = `e2e-reset-${Date.now()}@cena.test`
    const oldPassword = 'OldPassword-1234!'
    const newPassword = 'NewPassword-5678!'
    await createFreshEmuUser(email, oldPassword)

    // Drive the /forgot-password form.
    await page.goto('/forgot-password')
    await expect(page.getByTestId('forgot-password-form')).toBeVisible()

    // ── Boundary: API ──
    // Capture the POST so we can pin the contract — must be 204 (or 200)
    // for both known and unknown emails. A 4xx here would mean the OWASP
    // account-enumeration defence is broken.
    const apiResp = page.waitForResponse(
      resp => resp.url().includes('/api/auth/password-reset')
        && resp.request().method() === 'POST',
      { timeout: 15_000 },
    )

    const sinceMs = Date.now()
    await page.getByTestId('forgot-email').locator('input').fill(email)
    await page.getByTestId('forgot-submit').click()

    const apiRespResolved = await apiResp
    expect(
      [200, 204].includes(apiRespResolved.status()),
      `POST /api/auth/password-reset must be 2xx (OWASP no-enumeration), got ${apiRespResolved.status()}`,
    ).toBe(true)

    // ── Boundary: DOM ──
    await expect(page.getByTestId('forgot-success-icon')).toBeVisible({ timeout: 10_000 })

    // ── Boundary: Firebase emu OOB ──
    const code = await fetchPasswordResetCode(email, sinceMs)
    expect(code.oobCode, 'Firebase emu must emit a PASSWORD_RESET oobCode').toBeTruthy()

    await resetEmuPassword(code.oobCode, newPassword)

    // ── Verification: new password works, old does not ──
    const oldResp = await emuSignIn(email, oldPassword)
    expect(
      oldResp.ok,
      'OLD password must be rejected after reset — reset did not take',
    ).toBe(false)

    const newResp = await emuSignIn(email, newPassword)
    expect(
      newResp.ok,
      'NEW password must succeed after reset — token issuance failed',
    ).toBe(true)
  })

  test('forgot for unknown email → identical success UI (no enumeration leak) @auth @p1', async ({ page }) => {
    await page.goto('/forgot-password')
    await page.getByTestId('forgot-email').locator('input').fill(`never-existed-${Date.now()}@cena.test`)
    await page.getByTestId('forgot-submit').click()

    // Backend returns 204 for unknown email — UI shows the SAME success
    // card. Anything that visually distinguishes "unknown email" from
    // "real email" is an account-enumeration leak.
    await expect(page.getByTestId('forgot-success-icon')).toBeVisible({ timeout: 10_000 })
  })
})
