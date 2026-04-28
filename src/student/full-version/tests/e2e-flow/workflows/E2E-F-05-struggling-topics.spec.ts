// =============================================================================
// E2E-F-05 — Struggling-topics surface (P1, ship-gate prr-049)
//
// /api/v1/institutes/{instituteId}/classrooms/{classroomId}/teacher-dashboard
// returns a struggling-topics array. Ship-gate: NO banned engagement
// copy (streak, etc.) in the response — caught by GD-004 scanner CI
// but worth its own contract assertion here.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR',
  schoolId: string,
): Promise<{ idToken: string }> {
  const email = `f-05-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_F_05_STRUGGLING_TOPICS', () => {
  test('teacher-dashboard surface bounded; no banned engagement copy @epic-f @teacher @ship-gate', async ({ page }) => {
    test.setTimeout(60_000)

    // STUDENT denied
    const student = await provision(page, 'STUDENT', 'cena-platform')
    const stResp = await page.request.get(
      `${ADMIN_API_URL}/api/v1/institutes/cena-platform/classrooms/fake-classroom/teacher-dashboard`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect([403, 404]).toContain(stResp.status())
    console.log(`[f-05] STUDENT → ${stResp.status()}`)

    // MODERATOR through
    const teacher = await provision(page, 'MODERATOR', 'cena-platform')
    const resp = await page.request.get(
      `${ADMIN_API_URL}/api/v1/institutes/cena-platform/classrooms/fake-classroom/teacher-dashboard`,
      { headers: { Authorization: `Bearer ${teacher.idToken}` } },
    )
    console.log(`[f-05] MODERATOR teacher-dashboard → ${resp.status()}`)
    expect(resp.status()).toBeLessThan(500)

    if (resp.status() === 200) {
      const body = await resp.text()
      // ── Ship-gate GD-004 invariant ──
      // No banned engagement copy on this surface.
      const banned = ['streak', 'days in a row', 'don\'t lose your', 'keep your streak']
      for (const term of banned) {
        expect(body.toLowerCase()).not.toContain(term)
      }
      console.log('[f-05] ship-gate verified: no banned engagement copy in teacher-dashboard')
    }
  })
})
