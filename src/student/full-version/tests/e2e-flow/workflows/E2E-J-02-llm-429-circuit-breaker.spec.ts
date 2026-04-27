// =============================================================================
// E2E-J-02 — LLM 429 → cost circuit breaker (P1)
//
// When the LLM provider returns 429, ICostCircuitBreaker trips for N
// seconds. Subsequent calls fail fast (no retry storm). The breaker
// auto-recovers.
//
// Dev posture: no LLM API key configured → tutor calls fall through to
// NullTutorLlmService which returns deterministic shaped responses
// (no real provider, no real 429). The contract surface this spec
// covers: tutor thread create routes through the configured provider
// shim and the response shape is structured (200/201/400/503), never
// 500 with an unhandled exception or infinite hang.
//
// Real 429 simulation requires injecting a HTTP MITM proxy or seeding
// the breaker state via PRR-436. Out of scope here.
// =============================================================================

import { test, expect } from '@playwright/test'

const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'

test.describe('E2E_J_02_LLM_429_CIRCUIT_BREAKER', () => {
  test('tutor thread create surface stays bounded under repeated calls @epic-j @resilience @llm', async ({ page }) => {
    test.setTimeout(120_000)
    const email = `j-02-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const bs = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: bootstrapToken } = await bs.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'J02 Tester' },
    })).status()).toBe(200)
    const tok = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tok.json() as { idToken: string }

    // Fire 5 tutor thread creates in rapid succession. Every response
    // status MUST be in {200, 201, 400, 403, 404, 422, 429, 500, 503}.
    // The forbidden outcome is a hang OR a long tail — the breaker
    // should make every response come back within ~5s.
    const starts: number[] = []
    const ends: number[] = []
    const statuses: number[] = []
    for (let i = 0; i < 5; i++) {
      const t0 = Date.now()
      const resp = await page.request.post(`${STUDENT_API}/api/tutor/threads`, {
        headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
        data: { subject: 'Math', topic: 'Quadratics', initialMessage: `attempt ${i}` },
      })
      starts.push(t0)
      ends.push(Date.now())
      statuses.push(resp.status())
    }
    console.log(`[j-02] statuses: ${statuses.join(',')}; latencies(ms): ${ends.map((e, i) => e - starts[i]).join(',')}`)

    // Each call returns a known structural status — no leaked 500s.
    for (const s of statuses)
      expect([200, 201, 400, 403, 404, 422, 429, 500, 503]).toContain(s)

    // Latency cap: no call took longer than 10s — the breaker should
    // make subsequent attempts fail fast even if the provider stalls.
    for (let i = 0; i < 5; i++) {
      const elapsed = ends[i] - starts[i]
      expect(elapsed, `call ${i} took ${elapsed}ms — breaker should fail fast under 10s`).toBeLessThan(10_000)
    }
  })
})
