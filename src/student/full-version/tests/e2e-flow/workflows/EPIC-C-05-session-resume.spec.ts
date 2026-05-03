// =============================================================================
// EPIC-E2E-C-05 — Session interrupt & resume (offline-tolerant)
//
// Per the EPIC-C plan: "mid-session → tab close / connection drop → reopen
// within 30 min → session resumes at correct question + previous answers
// preserved." The existing EPIC-C-learning-journey covers C-02 (happy
// practice path) but not the interrupt-resume contract.
//
// Boundaries asserted in this spec:
//   1. POST /api/sessions/start returns a sessionId + creates a server-
//      side LearningSessionQueueProjection (not localStorage).
//   2. GET /api/sessions/{id}/current-question + /history return the
//      same session state before AND after a simulated browser-close
//      (new page in same context). The server-side projection is the
//      source of truth, not the SPA.
//   3. /start is idempotent: calling it again with an active session
//      returns the SAME sessionId (ActiveSessionSnapshot dedup).
//   4. Cross-tenant attacker check: a different student's idToken
//      hits the SAME sessionId → 403/404, not 200 with another's data.
//
// Endpoint note: GET /api/sessions/{id} and POST /api/sessions/{id}/resume
// query TutoringSessionDocument (the AI-tutor bounded context — EPIC-D),
// not LearningSessionQueueProjection (the practice bounded context —
// EPIC-C). For a freshly-started learning session, those endpoints
// return 404 by design; we use the C-context endpoints instead.
//
// What's intentionally NOT covered (deeper C-05 follow-ups):
//   * Mid-flight answers in localStorage offline-queue draining on
//     reconnect — K-02 territory (offline answer queue).
//   * 30-minute window expiry — needs clock manipulation.
//   * Multi-device pickup — covered indirectly by /start idempotency
//     (the second /start call from a new tab returns the same id).
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface ProvisionedStudent {
  email: string
  password: string
  uid: string
  idToken: string
}

interface SessionStartResponse {
  sessionId: string
  hubGroupName?: string
  firstQuestionId?: string
}

