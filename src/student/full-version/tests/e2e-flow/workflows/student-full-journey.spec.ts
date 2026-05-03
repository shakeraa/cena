// =============================================================================
// Cena E2E flow — full student journey diagnostic
//
// This spec is NOT a regression test. It's a single browser-driven walk-
// through that:
//
//   1. Drives the browser through the full happy-path: register →
//      onboarding → home → pricing → tier-card click → confirm.
//   2. Logs every console message, page error, and failed network request
//      to the test annotation stream so the user can see exactly what the
//      page is shouting about.
//   3. Doesn't fail-fast on warnings; it accumulates everything and dumps
//      a structured report at the end.
//
// Where the existing flagship specs short-circuit network-driven UI
// interactions to avoid races (e.g. tier-card-plus-cta → window.location
// redirect), THIS spec actually clicks the buttons and watches what
// happens — the goal is observation, not assertion.
//
// Run via:
//   cd src/student/full-version
//   E2E_FLOW_BASE_URL=http://localhost:5175 \
//   FIREBASE_EMU_HOST=localhost:9099 \
//   CENA_TEST_PROBE_TOKEN=dev-only-test-probe-token-do-not-ship \
//   npx playwright test --config playwright.e2e-flow.config.ts \
//     --workers=1 --grep "FULL_JOURNEY"
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry {
  type: string
  text: string
  location?: string
}

interface NetworkFailure {
  method: string
  url: string
  status: number
  body?: string
}

