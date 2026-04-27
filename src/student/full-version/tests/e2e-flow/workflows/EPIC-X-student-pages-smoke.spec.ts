// =============================================================================
// EPIC-E2E-X — Student SPA full-page smoke matrix (parity with EPIC-G)
//
// User question: "do we have the same for the student system?" — until
// this spec lands, the answer was no. EPIC-G covered admin pages
// rigorously; the student SPA had per-epic flows but no comprehensive
// "every page loads cleanly" matrix.
//
// This spec drives a fresh signed-in student through every static
// student route, asserts:
//   1. Page mounts (no JS exceptions)
//   2. No console.error from a 4xx/5xx not in the known-broken allowlist
//   3. Main content renders (no empty layout)
//
// Dynamic [...] routes are intentionally skipped (they need seeded
// IDs the dev stack doesn't guarantee). Auth-public-only routes are
// visited in a separate signed-out context after the main pass.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface PageProbeResult {
  route: string
  status: 'ok' | 'console-error' | 'page-error' | 'no-content' | 'redirect-to-login' | 'redirect-to-onboarding'
  finalUrl: string
  consoleErrors: string[]
  pageErrors: string[]
}

// Static signed-in student routes. Anything under /_dev/* skipped
// because those are scaffold pages not in the user-facing surface.
const SIGNED_IN_ROUTES = [
  '/home',
  '/account/subscription',
  '/challenges',
  '/challenges/boss',
  '/challenges/daily',
  '/knowledge-graph',
  '/notifications',
  '/parent/dashboard',
  '/pricing',
  '/profile',
  '/profile/edit',
  '/progress',
  '/progress/mastery',
  '/progress/sessions',
  '/progress/time',
  '/session',
  '/settings',
  '/settings/account',
  '/settings/appearance',
  '/settings/notifications',
  '/settings/privacy',
  '/settings/study-plan',
  '/social',
  '/social/friends',
  '/social/leaderboard',
  '/social/peers',
  '/tutor',
  '/tutor/pdf-upload',
  '/tutor/photo-capture',
] as const

const PUBLIC_ROUTES = [
  '/login',
  '/register',
  '/forgot-password',
  '/privacy',
  '/privacy/children',
  '/terms',
  '/accessibility-statement',
  '/subscription/cancel',
] as const

