// =============================================================================
// TASK-E2E-A-04 — Parent ↔ child binding (prr-009)
//
// Designed journey (per EPIC-E2E-A): existing student logs in → parent
// receives bind-invite email → parent clicks /parent/bind?token=... →
// confirms kinship → parent-side dashboard shows child.
//
// Status of the underlying backend (verified 2026-04-27):
//   * `/parent/bind?token=...` SPA route — does NOT exist
//   * `POST /api/parent/bind/{token}` student-api endpoint — does NOT exist
//   * `ParentChildBoundV1` event — not in `grep` results
//   * The `ParentChildBindingService` exists in actor-host (writes
//     `ParentChildBindingDocument` rows) but the *invite + accept* edge is
//     unbuilt — bindings today are administered, not parent-driven.
//
// What this spec covers today:
//   1. (skipped, planned) The full invite → accept → dashboard journey
//      — kept as `test.fixme` so it appears in the report and forces a
//      review when the backend lands.
//   2. (live) `GET /api/me/parent-dashboard` shape smoke for the seeded
//      `parent1@cena.local` user. This catches: endpoint dropped from
//      build, auth shape regressions, JSON contract drift. It does NOT
//      assert specific child data because the seed does not write a
//      ParentChildBinding Marten row — that's the gap A-04 will close.
//
// When the bind endpoint ships:
//   * Remove the `test.fixme` and implement against the new contract
//   * Drop the shape smoke (it'll be subsumed by the full journey)
//   * Add a 409-on-second-click idempotency assertion
//   * Add a tenant-mismatch rejection assertion (cross-institute invite)
// =============================================================================

import { test, expect } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API_BASE_URL = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

const SEEDED_PARENT_EMAIL = 'parent1@cena.local'
const SEEDED_PARENT_PASSWORD = 'DevParent123!'

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

test.describe('E2E-A-04 parent ↔ child binding', () => {
  // TASK-E2E-A-04-BE shipped the issuance + consume endpoints. The flagship
  // journey now drives the real backend:
  //   POST /api/me/parent-bind-invite as the student (returns token)
  //   POST /api/parent/bind/{token}    as the parent (200 + binding written + bus event)
  //   POST /api/parent/bind/{token}    second call -> 409 (replay defence)
  //   POST /api/parent/bind/{token}    with parent in wrong tenant -> 403
  test('invite -> /api/parent/bind/{token} -> binding written + 409 on replay @auth @p0', async ({ page }) => {
    // Both seeded users land in the same institute (`cena-platform`) so the
    // happy-path flow works without manual provisioning. Cross-institute
    // rejection is asserted via a separate spec (the dev seed does not
    // create a second institute today; that scenario is owned by the
    // tenancy-isolation epic, not this auth-onboarding spec).
    const studentId = await emuSignIn('student1@cena.local', 'DevStudent123!')
    expect(studentId).toBeTruthy()

    // ── 1. Student issues an invite for the seeded parent's email ──
    const issueResp = await page.request.post(
      `${STUDENT_API_BASE_URL}/api/me/parent-bind-invite`,
      {
        headers: { Authorization: `Bearer ${studentId}` },
        data: { parentEmail: SEEDED_PARENT_EMAIL, relationship: 'parent' },
      },
    )
    expect(
      issueResp.status(),
      'POST /api/me/parent-bind-invite must return 200 once the student is signed in',
    ).toBe(200)
    const invite = await issueResp.json() as {
      token: string
      jti: string
      expiresAt: string
    }
    expect(invite.token, 'issue response must carry the signed JWT').toBeTruthy()
    expect(invite.jti, 'issue response must carry the jti').toBeTruthy()

    // ── 2. Parent signs in and consumes the invite ──
    const parentToken = await emuSignIn(SEEDED_PARENT_EMAIL, SEEDED_PARENT_PASSWORD)

    const consumeResp = await page.request.post(
      `${STUDENT_API_BASE_URL}/api/parent/bind/${invite.token}`,
      { headers: { Authorization: `Bearer ${parentToken}` } },
    )
    expect(
      consumeResp.status(),
      'POST /api/parent/bind/{token} must return 200 on first consume',
    ).toBe(200)
    const bound = await consumeResp.json() as {
      parentUid: string
      studentSubjectId: string
      tenantId: string
      relationship: string
      inviteJti: string
    }
    expect(bound.inviteJti).toBe(invite.jti)
    expect(bound.relationship).toBe('parent')

    // ── 3. Replay defence: same token, second consume -> 409 ──
    const replayResp = await page.request.post(
      `${STUDENT_API_BASE_URL}/api/parent/bind/${invite.token}`,
      { headers: { Authorization: `Bearer ${parentToken}` } },
    )
    expect(
      replayResp.status(),
      'second consume of the same token must return 409 (already consumed)',
    ).toBe(409)
  })

  test('parent-dashboard endpoint shape smoke (seeded parent) @auth @p1', async ({ page }) => {
    // The seed creates parent1 in Firebase but does NOT write a
    // ParentChildBinding Marten row. The dashboard endpoint must still
    // respond cleanly — empty, tier-gated, or with bound children if a
    // separate seeder ran. This test catches API contract regressions
    // independent of the binding backfill.
    const idToken = await emuSignIn(SEEDED_PARENT_EMAIL, SEEDED_PARENT_PASSWORD)

    const resp = await page.request.get(
      `${STUDENT_API_BASE_URL}/api/me/parent-dashboard`,
      { headers: { Authorization: `Bearer ${idToken}` } },
    )

    // 200 — endpoint live, returns dashboard payload (possibly empty)
    // 403 — tier-gate (parent has no Premium subscription); UI shows upsell
    // 401/404/500 — regression: endpoint dropped, auth shape changed, or
    //               unhandled exception in the read path
    expect(
      [200, 403].includes(resp.status()),
      `GET /api/me/parent-dashboard must be 200 or 403 (tier-gate); got ${resp.status()} — ` +
      'endpoint regressed or auth shape changed for seeded parent.',
    ).toBe(true)

    if (resp.status() === 200) {
      const body = await resp.json() as {
        students?: unknown
        householdMinutesWeekly?: number
        householdMinutesMonthly?: number
        generatedAt?: string
      }
      expect(Array.isArray(body.students), 'students must be an array').toBe(true)
      expect(typeof body.householdMinutesWeekly, 'householdMinutesWeekly is a number').toBe('number')
      expect(typeof body.householdMinutesMonthly, 'householdMinutesMonthly is a number').toBe('number')
      expect(body.generatedAt, 'generatedAt timestamp present').toBeTruthy()
    }
  })

  test('parent-dashboard rejects unauthenticated calls @auth @p1', async ({ page }) => {
    const resp = await page.request.get(
      `${STUDENT_API_BASE_URL}/api/me/parent-dashboard`,
    )
    // No Authorization header — must NOT be 200. Any 2xx here = auth bypass.
    expect(
      resp.status(),
      'Unauthenticated /api/me/parent-dashboard must reject (401/403); a 2xx is an auth bypass',
    ).toBeGreaterThanOrEqual(400)
  })
})
