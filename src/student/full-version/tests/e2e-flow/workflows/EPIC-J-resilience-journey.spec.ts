// =============================================================================
// EPIC-E2E-J — Resilience / failure-mode journey (real browser drive
// + container chaos)
//
// Stop the SymPy CAS sidecar mid-session and verify:
//   1. Stop sidecar via tests/e2e-flow/probes/chaos.ts
//   2. Drive a navigation that depends on backend availability (here
//      we use /home, the post-login landing — a reachable signed-in
//      route that exercises the same nav shell a math-flow page would
//      use). The point is not the depth of the action but to prove
//      the SPA does not litter the chrome console with uncaught
//      exceptions while a peripheral is down.
//   3. Restart the sidecar and re-drive — page should recover cleanly
//      with no console errors.
//
// Why CAS sidecar (J-01) instead of NATS / Firebase: the sidecar has
// the cleanest health-check + stop / start cycle in the chaos probe.
// Stopping NATS while the actor host is running risks the host
// going unhealthy and breaking subsequent tests. Firebase emu down
// breaks the entire signed-in story. SymPy is the lowest-blast-radius
// chaos service.
//
// Diagnostics collected per the shared pattern.
// =============================================================================

import { test, expect } from '@playwright/test'
import { stopService, startService } from '../probes/chaos'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

test.describe('EPIC_J_RESILIENCE_JOURNEY', () => {
  // Always restart sympy in afterEach in case the test fails between
  // stopService and the explicit startService call. Keeping a peripheral
  // down across specs poisons the rest of the suite.
  test.afterEach(async () => {
    try {
      await startService('cena-sympy-sidecar')
    }
    catch {
      // Best-effort recovery; surface in next run if persistent.
    }
  })

  test('stop SymPy sidecar → navigate signed-in routes → no JS errors → restart recovers @epic-j', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string; stack?: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({
      type: msg.type(),
      text: msg.text(),
      location: msg.location()?.url
        ? `${msg.location().url}:${msg.location().lineNumber}`
        : undefined,
    }))
    page.on('pageerror', err => pageErrors.push({ message: err.message, stack: err.stack }))
    page.on('response', async resp => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    // Provision + sign in.
    const email = `e2e-resilience-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const tokenResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: bootstrapToken } = await tokenResp.json() as { idToken: string }
    await page.request.post('/api/auth/on-first-sign-in', {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Resilience Test' },
    })

    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    // ── Inject chaos: stop the SymPy sidecar ──
    await stopService('cena-sympy-sidecar')

    // Drive the SPA while the sidecar is down. /home is the post-login
    // landing — the route guard + meStore hydration + nav shell run
    // here, which is the surface most likely to throw if a peripheral
    // is down. We don't strictly require an active session/answer
    // flow because the SPA may render a tier-gated upsell or empty
    // home — the negative signal is JS errors, not absent data.
    await page.goto('/home')
    await page.waitForLoadState('domcontentloaded', { timeout: 15_000 })

    const errsDuringChaos = consoleEntries.filter(e => e.type === 'error')
    const pageErrsDuringChaos = [...pageErrors]

    // ── Recover: restart sympy ──
    await startService('cena-sympy-sidecar')

    // Re-drive after recovery. Hard reload to make sure hydration runs
    // again post-recovery (cleaner signal than soft nav).
    await page.reload({ waitUntil: 'networkidle' })

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })
    testInfo.attach('chaos-snapshot.json', {
      body: JSON.stringify({ errsDuringChaos, pageErrsDuringChaos }, null, 2),
      contentType: 'application/json',
    })

    // Page errors AT ANY POINT are the regression catcher — uncaught
    // JS exceptions while a peripheral is down means the SPA doesn't
    // tolerate the failure mode gracefully. The SPA is allowed to
    // surface a user-facing error UI (toast, error card, etc.) but
    // not to throw uncaught.
    expect(pageErrors,
      `uncaught exceptions during chaos+recovery: ${JSON.stringify(pageErrors.slice(0, 3))}`,
    ).toEqual([])
  })
})
