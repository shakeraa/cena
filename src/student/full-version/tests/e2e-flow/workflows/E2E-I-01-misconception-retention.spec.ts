// =============================================================================
// E2E-I-01 — Misconception 30-day TTL retention (ADR-0003 Decision 2) — P0
//
// The /api/admin/compliance/data-retention endpoint is the external-auditor
// surface for proving the platform's commitment to per-session misconception
// scope + 30-day TTL. This spec verifies:
//   1. RBAC: SuperAdminOnly (others denied 403)
//   2. The policies array includes the SessionMisconception category with
//      retentionDays = 30 — auditable proof of the documented constant
//   3. Enforcement state shape is structured (isEnforced/isRunning/hasEverRun)
//
// Why we don't fast-forward IClock here: the retention worker runs nightly
// and persists RetentionRunHistory rows. Asserting the actual prune via
// IClock would require invasive test seams in the worker. The
// auditor-facing surface (this endpoint) is what ships and what the
// 30-day commitment is judged against.
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
  const email = `i-01-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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

test.describe('E2E_I_01_MISCONCEPTION_RETENTION', () => {
  test('30-day TTL exposed on auditor surface @epic-i @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(60_000)
    console.log('\n=== E2E_I_01_MISCONCEPTION_RETENTION ===\n')

    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/data-retention`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect(studentResp.status()).toBe(403)
    console.log(`[i-01] STUDENT → ${studentResp.status()}`)

    const admin = await provision(page, 'ADMIN')
    const adminResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/data-retention`,
      { headers: { Authorization: `Bearer ${admin.idToken}` } },
    )
    expect(adminResp.status(), 'compliance endpoint is SuperAdminOnly — ADMIN denied').toBe(403)
    console.log(`[i-01] ADMIN → ${adminResp.status()}`)

    const su = await provision(page, 'SUPER_ADMIN')
    const suResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/compliance/data-retention`,
      { headers: { Authorization: `Bearer ${su.idToken}` } },
    )
    expect(suResp.status()).toBe(200)

    interface PolicyEntry { category: string; retentionDays: number; description: string }
    interface Resp { policies: PolicyEntry[]; enforcement: Record<string, unknown> }
    const body = await suResp.json() as Resp
    console.log(`[i-01] policies: ${body.policies.map(p => `${p.category}=${p.retentionDays}d`).join(', ')}`)

    // ── ADR-0003 Decision 2: the 30-day TTL must be auditor-readable ──
    const misc = body.policies.find(p =>
      p.category.toLowerCase().includes('misconception')
      || p.category.toLowerCase().includes('session misconception'))
    expect(misc, 'Session Misconception retention policy must be exposed on the compliance surface').toBeDefined()
    expect(misc!.retentionDays, 'misconception retention must be exactly 30 days per ADR-0003').toBe(30)

    // Enforcement shape (regardless of whether worker has run)
    expect(body.enforcement).toBeTruthy()
    expect(body.enforcement).toHaveProperty('isEnforced')
    expect(body.enforcement).toHaveProperty('isRunning')
    expect(body.enforcement).toHaveProperty('hasEverRun')
    console.log(`[i-01] enforcement: ${JSON.stringify(body.enforcement)}`)
  })
})