test.describe('STUDENT_FULL_JOURNEY', () => {
  test('register → onboarding → home → pricing → activate → confirm with diagnostics', async ({ page, browser }, testInfo) => {
    test.setTimeout(120_000)

    // ── Diagnostic accumulators ──
    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string; stack?: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', (msg) => {
      consoleEntries.push({
        type: msg.type(),
        text: msg.text(),
        location: msg.location()?.url
          ? `${msg.location().url}:${msg.location().lineNumber}`
          : undefined,
      })
    })

    page.on('pageerror', (err) => {
      pageErrors.push({ message: err.message, stack: err.stack })
    })

    page.on('response', async (resp) => {
      const status = resp.status()
      // 4xx + 5xx are interesting; 304 is normal cache; ignore the rest.
      if (status >= 400) {
        let body: string | undefined
        try {
          const text = await resp.text()
          body = text.length > 800 ? `${text.slice(0, 800)}…(truncated)` : text
        }
        catch {
          body = '<body unreadable: navigation likely lost the resource>'
        }
        failedRequests.push({
          method: resp.request().method(),
          url: resp.url(),
          status,
          body,
        })
      }
    })

    // ── Lock locale + seed the trusted-mode tenant id so the SPA's
    //    register flow can call /api/auth/on-first-sign-in successfully.
    //    Without `cena-e2e-tenant-id` the SPA sends an empty tenantId and
    //    the backend returns 400 silently — register.vue then surfaces a
    //    generic "auth.signInFailed" toast and stays on the credentials
    //    form. Same pattern student-register.spec.ts uses.
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `journey-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    console.log(`\n=== STUDENT_FULL_JOURNEY for ${email} ===\n`)

    // ── 1. Register page ──
    await page.goto('/register')
    await expect(page.getByTestId('age-gate-step')).toBeVisible({ timeout: 10_000 })

    // 25 years ago = adult, no parental consent
    const today = new Date()
    const dob = `${today.getUTCFullYear() - 25}-06-15`
    await page.getByTestId('age-gate-dob').locator('input').fill(dob)
    await expect(page.getByTestId('age-gate-adult')).toBeVisible()
    await page.getByTestId('age-gate-next').click()

    // ── 2. Credentials form ──
    await expect(page.getByTestId('email-password-form')).toBeVisible()
    await page.getByTestId('auth-display-name').locator('input').fill('Journey Tester')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()

    // ── 3. Wait for /onboarding (student-register journey ends here in A-01) ──
    await page.waitForURL(url => url.pathname.startsWith('/onboarding'), { timeout: 20_000 })
    await expect(page.getByTestId('onboarding-page')).toBeVisible({ timeout: 10_000 })

    // ── 4. Now drive to /pricing as the freshly registered student ──
    await page.goto('/pricing')
    await expect(page.getByTestId('pricing-page')).toBeVisible({ timeout: 10_000 })
    await expect(page.getByTestId('tier-card-plus-cta')).toBeVisible()
    await page.getByTestId('pricing-cycle-annual').click()

    // ── 5. CLICK the actual tier-card-plus-cta button ──
    // We accept that this triggers `window.location.href = data.url` and
    // navigates the page. Playwright's response listener captures the
    // /api/me/subscription/checkout-session POST + status before the
    // navigation, so we still have telemetry.
    const checkoutResponsePromise = page.waitForResponse(
      r => r.url().includes('/api/me/subscription/checkout-session') && r.request().method() === 'POST',
      { timeout: 15_000 },
    )
    // Also block the actual sandbox-checkout navigation so the SPA
    // doesn't drag the page off and abort our observability.
    await page.route('**://sandbox.checkout.cena.test/**', route => route.abort('aborted'))

    await page.getByTestId('tier-card-plus-cta').click()
    const checkoutResp = await checkoutResponsePromise
    console.log(`[journey] checkout-session POST -> ${checkoutResp.status()}`)

    // ── 6. Drive `/subscription/confirm` (real student would arrive here
    //      via the gateway's success_url). Since the dev sandbox uses
    //      /api/me/subscription/activate as the webhook simulator, we
    //      call activate ourselves first so the aggregate is Active by
    //      the time confirm-active polls.
    const tokenResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tokenResp.json() as { idToken: string }

    const me = await page.request.get(`${STUDENT_API}/api/me`, {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    const meBody = await me.json() as { studentId?: string }
    const studentId = meBody.studentId
    console.log(`[journey] /api/me studentId=${studentId}`)

    const activateResp = await page.request.post(`${STUDENT_API}/api/me/subscription/activate`, {
      headers: { Authorization: `Bearer ${idToken}` },
      data: {
        primaryStudentId: studentId,
        tier: 'Plus',
        billingCycle: 'Annual',
        paymentIdempotencyKey: `journey-${Date.now()}`,
      },
    })
    console.log(`[journey] /activate -> ${activateResp.status()}`)

    // Read the checkoutSession id that the captured response carried
    let sessionId: string | null = null
    try {
      const body = await checkoutResp.json() as { sessionId?: string }
      sessionId = body.sessionId ?? null
    }
    catch (e) {
      console.log(`[journey] checkout response body unreadable: ${(e as Error).message}`)
    }

    // ── 7. Land on /subscription/confirm ──
    if (sessionId) {
      await page.goto(`/subscription/confirm?session=${sessionId}`)
      await expect(page.getByTestId('subscription-confirm-active'))
        .toBeVisible({ timeout: 15_000 })
      console.log('[journey] /subscription/confirm-active visible ✓')
    }

    // ── 8. Try to start a study session — this is where most "as a
    //      student opens app" breakage shows up beyond auth/checkout.
    await page.goto('/home')
    const homeOk = await page.getByText('Home', { exact: false }).first().isVisible()
      .catch(() => false)
    console.log(`[journey] /home reached=${homeOk}`)

    // ── 9. Dump diagnostics into testInfo so they show in the report ──
    testInfo.attach('console-entries.json', {
      body: JSON.stringify(consoleEntries, null, 2),
      contentType: 'application/json',
    })
    testInfo.attach('page-errors.json', {
      body: JSON.stringify(pageErrors, null, 2),
      contentType: 'application/json',
    })
    testInfo.attach('failed-requests.json', {
      body: JSON.stringify(failedRequests, null, 2),
      contentType: 'application/json',
    })

    // Print a summary to stdout — easiest for the user to scan.
    console.log('\n=== DIAGNOSTICS SUMMARY ===')
    console.log(`Console entries:  ${consoleEntries.length} total`)
    const errs = consoleEntries.filter(e => e.type === 'error')
    const warns = consoleEntries.filter(e => e.type === 'warning')
    console.log(`  errors:   ${errs.length}`)
    console.log(`  warnings: ${warns.length}`)
    console.log(`Page errors:      ${pageErrors.length}`)
    console.log(`Failed requests:  ${failedRequests.length} (4xx/5xx)`)
    console.log()

    if (errs.length > 0) {
      console.log('— Console errors —')
      for (const e of errs.slice(0, 30))
        console.log(`  [${e.type}] ${e.text}${e.location ? ` @ ${e.location}` : ''}`)
      if (errs.length > 30)
        console.log(`  ... +${errs.length - 30} more`)
    }
    if (pageErrors.length > 0) {
      console.log('— Page errors (uncaught throws) —')
      for (const e of pageErrors.slice(0, 10))
        console.log(`  ${e.message}`)
    }
    if (failedRequests.length > 0) {
      console.log('— Failed network requests —')
      for (const f of failedRequests.slice(0, 30))
        console.log(`  ${f.status} ${f.method} ${f.url}`)
      if (failedRequests.length > 30)
        console.log(`  ... +${failedRequests.length - 30} more`)
    }
  })
})
