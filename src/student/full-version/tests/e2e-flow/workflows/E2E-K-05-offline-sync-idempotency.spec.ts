// =============================================================================
// E2E-K-05 — Offline sync idempotency (P1, RDY-075)
//
// The contract under test:
//
//   When the student answers a question while offline, the queue may end up
//   with multiple entries targeting the SAME (sessionId, questionId)
//   (re-submission after a perceived failure, network glitch mid-drain,
//   tab duplication, etc.). The drain MUST converge to exactly one
//   recorded attempt on the backend. NO double-count in mastery.
//
// How this is achieved in the codebase today:
//
//   • Backend (SessionEndpoints.cs `MapPost("/{sessionId}/answer")`): the
//     LearningSessionQueueProjection.PeekNext() returns the current
//     question. RecordAnswer advances the queue. A second POST against
//     the same (sessionId, questionId) finds either:
//       - "No current question" (if queue empty) → 409 Conflict, OR
//       - a different next question → POST is processed against a
//         different question (so it can't double-count for the original).
//   • Client drain (useNetworkStatus.drainQueue line 170): treats both
//     `res.ok` AND `res.status === 409` as drain-success; the entry is
//     removed from IndexedDB either way. This is "idempotency via state
//     machine" — the backend's queue advance is the dedup primitive.
//
// What this spec does:
//
//   1. Bootstrap student + start session + capture firstQuestionId.
//   2. Pre-populate the IndexedDB offline queue with TWO submissions
//      pointing at the SAME (sessionId, questionId) but with different
//      client-side ids (simulating: first submit failed mid-network, retry
//      enqueued a duplicate, then both reach the drain after reconnect).
//   3. Run drain.
//   4. Assert: drainQueue.sent === 2 (both removed from IDB), drainQueue.failed === 0.
//   5. Assert: the BACKEND recorded exactly one attempt — `state.answeredCount ===
//      pre-drain answeredCount + 1`. NOT +2.
//
// Why two-different-ids and not two-of-the-same:
//
//   The IDB store has keyPath: 'id'. Two entries with the SAME id would
//   overwrite each other on `put`, never giving us a "twice in the queue"
//   condition. The realistic failure mode (retry-enqueue) generates a
//   fresh id each time — that's what we mirror.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string }

async function bootstrapStudent(
  page: import('@playwright/test').Page,
  label: string,
): Promise<{ idToken: string; email: string; password: string }> {
  const email = `${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  const su = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(su.ok()).toBe(true)
  const { idToken: bootstrapToken } = await su.json() as { idToken: string }

  expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `K05 ${label}` },
  })).status()).toBe(200)

  expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: {
      role: 'student',
      locale: 'en',
      subjects: ['math'],
      dailyTimeGoalMinutes: 15,
      weeklySubjectTargets: [],
      diagnosticResults: null,
      classroomCode: null,
    },
  })).status()).toBe(200)

  const reLogin = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await reLogin.json() as { idToken: string }
  return { idToken, email, password }
}

async function startSession(
  page: import('@playwright/test').Page,
  idToken: string,
): Promise<{ sessionId: string; firstQuestionId: string | null }> {
  const resp = await page.request.post(`${STUDENT_API}/api/sessions/start`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: { subjects: ['math'], durationMinutes: 5, mode: 'practice' },
  })
  expect(resp.status()).toBe(200)
  return await resp.json() as { sessionId: string; firstQuestionId: string | null }
}

async function loginViaSpa(
  page: import('@playwright/test').Page,
  email: string,
  password: string,
): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

async function getAnsweredCount(
  page: import('@playwright/test').Page,
  idToken: string,
  sessionId: string,
): Promise<number> {
  // Source of truth is TutorContextResponseDto.AnsweredCount returned by
  // GET /api/v1/sessions/{sessionId}/tutor-context (SessionDtos.cs L381).
  const resp = await page.request.get(
    `${STUDENT_API}/api/v1/sessions/${sessionId}/tutor-context`,
    { headers: { Authorization: `Bearer ${idToken}` } },
  )
  expect(resp.status(), '/api/v1/sessions/:id/tutor-context').toBe(200)
  const state = await resp.json() as { answeredCount?: number }
  return state.answeredCount ?? 0
}

/** Pre-populate IDB with N entries pointing at the same (sessionId, questionId). */
async function prePopulateQueueWithDuplicates(
  page: import('@playwright/test').Page,
  args: {
    apiBase: string
    idToken: string
    sessionId: string
    questionId: string
    count: number
  },
): Promise<string[]> {
  return page.evaluate(async (a) => {
    const open = await new Promise<IDBDatabase>((resolve, reject) => {
      const req = indexedDB.open('cena-offline', 1)
      req.onupgradeneeded = () => {
        const d = req.result
        if (!d.objectStoreNames.contains('submissions'))
          d.createObjectStore('submissions', { keyPath: 'id' })
      }
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    const ids: string[] = []
    for (let i = 0; i < a.count; i++) {
      const id = `sub_${Date.now()}_${i}_${Math.random().toString(36).slice(2, 8)}`
      ids.push(id)
      const entry = {
        id,
        url: `${a.apiBase}/api/sessions/${a.sessionId}/answer`,
        method: 'POST',
        body: JSON.stringify({ questionId: a.questionId, answer: 'A', timeSpentMs: 1000 + i }),
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${a.idToken}`,
        },
        createdAt: Date.now() + i,
        retries: 0,
      }
      await new Promise<void>((resolve, reject) => {
        const tx = open.transaction('submissions', 'readwrite')
        tx.objectStore('submissions').put(entry)
        tx.oncomplete = () => resolve()
        tx.onerror = () => reject(tx.error)
      })
    }
    open.close()
    return ids
  }, args)
}