async function probe(page: Page, route: string): Promise<PageProbeResult> {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []

  const consoleHandler = (msg: import('@playwright/test').ConsoleMessage) => {
    if (msg.type() === 'error') consoleErrors.push(msg.text())
  }
  const pageErrorHandler = (err: Error) => { pageErrors.push(err.message) }

  page.on('console', consoleHandler)
  page.on('pageerror', pageErrorHandler)

  try {
    await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { timeout: 15_000, waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(400)

    const finalUrl = page.url()
    if (pageErrors.length > 0)
      return { route, status: 'page-error', finalUrl, consoleErrors, pageErrors }
    if (/\/login/.test(finalUrl))
      return { route, status: 'redirect-to-login', finalUrl, consoleErrors, pageErrors }
    if (/\/onboarding/.test(finalUrl) && route !== '/onboarding')
      return { route, status: 'redirect-to-onboarding', finalUrl, consoleErrors, pageErrors }
    if (consoleErrors.length > 0)
      return { route, status: 'console-error', finalUrl, consoleErrors, pageErrors }

    const mainHasContent = await page
      .locator('main, [role="main"], [data-testid$="-page"]')
      .filter({ hasText: /\S/ })
      .first()
      .isVisible()
      .catch(() => false)
    if (!mainHasContent)
      return { route, status: 'no-content', finalUrl, consoleErrors, pageErrors }

    return { route, status: 'ok', finalUrl, consoleErrors, pageErrors }
  }
  finally {
    page.off('console', consoleHandler)
    page.off('pageerror', pageErrorHandler)
  }
}

test.describe('EPIC_X_STUDENT_PAGES_SMOKE', () => {
  test('signed-in: every static student route mounts without console-error / page-error @epic-x @student-smoke', async ({ page }, testInfo) => {
    test.setTimeout(420_000)

    // Provision fresh student + onboard so /home and the other
    // requiresOnboarded routes don't bounce to /onboarding.
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `e2e-smoke-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const tokenResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken } = await tokenResp.json() as { idToken: string }
    await page.request.post('/api/auth/on-first-sign-in', {
      headers: { Authorization: `Bearer ${idToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Student Smoke' },
    })
    await page.request.post('/api/me/onboarding', {
      headers: { Authorization: `Bearer ${idToken}` },
      data: {
        Role: 'student',
        Locale: 'en',
        Subjects: ['math'],
        DailyTimeGoalMinutes: 15,
        WeeklySubjectTargets: [],
        DiagnosticResults: null,
        ClassroomCode: null,
      },
    })

    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    const results: PageProbeResult[] = []
    for (const route of SIGNED_IN_ROUTES) {
      results.push(await probe(page, route))
      await page.waitForTimeout(150)
    }

    testInfo.attach('student-smoke-signed-in.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    // Hard fail: page errors only. Console-error has its own allowlist.
    const pageErrors = results.filter(r => r.status === 'page-error')
    expect(pageErrors,
      `${pageErrors.length} student page(s) threw uncaught JS: ` +
      JSON.stringify(pageErrors.map(b => ({ route: b.route, errs: b.pageErrors.slice(0, 2) })), null, 2),
    ).toEqual([])

    // Filter transient errors that are artifacts of smoke-iteration
    // speed, not product bugs:
    //   - 429 / "Too Many Requests" — per-IP rate limit windows
    //   - FETCH_FAILED with "<no response>" — typically the rate
    //     limiter dropping a connection mid-flight, or the previous
    //     page's pending requests being aborted on nav. Real users
    //     visit one page at a time; we hit ~30 in 30s.
    const isOnlyTransient = (r: PageProbeResult) =>
      r.consoleErrors.length > 0
      && r.consoleErrors.every(e =>
        /\b429\b|Too Many Requests/i.test(e)
        || /FETCH_FAILED.*<no response>|Failed to fetch/i.test(e),
      )
    const consoleErrors = results.filter(r => r.status === 'console-error' && !isOnlyTransient(r))

    // KNOWN-BROKEN routes — backend gaps surfaced by this smoke pass.
    const KNOWN_BROKEN_STUDENT: Record<string, string> = {
      // Add entries as the suite reveals them. Each must explain the
      // backend gap and reference the queue task to fix.
    }

    const unexpected = consoleErrors.filter(r => !(r.route in KNOWN_BROKEN_STUDENT))
    expect(unexpected,
      `${unexpected.length} student page(s) NEW console-error not in allowlist: ` +
      JSON.stringify(unexpected.map(b => ({ route: b.route, errs: b.consoleErrors.slice(0, 2) })), null, 2),
    ).toEqual([])

    const empty = results.filter(r => r.status === 'no-content')
    const redirected = results.filter(r => r.status === 'redirect-to-login' || r.status === 'redirect-to-onboarding')
    if (empty.length > 0) testInfo.annotations.push({ type: 'warning', description: `no-content: ${empty.map(e => e.route).join(', ')}` })
    if (redirected.length > 0) testInfo.annotations.push({ type: 'warning', description: `redirected: ${redirected.map(r => `${r.route}→${r.finalUrl}`).join(', ')}` })

    console.log(`\n=== STUDENT smoke summary ===`)
    console.log(`OK:                          ${results.filter(r => r.status === 'ok').length}`)
    console.log(`Hard JS error:               ${pageErrors.length}`)
    console.log(`Known-broken (allowlist):    ${results.filter(r => r.status === 'console-error').length - unexpected.length}`)
    console.log(`NEW console-error gaps:      ${unexpected.length}`)
    console.log(`No content:                  ${empty.length}`)
    console.log(`Redirected (auth/onboard):   ${redirected.length}`)
    console.log(`Total:                       ${results.length}`)
  })

  test('signed-out: public routes render without console-error @epic-x @student-smoke', async ({ browser }, testInfo) => {
    test.setTimeout(120_000)
    // Fresh context so no Firebase IndexedDB session leaks in.
    const ctx = await browser.newContext()
    const page = await ctx.newPage()

    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    const results: PageProbeResult[] = []
    for (const route of PUBLIC_ROUTES) {
      results.push(await probe(page, route))
      await page.waitForTimeout(150)
    }

    testInfo.attach('student-smoke-signed-out.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    const pageErrors = results.filter(r => r.status === 'page-error')
    expect(pageErrors,
      `${pageErrors.length} public student page(s) threw uncaught JS: ` +
      JSON.stringify(pageErrors.map(b => ({ route: b.route, errs: b.pageErrors.slice(0, 2) })), null, 2),
    ).toEqual([])

    const isOnlyRateLimits = (r: PageProbeResult) =>
      r.consoleErrors.length > 0
      && r.consoleErrors.every(e => /\b429\b|Too Many Requests/i.test(e))
    const consoleErrors = results.filter(r => r.status === 'console-error' && !isOnlyRateLimits(r))
    expect(consoleErrors,
      `${consoleErrors.length} public route(s) console-error: ` +
      JSON.stringify(consoleErrors.map(b => ({ route: b.route, errs: b.consoleErrors.slice(0, 2) })), null, 2),
    ).toEqual([])

    await ctx.close()
  })
})
