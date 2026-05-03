// =============================================================================
// Cena E2E flow — TASK-E2E-INFRA-03 dynamic-route seed fixture
//
// Several admin + student dynamic [id] routes need a real seeded id to
// drive a meaningful test. Examples:
//   /apps/experiments/[id]
//   /apps/mastery/student/[id]
//   /apps/moderation/review/[id]
//   /apps/tutoring/sessions/[id]
//   /apps/user/view/[id]
//   /tutor/[threadId]
//   /session/[sessionId]
//
// Hardcoded ids drift; ad-hoc seeding per-spec is fragile + duplicates
// boilerplate. This fixture centralizes:
//   - per-test fresh student/admin/parent provisioning (Firebase emu +
//     on-first-sign-in)
//   - tutor-thread creation via POST /api/tutor/threads (real
//     backend endpoint — no probe needed)
//   - cleanup tracking for best-effort Firebase emu user removal
//
// Scope discipline: this fixture only uses **already-shipped** backend
// endpoints. Adding probe endpoints (POST /api/admin/test/seed/*) is
// claude-code's territory — that work is queued separately.
//
// Usage:
//   import { e2eTest as test } from './fixtures/tenant'
//   ...
//   test('drill into a tutor thread', async ({ page, dynamicSeed }) => {
//     const student = await dynamicSeed.freshStudent()
//     const { threadId } = await dynamicSeed.tutorThread({ student })
//     await page.goto(`/tutor/${threadId}`)
//     // ...
//   })
// =============================================================================

import { request } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const PROJECT_ID = process.env.FIREBASE_PROJECT_ID ?? 'cena-platform'
const EMU_BEARER = process.env.FIREBASE_EMU_BEARER ?? 'owner'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

const SEEDED_ADMIN_EMAIL = 'admin@cena.local'
const SEEDED_ADMIN_PASSWORD = 'DevAdmin123!'

export interface FreshStudent {
  email: string
  password: string
  uid: string
  idToken: string
  studentId: string
}

export interface SeededTutorThread {
  threadId: string
  student: FreshStudent
  messageCount: number
}

export interface DynamicSeed {
  /**
   * Provision a fresh student via Firebase emu signUp + on-first-sign-in
   * + onboarding. Returns a fully-bootstrapped student suitable for
   * driving any signed-in route. Cheap (~600ms cold).
   */
  freshStudent(opts?: {
    tenantId?: string
    schoolId?: string
    displayName?: string
  }): Promise<FreshStudent>

  /**
   * Sign in as the seeded admin@cena.local user. Returns the idToken
   * (with the SUPER_ADMIN claim already on the JWT). Use for tests
   * that hit /api/admin/* surfaces.
   */
  freshAdminToken(): Promise<string>

  /**
   * Create a tutor thread for a fresh student. POSTs to
   * /api/tutor/threads, returns threadId. Optionally seeds N
   * messages via /api/tutor/threads/{threadId}/messages.
   */
  tutorThread(opts: {
    student: FreshStudent
    messageCount?: number
  }): Promise<SeededTutorThread>

  /**
   * Provision a parent + child Firebase user pair (no binding yet —
   * binding flow lives in TASK-E2E-A-04 and needs an invite token from
   * the admin side). Returns both uids + the parent's idToken.
   */
  parentChildPair(opts?: {
    tenantId?: string
    schoolId?: string
  }): Promise<{
    parent: FreshStudent
    child: FreshStudent
  }>

  /**
   * Best-effort cleanup of resources created by this fixture instance.
   * Deletes Firebase emu users; does NOT delete tutor threads / Marten
   * docs (no admin DELETE endpoint exposed for those — they're left
   * for the postgres TTL / dev-stack reset to garbage-collect).
   *
   * Idempotent — safe to call multiple times.
   */
  cleanup(): Promise<void>
}

/**
 * Build a DynamicSeed bound to the test's tenant + a tracking list for
 * cleanup. Internal — exported for tenant.ts to wire as a fixture.
 */
