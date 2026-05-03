// =============================================================================
// E2E-I-02 — Misconception NEVER attached to student profile (ADR-0003) — P0
//
// ADR-0003 Decision 1 + 4: misconception data is session-scoped only.
// StudentProfileSnapshot must contain ZERO misconception fields. Per-student
// event streams must NOT carry MisconceptionEvent rows that are studentId-keyed.
//
// What this spec proves:
//   1. The /api/me/gdpr/export response (GDPR Art 20 portability) is the
//      definitive shape of student-attached data. Its profile section MUST
//      NOT contain any field whose name contains "misconception".
//   2. The export's `eventCount`/event types section, if present, MUST NOT
//      list any MisconceptionEvent type.
//
// We drive this from the student side because:
//   - The export is the auditor-facing artifact (not an admin-only probe).
//   - The export shape is the contract a parent/regulator can request and
//     verify directly.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

test.describe('E2E_I_02_MISCONCEPTION_NOT_ON_PROFILE', () => {
  test('GDPR export contains NO misconception fields on profile @epic-i @ship-gate @compliance', async ({ page }) => {
    test.setTimeout(120_000)
    console.log('\n=== E2E_I_02_MISCONCEPTION_NOT_ON_PROFILE ===\n')

    const email = `i-02-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'I02 Tester' },
    })).status()).toBe(200)

    const reLogin = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await reLogin.json() as { idToken: string }

    // POST /api/me/gdpr/export returns the full portability JSON.
    const exportResp = await page.request.post(`${STUDENT_API}/api/me/gdpr/export`, {
      headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
    })
    expect(exportResp.status()).toBe(200)
    const exportJson = await exportResp.json() as Record<string, unknown>
    console.log(`[i-02] export keys: ${Object.keys(exportJson).join(', ')}`)

    // ── ADR-0003 Decision 1: zero misconception fields on profile ──
    interface ExportedField { name: string; piiLevel: string; category: string }
    const profile = (exportJson.profile ?? []) as ExportedField[]
    const offending = profile.filter(f => f.name.toLowerCase().includes('misconception'))
    expect(offending, `profile must not carry misconception fields; found: ${JSON.stringify(offending)}`).toHaveLength(0)
    console.log(`[i-02] profile fields: ${profile.length}, misconception leaks: 0`)

    // ── ADR-0003 Decision 4: events NOT keyed on studentId for misconceptions ──
    // The export's domain events list (eventCount or events array) must not
    // list any MisconceptionEvent or similar type.
    const eventsRaw = exportJson.events ?? exportJson.domainEvents
    if (Array.isArray(eventsRaw)) {
      const events = eventsRaw as { type?: string; eventType?: string }[]
      const misEvents = events.filter(e => {
        const t = (e.type ?? e.eventType ?? '').toLowerCase()
        return t.includes('misconception')
      })
      expect(misEvents, 'event stream must not carry studentId-keyed misconception events').toHaveLength(0)
      console.log(`[i-02] events: ${events.length}, misconception leaks: 0`)
    }
    else {
      console.log('[i-02] no events section in export — vacuously passes profile assertion')
    }
  })
})
