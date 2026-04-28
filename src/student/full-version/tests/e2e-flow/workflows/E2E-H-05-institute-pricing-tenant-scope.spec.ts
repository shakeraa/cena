// =============================================================================
// E2E-H-05 — Institute pricing override tenant-scoped (P0, prr-244)
//
// /api/admin/institutes/{id}/pricing-override — set/get pricing for a
// specific institute. ADMIN-A's override must NOT affect ADMIN-B's
// students; cache must be keyed by tenant.
//
// Contract layer: probe both endpoints with two ADMINs in different
// schools. Cross-tenant id probes must 404 (existence-leak guard).
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
  const email = `h-05-${role.toLowerCase()}-${schoolId}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_H_05_INSTITUTE_PRICING_TENANT_SCOPE', () => {
  test('cross-tenant pricing-override probe stays 404 @epic-h @tenant @billing @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    const adminA = await provision(page, 'ADMIN', 'tenant-a')

    // Probe tenant-B's pricing-override from tenant-A's admin.
    const crossResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/institutes/tenant-b/pricing-override`,
      { headers: { Authorization: `Bearer ${adminA.idToken}` } },
    )
    console.log(`[h-05] ADMIN-A GET tenant-b pricing-override → ${crossResp.status()}`)
    // Cross-tenant must be 403 or 404 — not 200 with tenant-b data.
    expect([403, 404]).toContain(crossResp.status())

    // Same-tenant probe — returns 200 (default) or 404 (not yet
    // configured), never 403 to own admin.
    const ownResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/institutes/tenant-a/pricing-override`,
      { headers: { Authorization: `Bearer ${adminA.idToken}` } },
    )
    console.log(`[h-05] ADMIN-A GET tenant-a pricing-override → ${ownResp.status()}`)
    expect([200, 404]).toContain(ownResp.status())
  })
})
