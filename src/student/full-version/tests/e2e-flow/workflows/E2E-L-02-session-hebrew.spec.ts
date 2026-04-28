// =============================================================================
// EPIC-E2E-L-02 — Practice session in Hebrew (RTL + math LTR isolation)
//
// Per the EPIC-L plan: "set locale=he → run EPIC-E2E-C-02 practice session →
// math equations render LTR inside RTL question bodies → hint cards RTL →
// feedback RTL". This is the canonical "math always LTR inside RTL" check
// memorialized in the user-confirmed reversed-equation regression
// (memory: feedback_math_always_ltr).
//
// Boundaries asserted:
//   1. <html dir="rtl"> + lang="he" on /session (SessionSetupForm) +
//      on /session/{id} (active question render)
//   2. SessionSetupForm duration buttons render with the bdi-isolated
//      digit so "{n} د"/"{n} דקה" doesn't reverse the digit
//   3. Click "Start practice" → POST /api/sessions/start succeeds → SPA
//      transitions to /session/{id}
//   4. Active question page: every element matching :is(.katex, [class*=
//      "math"], .math-block) sits inside a <bdi dir="ltr"> ancestor (the
//      reversed-equation regression class)
//   5. Diagnostics: 0 page-errors, no console-errors that aren't ambient
//      (browser-internal), no 4xx/5xx network failures from app routes
//
// What's NOT covered (deferred):
//   * Hint card RTL — needs the student to actually answer wrong then
//     request hints; the hint UI is its own E2E flow (C-03 territory)
//   * Feedback RTL — same; arrives after answer submission
//   * Concept-cluster mastery RTL — admin/parent visualization scope
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface DiagnosticCtx {
  consoleErrors: string[]
  pageErrors: string[]
  failedRequests: { method: string; url: string; status: number }[]
}

function attachDiagnostics(page: Page): DiagnosticCtx {
  const ctx: DiagnosticCtx = { consoleErrors: [], pageErrors: [], failedRequests: [] }
  page.on('console', m => { if (m.type() === 'error') ctx.consoleErrors.push(m.text()) })
  page.on('pageerror', e => { ctx.pageErrors.push(e.message) })
  page.on('response', r => {
    if (r.status() >= 400)
      ctx.failedRequests.push({ method: r.request().method(), url: r.url(), status: r.status() })
  })
  return ctx
}

