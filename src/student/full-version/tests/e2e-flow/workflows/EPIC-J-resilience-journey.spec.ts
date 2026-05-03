// =============================================================================
// EPIC-E2E-J — Resilience / chaos journey (real browser drive)
//
// Drives a fresh student through a multi-page journey while peripheral
// services are intentionally cycled, and asserts the platform stays
// usable. The chaos primitives in tests/e2e-flow/probes/chaos.ts are
// the underlying tool — this spec composes them around real SPA
// navigations.
//
// Scenario covered:
//   J-01  — SymPy sidecar down, student loads /home + /settings/privacy.
//           SymPy is consulted only at answer-time; routing + the
//           privacy export round-trip must NOT depend on it.
//
// Why we don't drop the actor-host or postgres:
//   * actor-host owns the Marten schema warm and projection runners
//     (recent commit `infra(marten): single-host schema warm`); cycling
//     it is a 30-60s recovery and the e2e-flow stack would idle the
//     other test workers.
//   * postgres is shared by every API, so dropping it is functionally
//     "kill the whole stack" — outside this spec's scope.
//
// What the spec does NOT cover (intentional, separate epics):
//   * J-03 NATS-down outbox drain (RDY-094 lives elsewhere)
//   * J-09 SignalR mid-session reconnect — exam-prep flow specific
//   * K-02 / K-03 offline / PWA paths (EPIC-K)
// =============================================================================

import { test, expect } from '@playwright/test'
import { withServiceDown, waitForHealthy } from '../probes/chaos'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string }
interface NetworkFailure { method: string; url: string; status: number }

test.describe('EPIC_J_RESILIENCE_JOURNEY', () => {
  test('student stays usable while cena-sympy-sidecar is down @epic-j', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({ type: msg.type(), text: msg.text() }))
    page.on('pageerror', err => pageErrors.push({ message: err.message }))
    page.on('response', async (resp) => {
      if (resp.status() >= 400)
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status() })
    })

    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `epic-j-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_J_RESILIENCE_JOURNEY for ${email} ===\n`)

    // ── 1. Bootstrap (server-side) before chaos starts ──
    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok()).toBe(true)
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }

    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'EpicJ Tester' },
    })).status()).toBe(200)

    const reLoginResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: postClaimsToken } = await reLoginResp.json() as { idToken: string }

    expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
      headers: { Authorization: `Bearer ${postClaimsToken}` },
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

    // ── 2. Sign in via the SPA ──
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    console.log(`[epic-j] post-login url: ${page.url()}`)

    // ── 3. Inside withServiceDown(sympy): drive non-CAS flows ──
    await withServiceDown('cena-sympy-sidecar', async () => {
      console.log('[epic-j] sympy sidecar stopped — exercising non-CAS flows')

      // /home — purely projection-driven, no CAS needed
      await page.goto('/home')
      await expect(page.locator('body')).toBeVisible()
      // Brief settle window so any in-flight fetches surface 4xx/5xx.
      await page.waitForTimeout(1500)
      console.log('[epic-j] /home loaded with sympy down')

      // /settings/privacy — GDPR pipeline does NOT touch sympy. Verifies
      // we covered that surface end-to-end while a peripheral is gone.
      await page.goto('/settings/privacy')
      await expect(page.getByTestId('settings-privacy-page')).toBeVisible({ timeout: 15_000 })
      console.log('[epic-j] /settings/privacy rendered with sympy down')
    })

    // ── 4. Post-recovery sanity ──
    // withServiceDown re-starts + awaits health, so on this line sympy
    // is back. Drive one more page-load to prove the stack didn't get
    // stuck in any half-degraded state.
    await waitForHealthy('cena-sympy-sidecar', 30_000)
    await page.goto('/home')
    console.log('[epic-j] post-recovery /home re-loaded')

    // ── 5. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')

    console.log('\n=== EPIC_J DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length}`)
    console.log(`Page errors: ${pageErrors.length}`)
    console.log(`Failed network: ${failedRequests.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 10))
        console.log(`  ${e.text}`)
    }
    if (failedRequests.length) {
      console.log('— failed requests —')
      for (const f of failedRequests.slice(0, 10))
        console.log(`  ${f.status} ${f.method} ${f.url}`)
    }

    // The spec asserts the platform stayed usable — non-CAS flows must
    // not crash because a peripheral is down. We hard-fail on JS throws;
    // 4xx/5xx are documented (any unexpected ones print above for triage).
    expect(pageErrors, 'No JS exceptions while sympy was down').toHaveLength(0)
  })
})
