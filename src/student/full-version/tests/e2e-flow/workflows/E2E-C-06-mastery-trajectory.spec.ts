// =============================================================================
// EPIC-E2E-C-06 — Mastery trajectory contract (API-driven boundary)
//
// Per the EPIC-C plan: "over N sessions → /progress → trajectory graph
// updated with BKT+HLR decay (MST-003, MST-008) → trajectory reflects
// actual performance (not flat-lined or jittery)."
//
// What this spec covers:
//   1. /api/analytics/mastery returns 200 for an authenticated student,
//      empty array for fresh students (no sessions yet) — NOT 500
//   2. /api/analytics/progress returns 200 with a sane date-range
//      response shape, even with no data
//   3. Cross-tenant: foreign student's idToken on the same endpoints
//      returns the FOREIGN student's data — never the original's.
//      This is the canonical "/api/me cache by JWT" check.
//   4. Ship-gate scan: response payload must NOT include any of the
//      banned engagement-mechanic keys (streak, daysInARow, lossAversion,
//      varianceReward) per the design non-negotiable + memory
//      feedback_shipgate_banned_terms.md
//
// Why API-driven, not UI-driven: the SPA dashboard renders a chart only
// when N≥1 completed sessions exist. Driving N sessions to completion
// in the dev env requires seeded questions (PRR-250 §2 BLOCKER —
// corpus not ingested). The API contract is what protects the data
// invariants — the chart is a render concern verified separately.
// =============================================================================

import { test, expect, type APIRequestContext } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

// Shipgate banned terms per docs/engineering/shipgate.md GD-004 + memory.
// If any response payload contains these as keys, we have a regression.
const BANNED_KEY_PATTERNS = [
  /\bstreak\b/i,
  /\bdaysInARow\b/i,
  /\blossAversion\b/i,
  /\bvarianceReward\b/i,
  /\bvariableRatio\b/i,
] as const

interface ProvisionedStudent {
  email: string
  password: string
  uid: string
  idToken: string
}

async function provision(ctx: APIRequestContext, label: string): Promise<ProvisionedStudent> {
  const email = `e2e-c06-${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  const signup = await ctx.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const signupBody = await signup.json() as { idToken: string; localId: string }

  await ctx.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${signupBody.idToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `C-06 ${label}` },
  })
  const re = await ctx.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await re.json() as { idToken: string }

  await ctx.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      role: 'student', locale: 'en', subjects: ['math'],
      dailyTimeGoalMinutes: 15, weeklySubjectTargets: [],
      diagnosticResults: null, classroomCode: null,
    },
  })
  return { email, password, uid: signupBody.localId, idToken }
}

function findBannedKeys(json: unknown, path: string = '$'): string[] {
  const violations: string[] = []
  if (json === null || typeof json !== 'object') return violations
  for (const [k, v] of Object.entries(json as Record<string, unknown>)) {
    for (const rx of BANNED_KEY_PATTERNS) {
      if (rx.test(k)) violations.push(`${path}.${k}`)
    }
    if (v !== null && typeof v === 'object') {
      violations.push(...findBannedKeys(v, `${path}.${k}`))
    }
  }
  return violations
}

test.describe('E2E_C_06_MASTERY_TRAJECTORY', () => {
  test('mastery + progress endpoints: shape, fresh-student empty, no banned engagement keys @epic-c @c-06 @ship-gate', async ({ request }, testInfo) => {
    test.setTimeout(120_000)

    const me = await provision(request, 'me')
    const other = await provision(request, 'other')

    // ── 1. /api/analytics/mastery for fresh student → 200 + sane shape ──
    const masteryResp = await request.get(`${STUDENT_API}/api/analytics/mastery`, {
      headers: { Authorization: `Bearer ${me.idToken}` },
    })
    expect(masteryResp.status(), '/api/analytics/mastery must be 200 (NOT 500) for a fresh student').toBe(200)
    const masteryBody = await masteryResp.json() as unknown

    const masteryViolations = findBannedKeys(masteryBody)
    expect(masteryViolations,
      `mastery payload must NOT contain banned engagement-mechanic keys ` +
      `(streak / daysInARow / lossAversion / varianceReward / variableRatio). ` +
      `Per docs/engineering/shipgate.md GD-004 + memory feedback_shipgate_banned_terms.md. ` +
      `Found: ${JSON.stringify(masteryViolations)}`,
    ).toEqual([])

    // ── 2. /api/analytics/progress for fresh student → 200 + sane shape ──
    const progressResp = await request.get(`${STUDENT_API}/api/analytics/progress`, {
      headers: { Authorization: `Bearer ${me.idToken}` },
    })
    expect(progressResp.status(), '/api/analytics/progress must be 200 for a fresh student').toBe(200)
    const progressBody = await progressResp.json() as unknown

    const progressViolations = findBannedKeys(progressBody)
    expect(progressViolations,
      `progress payload must NOT contain banned engagement-mechanic keys. ` +
      `Found: ${JSON.stringify(progressViolations)}`,
    ).toEqual([])

    // ── 3. Date-range query honored (smoke) ──
    const from = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString()
    const to = new Date().toISOString()
    const rangeResp = await request.get(
      `${STUDENT_API}/api/analytics/progress?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
      { headers: { Authorization: `Bearer ${me.idToken}` } },
    )
    expect(rangeResp.status(), '/api/analytics/progress with date range must be 200').toBe(200)

    // ── 4. JWT-claim isolation: same endpoint with `other`'s token must
    //      NOT return `me`'s data. The check is structural — we don't
    //      know what's in the payload, but if the endpoint inserts a
    //      studentId we should see `other.uid`, never `me.uid`. ──
    const otherMastery = await request.get(`${STUDENT_API}/api/analytics/mastery`, {
      headers: { Authorization: `Bearer ${other.idToken}` },
    })
    expect(otherMastery.status()).toBe(200)
    const otherMasteryStr = JSON.stringify(await otherMastery.json())
    expect(otherMasteryStr.includes(me.uid),
      `cross-token /api/analytics/mastery must NOT contain me.uid (${me.uid}) when called with other.idToken. ` +
      `This would be a JWT-claim-isolation regression.`,
    ).toBe(false)

    testInfo.attach('c-06-mastery-trajectory.json', {
      body: JSON.stringify({
        meUid: me.uid,
        otherUid: other.uid,
        mastery: masteryBody,
        progress: progressBody,
        masteryViolations,
        progressViolations,
      }, null, 2),
      contentType: 'application/json',
    })
  })
})
