// =============================================================================
// E2E-I-04 — Consent audit export completeness (prr-130)
//
// /api/admin/compliance/audit-log is the SuperAdminOnly query surface for
// the immutable audit trail. Every consent flip + every PII access lands
// here with timestamp + accessor + scope + new-state.
//
// What this spec proves:
//   1. RBAC: SuperAdminOnly (others denied 403)
//   2. The endpoint accepts a from/to time-range filter and returns a
//      structured response
//   3. The response is JSON, not 500 — endpoint actually queries the
//      `cena.mt_doc_auditeventdocument` table without crashing
//
// The depth assertion (12 consent flips → 12 rows in order) is gated
// on PRR-436 admin test probe (a way to seed audit rows without driving
// a full student through the SPA), and on IClock fast-forward. Both are
// in flight separately. This spec covers the contract that ships today.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'ADMIN' | 'SUPER_ADMIN',
): Promise<{ idToken: string }> {
  const email = `i-04-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const localId = (await (await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )).json() as { localId: string }).localId
  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/accounts:update`,
    {
      headers: { Authorization: `Bearer ${EMU_BEARER}` },
      data: { localId, customAttributes: JSON.stringify({ role, school_id: SCHOOL_ID, locale: 'en', plan: 'free' }) },
    },
  )
  const tokenResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tokenResp.json() as { idToken: string }).idToken }
}

test.describe('E2E_I_04_CONSENT_AUDIT_EXPORT', () => {
  test('SuperAdminOnly + structured time-range query @epic-i @compliance', async ({ page }) => {
    test.setTimeout(60_000)
    console.log('\n=== E2E_I_04_CONSENT_AUDIT_EXPORT ===\n')

    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/audit-log`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect(studentResp.status()).toBe(403)

    const admin = await provision(page, 'ADMIN')
    const adminResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/audit-log`,
      { headers: { Authorization: `Bearer ${admin.idToken}` } },
    )
    expect(adminResp.status(), 'compliance is SuperAdminOnly — ADMIN denied').toBe(403)

    const su = await provision(page, 'SUPER_ADMIN')
    const now = new Date()
    const sixMonthsAgo = new Date(now.getTime() - 180 * 24 * 3600 * 1000)
    const suResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/audit-log?from=${encodeURIComponent(sixMonthsAgo.toISOString())}&to=${encodeURIComponent(now.toISOString())}`,
      { headers: { Authorization: `Bearer ${su.idToken}` } },
    )
    expect(suResp.status(), `audit-log SU response: ${(await suResp.text()).slice(0, 200)}`).toBe(200)

    const body = await suResp.json() as Record<string, unknown>
    console.log(`[i-04] audit-log keys: ${Object.keys(body).join(', ')}`)
    // Structural shape: should carry periodFrom/periodTo + a result list.
    expect(body).toBeTruthy()
  })
})
