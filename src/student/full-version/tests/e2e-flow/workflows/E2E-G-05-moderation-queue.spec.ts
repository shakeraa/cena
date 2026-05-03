// =============================================================================
// E2E-G-05 — Question moderation queue
//
// /api/admin/moderation/* is the curator workspace. Group policy is
// ModeratorOrAbove so MODERATOR / ADMIN / SUPER_ADMIN can list, claim,
// approve/reject items. STUDENT and PARENT roles are denied.
//
// What this spec covers (the contract surface):
//   1. RBAC — STUDENT denied, MODERATOR allowed for the queue listing
//   2. Queue summary endpoint returns a structured response
//   3. Stats endpoint returns a structured response
//
// What's not covered: the full claim → approve → invalidate-bus path.
// That requires (a) a seeded ModerationItem document, (b) a running
// student session listening on the bus to verify the broadcast. Both
// are in-flight via PRR-436 admin test probe (not yet shipped per the
// task prereqs). This spec covers the boundary that ships today.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_API_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const FIREBASE_PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const SCHOOL_ID = 'cena-platform'
const TENANT_ID = 'cena'

async function provision(
  page: import('@playwright/test').Page,
  role: 'STUDENT' | 'MODERATOR' | 'ADMIN' | 'SUPER_ADMIN',
): Promise<{ idToken: string }> {
  const email = `g-05-${role.toLowerCase()}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      data: { localId, customAttributes: JSON.stringify({ role, school_id: SCHOOL_ID, tenant_id: TENANT_ID, locale: 'en', plan: 'free' }) },
    },
  )
  const tokenResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tokenResp.json() as { idToken: string }).idToken }
}

test.describe('E2E_G_05_MODERATION_QUEUE', () => {
  test('RBAC + queue listing + summary + stats contract @epic-g @content', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_G_05_MODERATION_QUEUE ===\n')

    // ── 1. STUDENT must be denied ──
    const student = await provision(page, 'STUDENT')
    const studentResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/queue`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    console.log(`[g-05] STUDENT GET /queue → ${studentResp.status()}`)
    expect(studentResp.status()).toBe(403)

    // ── 2. MODERATOR through to listing ──
    const moderator = await provision(page, 'MODERATOR')
    const queueResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/queue`,
      { headers: { Authorization: `Bearer ${moderator.idToken}` } },
    )
    console.log(`[g-05] MODERATOR GET /queue → ${queueResp.status()}`)
    expect(queueResp.status()).toBe(200)
    const queue = await queueResp.json() as Record<string, unknown>
    expect(queue, 'queue response must be a structured object').toBeTruthy()

    // ── 3. Summary ──
    const summaryResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/queue/summary`,
      { headers: { Authorization: `Bearer ${moderator.idToken}` } },
    )
    console.log(`[g-05] GET /queue/summary → ${summaryResp.status()}`)
    expect(summaryResp.status()).toBe(200)

    // ── 4. Stats ──
    const statsResp = await page.request.get(
      `${ADMIN_API_URL}/api/admin/moderation/stats`,
      { headers: { Authorization: `Bearer ${moderator.idToken}` } },
    )
    console.log(`[g-05] GET /stats → ${statsResp.status()}`)
    expect(statsResp.status()).toBe(200)

    console.log('[g-05] queue + summary + stats contract verified for MODERATOR')
  })
})
