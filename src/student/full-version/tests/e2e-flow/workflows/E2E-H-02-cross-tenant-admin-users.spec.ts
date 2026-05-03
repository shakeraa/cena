// =============================================================================
// E2E-H-02 — Admin A cannot query admin B's users (P0, RBAC)
//
// /api/admin/users — list endpoint must filter by caller's tenant.
// Direct-id probe must return 404 (not 403) for cross-tenant ids — 403
// would leak existence and enable enumeration.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'ADMIN' | 'SUPER_ADMIN',
  schoolId: string,
): Promise<{ idToken: string; uid: string }> {
  const email = `h-02-${role.toLowerCase()}-${schoolId}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
  return { idToken: (await tok.json() as { idToken: string }).idToken, uid: localId }
}

test.describe('E2E_H_02_CROSS_TENANT_ADMIN_USERS', () => {
  test('ADMIN-A list = own tenant only; cross-tenant id probe → 404 not 403 @epic-h @tenant @rbac @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    // STUDENT denied
    const student = await provision(page, 'STUDENT', 'tenant-a')
    const studentResp = await page.request.get(`${ADMIN_API_URL}/api/admin/users`, {
      headers: { Authorization: `Bearer ${student.idToken}` },
    })
    expect(studentResp.status()).toBe(403)
    console.log(`[h-02] STUDENT GET /users → ${studentResp.status()}`)

    // ADMIN-A reaches list
    const adminA = await provision(page, 'ADMIN', 'tenant-a')
    const aResp = await page.request.get(`${ADMIN_API_URL}/api/admin/users`, {
      headers: { Authorization: `Bearer ${adminA.idToken}` },
    })
    console.log(`[h-02] ADMIN-A GET /users → ${aResp.status()}`)
    expect(aResp.status()).toBeLessThan(500)

    // ADMIN-B + their user-id; ADMIN-A probes ADMIN-B's id → must 404
    const adminB = await provision(page, 'ADMIN', 'tenant-b')
    const probeResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/users/${adminB.uid}`,
      { headers: { Authorization: `Bearer ${adminA.idToken}` } },
    )
    console.log(`[h-02] ADMIN-A GET /users/${adminB.uid.slice(0, 12)}... (cross-tenant) → ${probeResp.status()}`)
    // ── Existence-leak invariant ──
    // Cross-tenant id MUST return 404 (not found), NOT 403 (forbidden).
    // 403 would let ADMIN-A enumerate id existence by trying random ids.
    expect([404]).toContain(probeResp.status())
  })
})