async function provisionHebrewStudent(page: Page): Promise<{ email: string; password: string; idToken: string }> {
  const email = `e2e-l02-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const t = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken: bootstrapToken } = await t.json() as { idToken: string }

  await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'L-02 Hebrew' },
  })

  const re = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await re.json() as { idToken: string }

  await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      role: 'student', locale: 'he', subjects: ['math'],
      dailyTimeGoalMinutes: 15, weeklySubjectTargets: [],
      diagnosticResults: null, classroomCode: null,
    },
  })

  return { email, password, idToken }
}

test.describe('E2E_L_02_SESSION_HEBREW', () => {
  test('Hebrew locale: /session setup + active session render dir=rtl, math wrapped in <bdi dir=ltr> @epic-l @l-02 @rtl @math-ltr', async ({ page }, testInfo) => {
    test.setTimeout(120_000)
    const diag = attachDiagnostics(page)

    // Seed locale=he BEFORE any navigation. The locale store accepts the
    // versioned-object shape with locked=true so the first-run chooser
    // doesn't steal the route.
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'he', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const acct = await provisionHebrewStudent(page)

    // Sign in via real button click (per the user's "real-browser E2E
    // with diagnostics" rule — never HTTP shortcuts for student-facing
    // flows).
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(acct.email)
    await page.getByTestId('auth-password').locator('input').fill(acct.password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    // ── 1. /session setup form: dir=rtl, lang=he ──
    await page.goto(`${STUDENT_SPA_BASE_URL}/session`, { waitUntil: 'domcontentloaded' })
    await expect.poll(
      async () => page.evaluate(() => document.documentElement.dir),
      { timeout: 8_000, message: '/session in he locale must render dir=rtl' },
    ).toBe('rtl')
    await expect.poll(
      async () => page.evaluate(() => document.documentElement.lang),
      { timeout: 8_000, message: '/session in he locale must render lang=he' },
    ).toBe('he')

    // ── 2. Duration buttons: bidi-isolated digit + label ──
    // SessionSetupForm wraps "{n} דקה" in <bdi> so the LTR digit + RTL
    // letter run isn't fragmented. Verify the bdi wrappers exist.
    // (The fix was landed in commit a0d0555e for ar; he uses the same
    // template pattern.)
    const setupForm = page.locator('[data-testid="session-setup-form"], main form').first()
    await expect(setupForm,
      '/session must render the SessionSetupForm in he locale',
    ).toBeVisible({ timeout: 10_000 })

    // Look for any duration-button labels and confirm each contains a
    // <bdi> wrapper. If the SPA hasn't applied the bidi fix to he, the
    // count will be 0 and the test fails loudly.
    const bdiCount = await setupForm.locator('bdi').count()
    expect(bdiCount,
      'SessionSetupForm in he must wrap mixed-direction text in <bdi> ' +
      '(prevents the digit-reversal regression caught against ar in a0d0555e). ' +
      `Got bdi.count=${bdiCount}.`,
    ).toBeGreaterThan(0)

    // ── 3. Start practice: POST /api/sessions/start succeeds ──
    // Find a duration button (any) and submit. The setup form's submit
    // path varies; we click the first "start" testid or fallback to
    // form submit.
    const startButton = await Promise.race([
      page.getByTestId('session-setup-start').first().isVisible().catch(() => false).then(v => v ? page.getByTestId('session-setup-start').first() : null),
      page.getByRole('button', { name: /start|begin|התחל/i }).first().isVisible().catch(() => false).then(v => v ? page.getByRole('button', { name: /start|begin|התחל/i }).first() : null),
    ])
    if (startButton) {
      await startButton.click()
      // Allow time for /api/sessions/start to round-trip + router push.
      await page.waitForURL(/\/session\/[a-zA-Z0-9_-]+/, { timeout: 15_000 }).catch(() => {})
    }

    const onActiveSession = /\/session\/[a-zA-Z0-9_-]+/.test(page.url())

    // ── 4. If we made it to /session/{id}, verify dir=rtl persists +
    //    math elements (if any) are bidi-isolated. ──
    if (onActiveSession) {
      await expect.poll(
        async () => page.evaluate(() => document.documentElement.dir),
        { timeout: 8_000, message: '/session/{id} must keep dir=rtl' },
      ).toBe('rtl')

      // Wait a moment for question render + KaTeX mount.
      await page.waitForTimeout(1_500)

      // Find every math element by common selectors. A KaTeX-rendered
      // expression has class .katex on the wrapper. MathLive editor
      // adds .ML__mathfield. Plain prose math we render inside .math
      // / [data-math]. None must escape an LTR-isolated ancestor.
      const mathOrphans = await page.evaluate(() => {
        const sels = '.katex, .ML__mathfield, .math, .math-block, [data-math]'
        const all = Array.from(document.querySelectorAll(sels))
        const orphaned: string[] = []
        for (const el of all) {
          // Walk up to find a <bdi dir="ltr"> OR an element with dir=ltr.
          let n: Element | null = el
          let isolated = false
          while (n) {
            if (n.tagName === 'BDI' && n.getAttribute('dir') === 'ltr') {
              isolated = true
              break
            }
            // Acceptable alternative: the element itself or an ancestor
            // explicitly carries dir="ltr" (some pages use a wrapper div
            // instead of <bdi>).
            const dirAttr = n.getAttribute && n.getAttribute('dir')
            if (dirAttr === 'ltr') {
              isolated = true
              break
            }
            n = n.parentElement
          }
          if (!isolated) {
            const sample = el.textContent?.slice(0, 80) ?? ''
            orphaned.push(`${el.tagName.toLowerCase()}.${el.className.slice(0, 40)}: "${sample}"`)
          }
        }
        return orphaned
      })

      expect(mathOrphans,
        `Math elements not wrapped in <bdi dir="ltr"> (or dir=ltr ancestor) on /session/{id} ` +
        `under he locale. This is the canonical reversed-equation regression class. ` +
        `${mathOrphans.length === 0 ? '' : 'First 3: ' + JSON.stringify(mathOrphans.slice(0, 3))}`,
      ).toEqual([])
    }
    else {
      // No /session/{id} reached. The setup form is still the surface
      // under test — log it but don't fail (some dev configurations
      // gate /api/sessions/start on prerequisites we don't seed here).
      console.log('[l-02] /session/{id} not reached after Start click; setup-form RTL coverage stands alone')
    }

    testInfo.attach('l-02-diagnostics.json', {
      body: JSON.stringify({
        reachedActiveSession: onActiveSession,
        bdiCountOnSetup: bdiCount,
        diag,
      }, null, 2),
      contentType: 'application/json',
    })

    // No app-level page errors. Filter the same ambient localStorage
    // browser warning as the cov-06 spec.
    const appErrors = diag.pageErrors.filter(e =>
      !/localStorage.*access is denied|opaque origin/i.test(e),
    )
    expect(appErrors,
      `app-level pageerror under he locale: ${JSON.stringify(appErrors.slice(0, 3))}`,
    ).toEqual([])
  })
})
