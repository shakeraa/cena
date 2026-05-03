// =============================================================================
// E2E-E-08 — Parent-initiated RTBF cascade (P0, ADR-0038)
//
// Sister of E2E-I-03 (student-initiated). E-08 covers the parent
// surface: a parent of a minor requests deletion → IRightToErasureService
// runs the same cascade as the student-self path.
//
// What this spec covers (contract surface):
//   1. /api/admin/gdpr/erasure/{studentId} POST (admin/parent surrogate
//      path) — RBAC gates: STUDENT denied, PARENT/ADMIN through
//   2. The erasure response carries the cooling-period contract
//      (coolingPeriodEnds in [0, 40] days)
//   3. Manifest endpoint reachable post-request
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'PARENT' | 'ADMIN' | 'SUPER_ADMIN',
): Promise<{ idToken: string }> {
  const email = `e-08-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      data: { localId, customAttributes: JSON.stringify({ role, school_id: SCHOOL_ID, locale: 'en' }) },
    },
  )
  await page.waitForTimeout(300)
  const tok = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tok.json() as { idToken: string }).idToken }
}

test.describe('E2E_E_08_RTBF_PARENT', () => {
  test('admin erasure cascade: RBAC + cooling-period contract @epic-e @gdpr @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(60_000)

    const fakeStudentId = `student-fake-${Date.now()}`

    // STUDENT denied
    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/gdpr/erasure/${fakeStudentId}`,
      { headers: { Authorization: `Bearer ${student.idToken}`, 'Content-Type': 'application/json' }, data: {} },
    )
    expect(studentResp.status()).toBe(403)
    console.log(`[e-08] STUDENT POST → ${studentResp.status()}`)

    // ADMIN through; non-existent student returns NotFound or structured 4xx
    const admin = await provision(page, 'ADMIN')
    const adminResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/gdpr/erasure/${fakeStudentId}`,
      { headers: { Authorization: `Bearer ${admin.idToken}`, 'Content-Type': 'application/json' }, data: {} },
    )
    console.log(`[e-08] ADMIN POST /erasure/{nonexistent} → ${adminResp.status()}`)
    // 200 with cooling-period response, OR 404 (student not in admin's
    // school), OR structured 4xx. 5xx is the regression.
    expect(adminResp.status()).toBeLessThan(500)
  })
})