async function drainOfflineQueue(
  page: import('@playwright/test').Page,
): Promise<{ sent: number; failed: number; statuses: number[] }> {
  return page.evaluate(async () => {
    const open = await new Promise<IDBDatabase>((resolve, reject) => {
      const req = indexedDB.open('cena-offline', 1)
      req.onupgradeneeded = () => {
        const d = req.result
        if (!d.objectStoreNames.contains('submissions'))
          d.createObjectStore('submissions', { keyPath: 'id' })
      }
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    const entries = await new Promise<Array<{
      id: string; url: string; method: string; headers: Record<string, string>; body: string
    }>>((resolve, reject) => {
      const tx = open.transaction('submissions', 'readonly')
      const r = tx.objectStore('submissions').getAll()
      r.onsuccess = () => resolve(r.result as Array<{
        id: string; url: string; method: string; headers: Record<string, string>; body: string
      }>)
      r.onerror = () => reject(r.error)
    })
    let sent = 0
    let failed = 0
    const statuses: number[] = []
    // Drain SEQUENTIALLY (mirrors useNetworkStatus.drainQueue line 162).
    // The order matters here: the first POST will succeed (200) and
    // advance the backend queue; the second POST against the SAME
    // (sessionId, questionId) MUST hit the post-advance state machine.
    for (const entry of entries) {
      try {
        const res = await fetch(entry.url, {
          method: entry.method,
          headers: entry.headers,
          body: entry.body,
        })
        statuses.push(res.status)
        if (res.ok || res.status === 409) {
          await new Promise<void>((resolve, reject) => {
            const tx = open.transaction('submissions', 'readwrite')
            tx.objectStore('submissions').delete(entry.id)
            tx.oncomplete = () => resolve()
            tx.onerror = () => reject(tx.error)
          })
          sent++
        }
        else {
          failed++
        }
      }
      catch {
        failed++
        statuses.push(0)
      }
    }
    open.close()
    return { sent, failed, statuses }
  })
}

async function readQueueIds(
  page: import('@playwright/test').Page,
): Promise<string[]> {
  return page.evaluate(async () => {
    const open = await new Promise<IDBDatabase>((resolve, reject) => {
      const req = indexedDB.open('cena-offline', 1)
      req.onupgradeneeded = () => {
        const d = req.result
        if (!d.objectStoreNames.contains('submissions'))
          d.createObjectStore('submissions', { keyPath: 'id' })
      }
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
    const ids = await new Promise<string[]>((resolve, reject) => {
      const tx = open.transaction('submissions', 'readonly')
      const r = tx.objectStore('submissions').getAllKeys()
      r.onsuccess = () => resolve(r.result as string[])
      r.onerror = () => reject(r.error)
    })
    open.close()
    return ids
  })
}

test.describe('E2E_K_05_OFFLINE_SYNC_IDEMPOTENCY', () => {
  test('two duplicate enqueued submissions drain to exactly ONE backend attempt @epic-k @offline @p1 @ship-gate', async ({ page }, testInfo) => {
    test.setTimeout(180_000)
    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string }[] = []
    page.on('console', m => consoleEntries.push({ type: m.type(), text: m.text() }))
    page.on('pageerror', e => pageErrors.push({ message: e.message }))

    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    const a = await bootstrapStudent(page, 'k-05')
    const session = await startSession(page, a.idToken)
    expect(
      session.firstQuestionId,
      'dev seed must produce a math question; if this fails, the seeded question bank is empty for math',
    ).not.toBeNull()
    const questionId = session.firstQuestionId as string

    await loginViaSpa(page, a.email, a.password)

    const initialAnswered = await getAnsweredCount(page, a.idToken, session.sessionId)

    // Pre-populate the queue with TWO duplicate submissions (different IDs,
    // same target). Mirrors a retry-after-perceived-network-failure pattern.
    const enqueuedIds = await prePopulateQueueWithDuplicates(page, {
      apiBase: STUDENT_API,
      idToken: a.idToken,
      sessionId: session.sessionId,
      questionId,
      count: 2,
    })
    expect(enqueuedIds.length).toBe(2)

    const queueBeforeDrain = await readQueueIds(page)
    expect(queueBeforeDrain.length, 'queue has both duplicates pre-drain').toBe(2)

    // Drive drain. The drain runs sequentially. First POST records
    // the answer (200/201). Second POST hits the post-advance state →
    // backend either returns 409 (queue empty) or processes a different
    // question (whichever the seeded queue depth produces). Either way,
    // the answeredCount delta MUST be exactly +1 for THIS questionId.
    const result = await drainOfflineQueue(page)

    testInfo.attach('drain-result.json', {
      body: JSON.stringify(result, null, 2),
      contentType: 'application/json',
    })

    expect(result.failed,
      `no failed entries during drain (got statuses ${JSON.stringify(result.statuses)})`,
    ).toBe(0)
    expect(result.sent,
      `both entries removed from IDB (got sent=${result.sent})`,
    ).toBe(2)

    const queueAfterDrain = await readQueueIds(page)
    expect(queueAfterDrain.length, 'IDB store empty after drain').toBe(0)

    // The KEY ASSERTION — backend has exactly one extra attempt event for
    // questionId, not two. answeredCount === initial + 1. If this fails,
    // the backend double-counted the duplicate submission.
    const finalAnswered = await getAnsweredCount(page, a.idToken, session.sessionId)
    expect(
      finalAnswered,
      `backend must record EXACTLY ONE attempt for the duplicated submission ` +
      `(initial=${initialAnswered}, final=${finalAnswered}, drainSent=${result.sent}, ` +
      `statuses=${JSON.stringify(result.statuses)}). ` +
      `If final > initial+1, the backend's idempotency-via-state-machine is broken — ` +
      `mastery will double-count on retry.`,
    ).toBe(initialAnswered + 1)

    // The two responses MUST be: one 200/201 (accepted), one 409
    // (post-advance dedup). Documents the contract for future regressions.
    const [s1, s2] = result.statuses
    expect(s1).toBeLessThan(300) // first request: 200/201
    expect(
      s2,
      `second response must be 409 (idempotency-via-queue-advance); got ${s2}. ` +
      `Other 4xx codes also pass this test — what's forbidden is a 200/201 that creates a duplicate event, which would ` +
      `manifest as finalAnswered = initial + 2.`,
    ).toBeGreaterThanOrEqual(400)

    testInfo.attach('console-entries.json', {
      body: JSON.stringify(consoleEntries, null, 2),
      contentType: 'application/json',
    })
    testInfo.attach('page-errors.json', {
      body: JSON.stringify(pageErrors, null, 2),
      contentType: 'application/json',
    })
    expect(pageErrors, 'no JS exceptions').toHaveLength(0)
  })
})
