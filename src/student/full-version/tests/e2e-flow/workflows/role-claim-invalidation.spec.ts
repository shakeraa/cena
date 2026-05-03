// =============================================================================
// TASK-E2E-A-05 — Role-claim cache invalidation
//
// Designed journey (per EPIC-E2E-A): admin promotes student → /role
// endpoint hits Firebase Admin SDK → student's idToken refreshes within
// 2min → teacher-only routes accessible.
//
// What's verifiable today (live tests below):
//   * `POST /api/admin/users/{id}/role` exists and is `SuperAdminOnly`
//     — anonymous callers and non-superadmin callers are rejected
//   * The endpoint integrates Firebase claim push (covered by the
//     full-flow fixme below, since the AdminUser bootstrap path for
//     a fresh STUDENT-role user is not exercised by current seed)
//
// Why the full flow is `test.fixme`: AdminRoleService.AssignRoleToUserAsync
// expects an `AdminUser` Marten doc with id == Firebase uid. The current
// `/api/admin/me` lazy-bootstrap requires an ADMIN-or-above caller, so a
// freshly created STUDENT user has no AdminUser row → /role returns 404.
// Closing this requires either a bootstrap that runs on STUDENT first
// sign-in (couples nicely with TASK-E2E-A-01-BE-01 `on-first-sign-in`)
// or an explicit admin-side `POST /api/admin/users` precursor that the
// test seeds.
//
// Once that lands, the fixme test should:
//   1. Create student via Firebase emu
//   2. Either: bootstrap AdminUser doc, or use a pre-existing seeded
//      student that has been "touched" by an admin path
//   3. Sign in as super-admin (admin@cena.local), POST /role with TEACHER
//   4. Force refresh student's idToken (signInWithPassword again)
//   5. Decode JWT, assert role=TEACHER within 2min
//   6. Optionally drive the SPA: assert teacher-only nav appears for
//      the now-promoted student session
// =============================================================================

import { test, expect } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const ADMIN_API_BASE_URL = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'

const SEEDED_SUPER_ADMIN_EMAIL = 'admin@cena.local'
const SEEDED_SUPER_ADMIN_PASSWORD = 'DevAdmin123!'
const SEEDED_TEACHER_EMAIL = 'teacher1@cena.local'
const SEEDED_TEACHER_PASSWORD = 'DevTeacher123!'

async function emuSignIn(email: string, password: string): Promise<string> {
  const resp = await fetch(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, returnSecureToken: true }),
    },
  )
  if (!resp.ok)
    throw new Error(`Firebase emu signIn failed for ${email}: ${resp.status} ${await resp.text()}`)
  const body = await resp.json() as { idToken: string }
  return body.idToken
}

test.describe('E2E-A-05 role-claim cache invalidation', () => {
  test('admin role endpoint requires authentication @auth @p0', async ({ page }) => {
    // Calling without Authorization header must NOT promote anyone.
    // Anything < 400 here is a critical privilege-escalation regression.
    const resp = await page.request.post(
      `${ADMIN_API_BASE_URL}/api/admin/users/some-uid/role`,
      {
        data: { Role: 'TEACHER' },
        headers: { 'Content-Type': 'application/json' },
      },
    )
    expect(
      resp.status(),
      `Unauthenticated POST /api/admin/users/{id}/role must reject; got ${resp.status()}`,
    ).toBeGreaterThanOrEqual(400)
  })

  test('admin role endpoint requires SUPER_ADMIN (TEACHER rejected) @auth @p0', async ({ page }) => {
    // Sign in as a TEACHER — must NOT be allowed to promote anyone.
    // Per FIND-sec-010, only SUPER_ADMIN can hit this endpoint.
    const teacherToken = await emuSignIn(SEEDED_TEACHER_EMAIL, SEEDED_TEACHER_PASSWORD)
    const resp = await page.request.post(
      `${ADMIN_API_BASE_URL}/api/admin/users/some-uid/role`,
      {
        data: { Role: 'TEACHER' },
        headers: {
          Authorization: `Bearer ${teacherToken}`,
          'Content-Type': 'application/json',
        },
      },
    )
    // Expected: 401 (token rejected at policy gate) or 403 (forbidden).
    // 200/204 here = privilege escalation regression — escalate immediately.
    expect(
      resp.status(),
      `TEACHER must NOT be able to promote users; got ${resp.status()} (expected 401/403)`,
    ).toBeGreaterThanOrEqual(400)
    expect(
      resp.status(),
      'TEACHER call must not crash the server (got 5xx — handler error path regression)',
    ).toBeLessThan(500)
  })

  test('admin role endpoint accepts SUPER_ADMIN, returns 404 for unknown user @auth @p1', async ({ page }) => {
    // SUPER_ADMIN is allowed past the policy gate; the unknown-uid case
    // must return 404 (handler handles KeyNotFoundException). 5xx here
    // means the handler regressed (uncaught exception path).
    const adminToken = await emuSignIn(SEEDED_SUPER_ADMIN_EMAIL, SEEDED_SUPER_ADMIN_PASSWORD)
    const resp = await page.request.post(
      `${ADMIN_API_BASE_URL}/api/admin/users/uid-that-does-not-exist-${Date.now()}/role`,
      {
        data: { Role: 'TEACHER' },
        headers: {
          Authorization: `Bearer ${adminToken}`,
          'Content-Type': 'application/json',
        },
      },
    )
    expect(
      resp.status(),
      `SUPER_ADMIN promote of unknown user must be 404, got ${resp.status()}`,
    ).toBe(404)
  })

  test.fixme(
    'full flow: admin promotes student → claims refresh < 2min → teacher route accessible @auth @p0 BLOCKED_ON: AdminUser bootstrap for STUDENT role',
    async () => {
      // Intentionally empty — see file header for the unbuilt path.
      // Implement once an AdminUser doc is auto-bootstrapped for STUDENT
      // users (likely via TASK-E2E-A-01-BE-01 on-first-sign-in handler
      // or an explicit POST /api/admin/users invite).
    },
  )
})
