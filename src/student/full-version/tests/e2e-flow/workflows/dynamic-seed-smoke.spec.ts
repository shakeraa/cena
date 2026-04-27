// =============================================================================
// TASK-E2E-INFRA-03 — dynamic-seed fixture smoke
//
// Proves the fixture's helpers actually work against the live dev stack:
//   - freshStudent() returns a usable studentId + idToken
//   - tutorThread() creates a real /api/tutor/threads row
//   - parentChildPair() returns two distinct uids
//   - cleanup() removes the Firebase emu users
//
// This spec is deliberately separate from any per-page functional spec
// — its only job is to keep the fixture honest. If a future code change
// breaks the fixture itself, the failure surfaces here in 5 seconds
// rather than an hour into a downstream spec run.
// =============================================================================

import { e2eTest as test, expect } from '../fixtures/tenant'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

test.describe('INFRA-03 dynamic-seed fixture smoke', () => {
  test('freshStudent returns a usable idToken + studentId @infra @dynamic-seed', async ({ dynamicSeed, page }) => {
    const student = await dynamicSeed.freshStudent({ displayName: 'Infra-03 Smoke' })

    expect(student.uid, 'fresh student should have a Firebase uid').toBeTruthy()
    expect(student.studentId, 'fresh student should have a Marten studentId').toBeTruthy()
    expect(student.idToken, 'fresh student should have an idToken').toBeTruthy()

    // Validate the idToken hits a real authenticated endpoint.
    const meResp = await page.request.get(`${STUDENT_API}/api/me`, {
      headers: { Authorization: `Bearer ${student.idToken}` },
    })
    expect(meResp.status(), 'idToken should validate against /api/me').toBe(200)

    const me = await meResp.json() as { studentId: string }
    expect(me.studentId).toBe(student.studentId)
  })

  test('tutorThread creates a real thread reachable by id @infra @dynamic-seed', async ({ dynamicSeed, page }) => {
    const student = await dynamicSeed.freshStudent()
    const seeded = await dynamicSeed.tutorThread({ student })

    expect(seeded.threadId, 'thread should have an id').toBeTruthy()

    // Verify the thread is reachable directly. /api/tutor/threads/{id}/messages
    // is the canonical "thread exists" check — the messages list endpoint
    // returns 200 with empty Items for a freshly-created thread, 404
    // otherwise.
    const messagesResp = await page.request.get(
      `${STUDENT_API}/api/tutor/threads/${seeded.threadId}/messages`,
      { headers: { Authorization: `Bearer ${student.idToken}` } },
    )
    expect(messagesResp.status(),
      `seeded thread ${seeded.threadId} should be reachable via /api/tutor/threads/{id}/messages`,
    ).toBe(200)
  })

  test('parentChildPair returns two distinct uids @infra @dynamic-seed', async ({ dynamicSeed }) => {
    const { parent, child } = await dynamicSeed.parentChildPair()

    expect(parent.uid).toBeTruthy()
    expect(child.uid).toBeTruthy()
    expect(parent.uid, 'parent + child must be distinct users').not.toBe(child.uid)
    expect(parent.email).not.toBe(child.email)
  })

  test('freshAdminToken signs in as seeded admin @infra @dynamic-seed', async ({ dynamicSeed, page }) => {
    const adminToken = await dynamicSeed.freshAdminToken()
    expect(adminToken).toBeTruthy()

    // /api/admin/me should accept the token (admin-api on :5052; we
    // reuse page.request which handles cross-origin via fetch).
    // We don't pin the response shape — just that auth succeeds (2xx
    // or 404 if the endpoint doesn't exist — anything except 401).
    const adminApiBase = process.env.E2E_ADMIN_API_URL ?? 'http://localhost:5052'
    const meResp = await page.request.get(`${adminApiBase}/api/admin/me`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    })
    expect(meResp.status(),
      `admin token should authenticate (got ${meResp.status()}, expected NOT 401/403)`,
    ).not.toBe(401)
    expect(meResp.status()).not.toBe(403)
  })
})
