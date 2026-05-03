// =============================================================================
// E2E-I-03 — RTBF crypto-shred contract (ADR-0038) — P0
//
// The right-to-be-forgotten cascade leaves aggregates in place as tombstones
// (so event-replay invariants survive) but irreversibly shreds the encryption
// pepper for personal columns. After erasure, the ciphertext is "frozen" —
// no key exists to recover the plaintext.
//
// What this spec proves at the contract layer:
//   1. POST /api/me/gdpr/erasure returns a structured response with a
//      cooling-period (cannot self-execute immediately, prevents abuse)
//   2. GET /api/me/gdpr/erasure/status returns the active request shape
//   3. The erasure response must mention `coolingPeriod` somewhere — that's
//      the load-bearing posture: NO immediate hard-delete, ALL paths go
//      through the 30-day cooling period during which the manifest builds
//
// What this spec doesn't drive: the actual cascade execution, key rotation,
// and ciphertext-at-rest verification. That requires a real student with
// data plus a clock fast-forward; PRR-436 admin probe gates this depth.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

test.describe('E2E_I_03_RTBF_CRYPTO_SHRED', () => {
  test('cooling-period contract on erasure request @epic-i @gdpr @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_I_03_RTBF_CRYPTO_SHRED ===\n')

    const email = `i-03-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const bootstrapResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: bootstrapToken } = await bootstrapResp.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'I03 Tester' },
    })).status()).toBe(200)
    const tokResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tokResp.json() as { idToken: string }

    // ── 1. Status read-back before any request — should NOT have an active request ──
    const before = await page.request.get(`${STUDENT_API}/api/me/gdpr/erasure/status`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(before.status()).toBe(200)
    interface StatusBody { hasActiveRequest: boolean; status?: string; coolingPeriodEnds?: string }
    const beforeBody = await before.json() as StatusBody
    expect(beforeBody.hasActiveRequest, 'no erasure should be active for a fresh student').toBe(false)
    console.log('[i-03] status before request: hasActiveRequest=false')

    // ── 2. Submit erasure request — must succeed AND surface coolingPeriod ──
    const erase = await page.request.post(`${STUDENT_API}/api/me/gdpr/erasure`, {
      headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
    })
    expect(erase.status()).toBe(200)
    interface EraseBody { studentId: string; status: string; coolingPeriodEnds: string; message?: string }
    const eraseBody = await erase.json() as EraseBody

    // Critical invariant: cooling period must be present and parseable as a date.
    expect(eraseBody.coolingPeriodEnds).toBeTruthy()
    const cooling = new Date(eraseBody.coolingPeriodEnds)
    expect(Number.isNaN(cooling.getTime()), 'coolingPeriodEnds must be a parseable date').toBe(false)
    const now = new Date()
    const daysAhead = (cooling.getTime() - now.getTime()) / (1000 * 60 * 60 * 24)
    console.log(`[i-03] cooling period: ${daysAhead.toFixed(1)} days ahead`)
    expect(daysAhead, 'cooling period must be in the future (no immediate hard-delete)').toBeGreaterThan(0)
    expect(daysAhead, 'cooling period should be ~30 days per ADR-0038').toBeLessThan(40)

    // ── 3. Status read-back AFTER request — must show active request ──
    const after = await page.request.get(`${STUDENT_API}/api/me/gdpr/erasure/status`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    const afterBody = await after.json() as StatusBody
    expect(afterBody.hasActiveRequest, 'status must reflect the just-submitted request').toBe(true)
    console.log(`[i-03] status after request: hasActiveRequest=true status=${afterBody.status}`)
  })
})
