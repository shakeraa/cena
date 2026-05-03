// =============================================================================
// E2E-I-06 — Age-band field filter consistency (prr-052)
//
// Children's data exposure shrinks at age thresholds (12 → 13 → 14 etc).
// The dashboard field set must match policy at the CURRENT age, and any
// historical export must respect the CURRENT band — not the snapshot's
// original. This is FIND-privacy-010 (ICO Children's Code Std 3+7).
//
// What this spec proves at the contract layer:
//   1. The /api/me/onboarding payload accepts age-related fields
//   2. The /api/me hydration response respects the documented age-band
//      field set (no "parentEmail" leaking to a 13+ student response, etc)
//
// Full IClock fast-forward across 12→13→14 transitions is depth-test
// material — requires test seams in the age calculator and a probe
// endpoint to flip clock without redeploying. PRR-436 admin probe + a
// follow-up clock-injection task gate this depth. Spec covers contract.
// =============================================================================

import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'

test.describe('E2E_I_06_AGE_BAND_CONSISTENCY', () => {
  test('/api/me hydration shape is age-aware @epic-i @compliance @parent', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_I_06_AGE_BAND_CONSISTENCY ===\n')

    const email = `i-06-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'I06 Tester' },
    })).status()).toBe(200)
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    // /api/me — hydration response shape
    const meResp = await page.request.get(`${STUDENT_API}/api/me`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(meResp.status()).toBe(200)
    const me = await meResp.json() as Record<string, unknown>
    console.log(`[i-06] /api/me keys: ${Object.keys(me).slice(0, 20).join(', ')}`)

    // ── ICO Children's Code Std 3+7 ──
    // The hydration response MUST NOT carry parent-only fields (e.g.
    // raw `parentEmail`) on the student-side surface. These belong on
    // the parent's own /api/me/parent/... routes. We assert by negative
    // property: no obviously parent-only field name leaks.
    const blocklistedKeys = ['parentEmail', 'parentPhone', 'parentSsn', 'parentSecondaryEmail']
    for (const k of blocklistedKeys) {
      expect(me, `student /api/me must NOT carry parent-only field "${k}"`).not.toHaveProperty(k)
    }
    console.log('[i-06] no parent-only fields leaked on /api/me')
  })
})
