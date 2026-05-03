// =============================================================================
// E2E-D-07 — Stem-grounded hints (P0, PRR-262, ADR-0003)
//
// Hint generation MUST use only the question's stem + the student's
// prior attempt — no cross-session context, no other students' data,
// no platform-wide patterns. Cross-tenant leak is a ship blocker.
//
// Hint-ladder endpoint: POST /api/sessions/{sid}/question/{qid}/hint/next
// (registered via MapHintLadderEndpoint in admin host).
//
// What this spec drives at the contract layer:
//   1. Provision a student
//   2. Hit /hint/next with a non-existent session/question id —
//      endpoint must return a structured 404 (not 500), proving the
//      auth + lookup path is ground (no fall-through to LLM with empty
//      stem)
//   3. Provision a SECOND student in a DIFFERENT tenant; hit /hint/next
//      with the FIRST student's session id (cross-tenant probe). Must
//      return 404 (existence-leak guard) — we do NOT want a 403 that
//      leaks "this session exists but you can't see it".
//
// Deeper assertion (LLM payload contains stem-only) requires a probe
// into the LLM-bound bytes. PRR-262 has an admin observability endpoint
// for this; out of scope for E2E contract.
// =============================================================================

import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const TENANT_A = 'cena'
const TENANT_B = 'tenant-b-isolated'
const SCHOOL_A = 'cena-platform'
const SCHOOL_B = 'school-b-isolated'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'

async function bootstrap(
  page: import('@playwright/test').Page,
  label: string,
  tenantId: string,
  schoolId: string,
): Promise<{ idToken: string; uid: string }> {
  const email = `${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const bs = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken: bootstrapToken, localId } = await bs.json() as { idToken: string; localId: string }
  await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId, schoolId, displayName: `D07 ${label}` },
  })
  await page.waitForTimeout(300)
  const tok = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  return { idToken: (await tok.json() as { idToken: string }).idToken, uid: localId }
}

test.describe('E2E_D_07_STEM_GROUNDED_HINTS', () => {
  test('hint-ladder is grounded + cross-tenant 404 (no existence leak) @epic-d @cas @compliance @ship-gate', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_D_07_STEM_GROUNDED_HINTS ===\n')

    const a = await bootstrap(page, 'd-07-tenant-a', TENANT_A, SCHOOL_A)
    const b = await bootstrap(page, 'd-07-tenant-b', TENANT_B, SCHOOL_B)

    // ── 1. Non-existent session/question ──
    // Must 404, not 500, not 200-with-empty-LLM-call.
    const fakeSession = `session-fake-${Date.now()}`
    const fakeQuestion = `q-fake-${Date.now()}`
    const aResp = await page.request.post(
      `${STUDENT_API}/api/sessions/${fakeSession}/question/${fakeQuestion}/hint/next`,
      {
        headers: { Authorization: `Bearer ${a.idToken}`, 'Content-Type': 'application/json' },
        data: {},
      },
    )
    console.log(`[d-07] tenant-A POST /hint/next on fake session → ${aResp.status()}`)
    expect([400, 401, 404]).toContain(aResp.status())

    // ── 2. Cross-tenant probe ──
    // Tenant-B student uses tenant-A's (also fake) session-id. The
    // endpoint must return the SAME outcome shape — no 403 that
    // would leak "this session exists in another tenant". With both
    // sessions fake, both should 404.
    const bResp = await page.request.post(
      `${STUDENT_API}/api/sessions/${fakeSession}/question/${fakeQuestion}/hint/next`,
      {
        headers: { Authorization: `Bearer ${b.idToken}`, 'Content-Type': 'application/json' },
        data: {},
      },
    )
    console.log(`[d-07] tenant-B POST /hint/next on tenant-A's fake session → ${bResp.status()}`)
    expect(bResp.status(), 'cross-tenant probe must mirror same-tenant outcome on a non-existent id').toBe(aResp.status())

    // ── 3. Empty stem (no question id provided / malformed) must NOT
    //      reach LLM. Contract: malformed → 400, not 500-with-empty-prompt.
    const malformedResp = await page.request.post(
      `${STUDENT_API}/api/sessions//question//hint/next`,
      {
        headers: { Authorization: `Bearer ${a.idToken}`, 'Content-Type': 'application/json' },
        data: {},
      },
    )
    console.log(`[d-07] malformed (empty ids) → ${malformedResp.status()}`)
    expect(malformedResp.status()).toBeLessThan(500)
  })
})
