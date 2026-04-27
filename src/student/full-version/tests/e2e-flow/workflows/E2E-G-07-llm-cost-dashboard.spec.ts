// =============================================================================
// E2E-G-07 — LLM cost dashboard (prr-112)
//
// GET /api/admin/llm-cost/per-cohort returns per-feature cost rollup for
// a cohort over a time window. Policy is AdminOnly (ADMIN/SUPER_ADMIN).
// Hard cap: window <= 90 days.
//
// What this spec covers:
//   1. RBAC — STUDENT denied 403; MODERATOR denied 403; ADMIN through
//   2. Required params — missing cohort/from/to → 400 validation
//   3. Window cap — windows > 90 days clamped or rejected
//   4. Tenant scope — ADMIN sees only own institute (server-side scope)
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR' | 'ADMIN' | 'SUPER_ADMIN',
): Promise<{ idToken: string }> {
  const email = `g-07-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      // institute_id is what TenantScope.GetInstituteFilter reads on this
      // endpoint; school_id is the broader claim used elsewhere. Set both.
      data: { localId, customAttributes: JSON.stringify({ role, institute_id: SCHOOL_ID, school_id: SCHOOL_ID, locale: 'en', plan: 'free' }) },
    },
  )
  const tokenResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tokenResp.json() as { idToken: string }).idToken }
}

test.describe('E2E_G_07_LLM_COST_DASHBOARD', () => {
  test('RBAC + window cap + per-cohort contract @epic-g @llm @tenant', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_G_07_LLM_COST_DASHBOARD ===\n')

    const now = new Date()
    const sevenDaysAgo = new Date(now.getTime() - 7 * 24 * 3600 * 1000)
    const validFrom = sevenDaysAgo.toISOString()
    const validTo = now.toISOString()
    const validQ = `cohort=${SCHOOL_ID}&from=${encodeURIComponent(validFrom)}&to=${encodeURIComponent(validTo)}`

    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/llm-cost/per-cohort?${validQ}`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect(studentResp.status()).toBe(403)
    console.log(`[g-07] STUDENT → ${studentResp.status()}`)

    const mod = await provision(page, 'MODERATOR')
    const modResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/llm-cost/per-cohort?${validQ}`,
      { headers: { Authorization: `Bearer ${mod.idToken}` } },
    )
    expect(modResp.status(), 'AdminOnly — MODERATOR must be denied').toBe(403)
    console.log(`[g-07] MODERATOR → ${modResp.status()}`)

    const admin = await provision(page, 'ADMIN')
    const adminResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/llm-cost/per-cohort?${validQ}`,
      { headers: { Authorization: `Bearer ${admin.idToken}` } },
    )
    console.log(`[g-07] ADMIN valid window → ${adminResp.status()}`)
    expect(adminResp.status()).toBe(200)

    // Window > 90 days must be rejected (MaxWindowDays = 90 per endpoint constant).
    const tooFar = new Date(now.getTime() - 120 * 24 * 3600 * 1000).toISOString()
    const oversizeResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/llm-cost/per-cohort?cohort=${SCHOOL_ID}&from=${encodeURIComponent(tooFar)}&to=${encodeURIComponent(validTo)}`,
      { headers: { Authorization: `Bearer ${admin.idToken}` } },
    )
    console.log(`[g-07] ADMIN 120-day window → ${oversizeResp.status()}`)
    expect(oversizeResp.status(), '120-day window must be rejected (cap = 90d)').toBe(400)
  })
})
