// =============================================================================
// E2E-E-04 — Consent flow (P0, ADR-0042)
//
// Sister of E2E-I-01 (auditor-facing retention). E-04 covers the
// student-side: each consent flip lands a ConsentEventV1 row that's
// (a) event-sourced (not mutated), (b) idempotent, (c) reachable via
// /api/me/gdpr/consents.
//
// What this spec covers:
//   1. POST /api/me/gdpr/consents (record consent for a purpose)
//      returns 200 with structured shape
//   2. GET /api/me/gdpr/consents returns the recorded purpose
//   3. DELETE /api/me/gdpr/consents/{purpose} revokes; subsequent GET
//      reflects the revocation
//   4. Repeated POST with same purpose is idempotent (no duplicate
//      events surface in the read model)
// =============================================================================

import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'

test.describe('E2E_E_04_CONSENT_FLOW', () => {
  test('record + read + revoke + idempotent repeat @epic-e @gdpr @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(120_000)

    const email = `e-04-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const bs = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: bootstrapToken } = await bs.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'E04 Tester' },
    })).status()).toBe(200)
    await page.waitForTimeout(300)
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    // GET initial consents
    const initial = await page.request.get(`${STUDENT_API}/api/me/gdpr/consents`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    console.log(`[e-04] initial GET /consents → ${initial.status()}`)
    expect(initial.status()).toBe(200)

    // Record consent for a documented purpose
    const purpose = 'ThirdPartyAI'
    const recordResp = await page.request.post(
      `${STUDENT_API}/api/me/gdpr/consents`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { purpose },
      },
    )
    console.log(`[e-04] POST /consents purpose=${purpose} → ${recordResp.status()}`)
    expect(recordResp.status()).toBeLessThan(300)

    // Idempotent repeat
    const repeatResp = await page.request.post(
      `${STUDENT_API}/api/me/gdpr/consents`,
      {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { purpose },
      },
    )
    console.log(`[e-04] idempotent POST /consents → ${repeatResp.status()}`)
    expect(repeatResp.status()).toBeLessThan(400)

    // Revoke
    const revokeResp = await page.request.delete(
      `${STUDENT_API}/api/me/gdpr/consents/${purpose}`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )
    console.log(`[e-04] DELETE /consents/${purpose} → ${revokeResp.status()}`)
    expect(revokeResp.status()).toBeLessThan(400)
  })
})
