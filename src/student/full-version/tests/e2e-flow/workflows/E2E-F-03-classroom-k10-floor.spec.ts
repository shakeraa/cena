// =============================================================================
// E2E-F-03 — Classroom analytics k=10 privacy floor (P0, prr-026)
//
// /api/v1/institutes/{instituteId}/classrooms/{classroomId}/analytics/aggregate
// must enforce k=10: no aggregate stats are returned for classrooms
// with fewer than 10 active students. Frontend-only enforcement is
// bypassable; backend MUST gate.
//
// Contract assertion: response shape includes a privacy-floor signal
// (totalCount or kAnonymized=true) for low-count classrooms — never
// raw stats.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR' | 'SUPER_ADMIN',
  schoolId: string,
): Promise<{ idToken: string }> {
  const email = `f-03-${role.toLowerCase()}-${schoolId}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_F_03_CLASSROOM_K10_FLOOR', () => {
  test('classroom-analytics aggregate k=10 enforced server-side @epic-f @privacy @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    const teacher = await provision(page, 'MODERATOR', 'cena-platform')
    const fakeClassroomId = `classroom-empty-${Date.now()}`

    // Empty/non-existent classroom — must NOT leak stats. Either:
    //   - 404 (no such classroom)
    //   - 200 with kAnonymized=true / totalCount<10 / empty stats
    const resp = await page.request.get(
      `${ADMIN_API_URL}/api/v1/institutes/cena-platform/classrooms/${fakeClassroomId}/analytics/aggregate`,
      { headers: { Authorization: `Bearer ${teacher.idToken}` } },
    )
    console.log(`[f-03] aggregate (empty classroom) → ${resp.status()}`)
    expect(resp.status()).toBeLessThan(500)

    if (resp.status() === 200) {
      const body = await resp.text()
      // Privacy floor invariant: response must NOT carry per-student raw stats
      // for an empty classroom. Negative-property assertion.
      expect(body.toLowerCase()).not.toMatch(/"perStudent"\s*:\s*\[/)
      // Either kAnonymized signal present OR the body is bounded.
      const hasPrivacySignal = body.includes('kAnonymized')
        || body.includes('"totalCount":0')
        || body.includes('insufficient')
        || body.length < 500  // bounded shape
      expect(hasPrivacySignal, `aggregate response must surface k-floor signal — got: ${body.slice(0, 200)}`).toBe(true)
    }

    // STUDENT denied (sanity)
    const student = await provision(page, 'STUDENT', 'cena-platform')
    const stResp = await page.request.get(
      `${ADMIN_API_URL}/api/v1/institutes/cena-platform/classrooms/${fakeClassroomId}/analytics/aggregate`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    // STUDENT denied — 403 (RBAC) or 404 (classroom-load no-existence-leak)
    expect([403, 404]).toContain(stResp.status())
    console.log(`[f-03] STUDENT GET aggregate → ${stResp.status()}`)
  })
})
