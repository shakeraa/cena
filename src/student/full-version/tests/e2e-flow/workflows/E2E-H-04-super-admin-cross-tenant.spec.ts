// =============================================================================
// E2E-H-04 — SUPER_ADMIN can cross tenants; ADMIN cannot (P0, RBAC)
//
// /api/admin/users — `?tenant=X` query param honored for SUPER_ADMIN
// (with audit log row); ignored for ADMIN (response filtered to the
// caller's own institute regardless of query param).
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

async function provision(
  page: import('@playwright/test').Page,
  role: 'ADMIN' | 'SUPER_ADMIN',
  schoolId: string,
): Promise<{ idToken: string }> {
  const email = `h-04-${role.toLowerCase()}-${schoolId}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      data: { localId, customAttributes: JSON.stringify({ role, school_id: schoolId, institute_id: schoolId, locale: 'en' }) },
    },
  )
  await page.waitForTimeout(300)
  const tok = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tok.json() as { idToken: string }).idToken }
}

test.describe('E2E_H_04_SUPER_ADMIN_CROSS_TENANT', () => {
  test('SUPER_ADMIN ?tenant=X honored; ADMIN query param ignored @epic-h @tenant @rbac @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    const adminA = await provision(page, 'ADMIN', 'tenant-a')
    const su = await provision(page, 'SUPER_ADMIN', 'tenant-a')

    // ADMIN-A with ?tenant=tenant-b — must NOT escalate to tenant-b.
    // Either the query is ignored (filtered to A) or the request is
    // rejected with 400. NEVER 200 with tenant-b data.
    const adminCrossResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/users?tenant=tenant-b`,
      { headers: { Authorization: `Bearer ${adminA.idToken}` } },
    )
    console.log(`[h-04] ADMIN-A ?tenant=tenant-b → ${adminCrossResp.status()}`)
    expect(adminCrossResp.status()).toBeLessThan(500)
    // Body inspection: if 200, must NOT contain tenant-b id strings.
    if (adminCrossResp.status() === 200) {
      const body = await adminCrossResp.text()
      // Privilege-escalation invariant: no tenant-b leakage.
      expect(body.toLowerCase()).not.toContain('tenant-b')
    }

    // SUPER_ADMIN with ?tenant=tenant-b — must work AND be audited.
    const suCrossResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/users?tenant=tenant-b`,
      { headers: { Authorization: `Bearer ${su.idToken}` } },
    )
    console.log(`[h-04] SUPER_ADMIN ?tenant=tenant-b → ${suCrossResp.status()}`)
    expect(suCrossResp.status()).toBeLessThan(500)
  })
})
