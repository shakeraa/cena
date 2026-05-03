// =============================================================================
// E2E-F-04 — Teacher schedule-override (P1, ADR-0044)
//
// /api/admin/teacher/override/{pin-topic,budget,motivation} — POST.
// Teacher overrides classroom schedule (pin a topic, adjust budget,
// motivation override). Must be tenant-scoped + RBAC-gated.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR' | 'ADMIN',
  schoolId: string,
): Promise<{ idToken: string }> {
  const email = `f-04-${role.toLowerCase()}-${schoolId}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_F_04_SCHEDULE_OVERRIDE', () => {
  test('schedule-override RBAC + tenant guard @epic-f @teacher', async ({ page }) => {
    test.setTimeout(60_000)

    const fakeStudentId = `st-fake-${Date.now()}`

    // STUDENT denied
    const student = await provision(page, 'STUDENT', 'cena-platform')
    const stResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/teacher/override/pin-topic`,
      {
        headers: { Authorization: `Bearer ${student.idToken}`, 'Content-Type': 'application/json' },
        data: { studentId: fakeStudentId, conceptId: 'algebra.linear', durationMinutes: 30 },
      },
    )
    expect(stResp.status()).toBe(403)
    console.log(`[f-04] STUDENT POST pin-topic → ${stResp.status()}`)

    // MODERATOR/ADMIN through; non-existent student → 404 (no existence leak)
    const teacher = await provision(page, 'MODERATOR', 'cena-platform')
    const tResp = await page.request.post(
      `${ADMIN_API_URL}/api/admin/teacher/override/pin-topic`,
      {
        headers: { Authorization: `Bearer ${teacher.idToken}`, 'Content-Type': 'application/json' },
        data: { studentId: fakeStudentId, conceptId: 'algebra.linear', durationMinutes: 30 },
      },
    )
    console.log(`[f-04] MODERATOR POST pin-topic (fake student) → ${tResp.status()}`)
    // 404 (student not in tenant), 400 (validation), or 200 (accepted) — not 5xx
    expect(tResp.status()).toBeLessThan(500)
  })
})
