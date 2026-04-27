// =============================================================================
// E2E-G-06 — Cultural-context review board DLQ (prr-034)
//
// GET /api/admin/moderation/cultural-context-dlq lists items the LLM
// flagged for cultural-context review. Group policy is ModeratorOrAbove
// inside the moderation route group; tenant scoping happens server-side
// via TenantScope (SUPER_ADMIN sees all; ADMIN/MODERATOR scoped to
// their school).
//
// What this spec covers:
//   1. RBAC — STUDENT denied 403; MODERATOR through to listing
//   2. Pagination guards — invalid page/pageSize values clamped server-side
//   3. ADR-0043 ship-gate analogue — flagged content is NOT served to
//      students (the listing returns moderator-facing structure only)
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR' | 'SUPER_ADMIN',
): Promise<{ idToken: string }> {
  const email = `g-06-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_G_06_CULTURAL_DLQ', () => {
  test('RBAC + pagination contract @epic-g @content', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_G_06_CULTURAL_DLQ ===\n')

    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/cultural-context-dlq`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect(studentResp.status()).toBe(403)
    console.log(`[g-06] STUDENT → ${studentResp.status()}`)

    const mod = await provision(page, 'MODERATOR')
    const modResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/cultural-context-dlq?page=1&pageSize=10`,
      { headers: { Authorization: `Bearer ${mod.idToken}` } },
    )
    expect(modResp.status()).toBe(200)
    console.log(`[g-06] MODERATOR → ${modResp.status()}`)

    // Pagination guard: pageSize=999 must NOT 500. Either 200 (clamped
    // server-side) or 400 (rejected with a structured error) is fine —
    // the only failure mode is silent crash.
    const clampResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/cultural-context-dlq?page=1&pageSize=999`,
      { headers: { Authorization: `Bearer ${mod.idToken}` } },
    )
    console.log(`[g-06] MODERATOR pageSize=999 → ${clampResp.status()}`)
    expect([200, 400]).toContain(clampResp.status())
  })
})