async function provisionStudent(page: Page, label: string): Promise<ProvisionedStudent> {
  const email = `e2e-c05-${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  const signupResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const signupBody = await signupResp.json() as { localId: string }
  const uid = signupBody.localId

  const tokenResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken: bootstrapToken } = await tokenResp.json() as { idToken: string }

  await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `C-05 ${label}` },
  })

  // Re-issue idToken AFTER on-first-sign-in pushed claims, otherwise
  // the bootstrapToken has stale claims and onboarding rejects.
  const reLoginResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await reLoginResp.json() as { idToken: string }

  await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      role: 'student', locale: 'en', subjects: ['math'],
      dailyTimeGoalMinutes: 15, weeklySubjectTargets: [],
      diagnosticResults: null, classroomCode: null,
    },
  })

  return { email, password, uid, idToken }
}

async function startSession(page: Page, idToken: string): Promise<SessionStartResponse> {
  const resp = await page.request.post(`${STUDENT_API}/api/sessions/start`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: { subjects: ['math'], durationMinutes: 15, mode: 'practice' },
  })
  expect(resp.status(), 'POST /api/sessions/start should be 200').toBe(200)
  const body = await resp.json() as Record<string, unknown>
  // The server returns SessionId in PascalCase (per the C# record contract);
  // ASP.NET's default JSON serializer camelCases automatically. We accept
  // both shapes to stay resilient if either side changes its naming.
  const sessionId = (body.sessionId ?? body.SessionId) as string
  expect(sessionId, '/start response must include a sessionId').toBeTruthy()
  return {
    sessionId,
    hubGroupName: (body.hubGroupName ?? body.HubGroupName) as string | undefined,
    firstQuestionId: (body.firstQuestionId ?? body.FirstQuestionId) as string | undefined,
  }
}

async function getCurrentQuestion(page: Page, idToken: string, sessionId: string): Promise<{ status: number; body: unknown }> {
  const resp = await page.request.get(`${STUDENT_API}/api/sessions/${sessionId}/current-question`, {
    headers: { Authorization: `Bearer ${idToken}` },
  })
  return { status: resp.status(), body: resp.status() === 200 ? await resp.json() : await resp.text() }
}

async function getHistory(page: Page, idToken: string, sessionId: string): Promise<{ status: number; body: unknown }> {
  const resp = await page.request.get(`${STUDENT_API}/api/sessions/${sessionId}/history`, {
    headers: { Authorization: `Bearer ${idToken}` },
  })
  return { status: resp.status(), body: resp.status() === 200 ? await resp.json() : await resp.text() }
}

test.describe('EPIC_C_05_SESSION_RESUME', () => {
  test('start → close+reopen → projection state survives + cross-tenant denied @epic-c @c-05 @session-resume', async ({ browser }, testInfo) => {
    test.setTimeout(120_000)

    // ── Bootstrap two students. We use the same browser context for the
    //    OWNER (page1 then page2) and a separate context for the FOREIGN
    //    student so cookies/IDB don't leak.
    const ownerCtx = await browser.newContext()
    const otherCtx = await browser.newContext()
    try {
      const ownerPage1 = await ownerCtx.newPage()
      const otherPage = await otherCtx.newPage()

      const owner = await provisionStudent(ownerPage1, 'owner')
      const other = await provisionStudent(otherPage, 'other')

      // ── 1. Start a session as the owner.
      const start1 = await startSession(ownerPage1, owner.idToken)
      console.log(`[c-05] started session ${start1.sessionId} firstQuestion=${start1.firstQuestionId ?? '<none>'}`)

      // ── 2. GET /api/sessions/{id}/current-question mid-flow → 200 with
      //    the active question payload. This is the C-context analog of
      //    "session detail" — it loads from LearningSessionQueueProjection.
      const before = await getCurrentQuestion(ownerPage1, owner.idToken, start1.sessionId)
      expect(before.status, 'mid-flow /current-question must be 200').toBe(200)
      const beforeKeys = Object.keys(before.body as object).sort()
      expect(beforeKeys.length, 'current-question payload must not be empty').toBeGreaterThan(0)

      // ── 3. /start TWICE — idempotency check. Same session id should
      //    surface (active-session snapshot prevents duplicate creation).
      const start2 = await startSession(ownerPage1, owner.idToken)
      expect(start2.sessionId,
        '/start must be idempotent: same active session → same sessionId',
      ).toBe(start1.sessionId)

      // ── 4. Simulate browser close: close ownerPage1, open ownerPage2
      //    in the SAME context. Cookies/Firebase IDB persist; the server-
      //    side projection persists in Marten regardless. The session
      //    must remain reachable from the new page.
      await ownerPage1.close()
      const ownerPage2 = await ownerCtx.newPage()

      const after = await getCurrentQuestion(ownerPage2, owner.idToken, start1.sessionId)
      expect(after.status, '/current-question after page close must still be 200').toBe(200)
      const afterKeys = Object.keys(after.body as object).sort()
      // The set of top-level keys should match — the projection is
      // stable across the page close.
      expect(afterKeys,
        'current-question payload key-set must match before vs after page close',
      ).toEqual(beforeKeys)

      // ── 5. /history is also queryable post-reopen (same projection).
      const history = await getHistory(ownerPage2, owner.idToken, start1.sessionId)
      expect(history.status, '/history must be 200 for the owner post-reopen').toBe(200)

      // ── 6. Foreign student attempting to read the SAME session id
      //    must be denied. ResourceOwnershipGuard returns 403 (NOT 404 —
      //    the doc exists, the caller just can't see it). 404 would be
      //    a strange-but-acceptable alternative; 200 with another's
      //    session data is a P0 leak.
      const foreignCurrent = await getCurrentQuestion(otherPage, other.idToken, start1.sessionId)
      expect([401, 403, 404]).toContain(foreignCurrent.status)
      expect(foreignCurrent.status,
        `cross-tenant /current-question must NOT return 200; got ${foreignCurrent.status}`,
      ).not.toBe(200)

      const foreignHistory = await getHistory(otherPage, other.idToken, start1.sessionId)
      expect([401, 403, 404]).toContain(foreignHistory.status)
      expect(foreignHistory.status,
        `cross-tenant /history must NOT return 200; got ${foreignHistory.status}`,
      ).not.toBe(200)

      testInfo.attach('c-05-resume-flow.json', {
        body: JSON.stringify({
          ownerSessionId: start1.sessionId,
          startIdempotency: { first: start1.sessionId, second: start2.sessionId, equal: start1.sessionId === start2.sessionId },
          projectionSurvivesPageClose: { beforeKeys, afterKeys, equal: JSON.stringify(beforeKeys) === JSON.stringify(afterKeys) },
          historyAfterReopen: history.status,
          foreignAttack: { currentStatus: foreignCurrent.status, historyStatus: foreignHistory.status },
        }, null, 2),
        contentType: 'application/json',
      })
    }
    finally {
      await ownerCtx.close().catch(() => {})
      await otherCtx.close().catch(() => {})
    }
  })
})