export function createDynamicSeed(opts: {
  tenantId: string
  schoolId?: string
  request?: APIRequestContext
}): DynamicSeed {
  const tenantId = opts.tenantId
  const schoolId = opts.schoolId ?? 'cena-platform'

  // Track created uids so cleanup can delete them via the emu admin API.
  const createdUids: string[] = []

  // Lazy-create the request context on first use. Saves the per-test
  // setup cost when a test only needs a subset of the seed helpers.
  let lazyRequest: APIRequestContext | null = opts.request ?? null
  async function req(): Promise<APIRequestContext> {
    if (lazyRequest) return lazyRequest
    lazyRequest = await request.newContext()
    return lazyRequest
  }

  async function freshStudent(o: {
    tenantId?: string; schoolId?: string; displayName?: string
  } = {}): Promise<FreshStudent> {
    const r = await req()
    const targetTenantId = o.tenantId ?? tenantId
    const targetSchoolId = o.schoolId ?? schoolId
    const displayName = o.displayName ?? 'E2E Seed Student'

    const email = `e2e-seed-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    // Firebase emu signUp
    const signupResp = await r.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    if (!signupResp.ok())
      throw new Error(`[dynamicSeed.freshStudent] signUp failed ${signupResp.status()}: ${await signupResp.text()}`)
    const signed = await signupResp.json() as { localId: string; idToken: string }
    createdUids.push(signed.localId)

    // on-first-sign-in pushes role/tenant/school into customClaims
    const bootstrapResp = await r.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${signed.idToken}` },
      data: { tenantId: targetTenantId, schoolId: targetSchoolId, displayName },
    })
    if (bootstrapResp.status() !== 200)
      throw new Error(`[dynamicSeed.freshStudent] on-first-sign-in ${bootstrapResp.status()}: ${await bootstrapResp.text()}`)

    // Re-issue idToken so the JWT carries the freshly pushed customClaims
    const tokenResp = await r.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tokenResp.json() as { idToken: string }

    // Optional: complete onboarding so requiresOnboarded routes load
    await r.post(`${STUDENT_API}/api/me/onboarding`, {
      headers: { Authorization: `Bearer ${idToken}` },
      data: {
        Role: 'student', Locale: 'en', Subjects: ['math'],
        DailyTimeGoalMinutes: 15, WeeklySubjectTargets: [],
        DiagnosticResults: null, ClassroomCode: null,
      },
    }).catch(() => { /* idempotent — tolerate already-onboarded */ })

    // Resolve studentId from /api/me
    const meResp = await r.get(`${STUDENT_API}/api/me`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    const meBody = await meResp.json() as { studentId?: string }
    if (!meBody.studentId)
      throw new Error(`[dynamicSeed.freshStudent] /api/me missing studentId: ${JSON.stringify(meBody)}`)

    return {
      email, password,
      uid: signed.localId,
      idToken,
      studentId: meBody.studentId,
    }
  }

  async function freshAdminToken(): Promise<string> {
    const r = await req()
    const tokenResp = await r.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email: SEEDED_ADMIN_EMAIL, password: SEEDED_ADMIN_PASSWORD, returnSecureToken: true } },
    )
    if (!tokenResp.ok())
      throw new Error(`[dynamicSeed.freshAdminToken] signIn failed ${tokenResp.status()}`)
    const { idToken } = await tokenResp.json() as { idToken: string }
    return idToken
  }

  async function tutorThread(o: {
    student: FreshStudent
    messageCount?: number
  }): Promise<SeededTutorThread> {
    const r = await req()
    const messageCount = o.messageCount ?? 0

    // /api/tutor/* is gated by RequireConsent(ProcessingPurpose.ThirdPartyAi).
    // Grant consent before creating the thread, otherwise the create
    // POST 403s with {"error":"consent_required","purpose":"thirdpartyai"}.
    // Idempotent — granting an already-granted consent is a no-op.
    await r.post(`${STUDENT_API}/api/me/consent`, {
      headers: { Authorization: `Bearer ${o.student.idToken}` },
      data: { Purpose: 'ThirdPartyAi', Granted: true },
    }).catch(() => { /* best-effort — let the create call surface the real reason */ })

    // POST /api/tutor/threads — creates an empty thread + returns id
    const createResp = await r.post(`${STUDENT_API}/api/tutor/threads`, {
      headers: { Authorization: `Bearer ${o.student.idToken}` },
      data: {
        // Minimal payload — real flow may post a title; backend handles
        // empty/default. Adjust if the contract evolves.
      },
    })
    if (createResp.status() !== 200 && createResp.status() !== 201)
      throw new Error(`[dynamicSeed.tutorThread] create failed ${createResp.status()}: ${await createResp.text()}`)
    const body = await createResp.json() as { threadId?: string; id?: string }
    const threadId = body.threadId ?? body.id
    if (!threadId)
      throw new Error(`[dynamicSeed.tutorThread] response missing threadId: ${JSON.stringify(body)}`)

    // Optional: seed messages so the thread isn't empty when the test
    // navigates. We POST short test messages — the backend may stream
    // assistant replies asynchronously which we don't wait for.
    for (let i = 0; i < messageCount; i++) {
      await r.post(`${STUDENT_API}/api/tutor/threads/${threadId}/messages`, {
        headers: { Authorization: `Bearer ${o.student.idToken}` },
        data: { content: `seed message #${i + 1}` },
      }).catch(() => { /* best-effort — content/budget can reject */ })
    }

    return { threadId, student: o.student, messageCount }
  }

  async function parentChildPair(o: {
    tenantId?: string; schoolId?: string
  } = {}): Promise<{ parent: FreshStudent; child: FreshStudent }> {
    const child = await freshStudent({
      tenantId: o.tenantId,
      schoolId: o.schoolId,
      displayName: 'E2E Seed Child',
    })
    // Parent uses the same shape as a student-tier user — the role
    // distinction lives in customClaims and gets set by on-first-sign-in.
    // For now both come back as STUDENT role; binding to a parent role
    // is a separate flow (TASK-E2E-A-04). Tests that need a true PARENT
    // claim should drive the parent-bind invite flow on top of this pair.
    const parent = await freshStudent({
      tenantId: o.tenantId,
      schoolId: o.schoolId,
      displayName: 'E2E Seed Parent',
    })
    return { parent, child }
  }

  async function cleanup(): Promise<void> {
    if (createdUids.length === 0) return

    const r = await req()
    for (const uid of createdUids) {
      // Firebase emu admin delete-account
      await r.post(
        `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/projects/${PROJECT_ID}/accounts:delete`,
        {
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${EMU_BEARER}`,
          },
          data: { localId: uid },
        },
      ).catch(() => { /* best-effort cleanup */ })
    }
    createdUids.length = 0

    if (!opts.request && lazyRequest) {
      await lazyRequest.dispose().catch(() => {})
      lazyRequest = null
    }
  }

  return {
    freshStudent,
    freshAdminToken,
    tutorThread,
    parentChildPair,
    cleanup,
  }
}
