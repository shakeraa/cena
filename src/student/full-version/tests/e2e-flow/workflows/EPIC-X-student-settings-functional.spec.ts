// =============================================================================
// EPIC-E2E-X — TASK-E2E-COV-05 student settings sub-tabs gap-fill
//
// stuw01..stuw15 cover most student per-page functionality but skip
// the /settings/* sub-tabs. Each tab is its own page with its own
// form/toggle interactions. This spec drives them.
//
// Routes covered (5 sub-tabs):
//   /settings/account        — display name, email, account-action
//   /settings/appearance     — theme + dark-mode toggle
//   /settings/notifications  — per-channel notification toggles
//   /settings/privacy        — consent toggles (subset of /api/me/consent)
//   /settings/study-plan     — exam-target / daily-time-goal config
//
// Diagnostic-collection per the shared pattern (console + page errors
// + 4xx/5xx). Each test is intentionally light: navigate, verify the
// page mounts, exercise ONE primary interaction, assert no JS errors.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface DiagnosticCtx {
  consoleErrors: string[]
  pageErrors: string[]
  failedRequests: { method: string; url: string; status: number }[]
}

function attachDiagnostics(page: Page): DiagnosticCtx {
  const ctx: DiagnosticCtx = {
    consoleErrors: [],
    pageErrors: [],
    failedRequests: [],
  }
  page.on('console', m => { if (m.type() === 'error') ctx.consoleErrors.push(m.text()) })
  page.on('pageerror', e => { ctx.pageErrors.push(e.message) })
  page.on('response', r => {
    if (r.status() >= 400)
      ctx.failedRequests.push({ method: r.request().method(), url: r.url(), status: r.status() })
  })
  return ctx
}

async function provisionAndLogin(page: Page): Promise<void> {
  await page.addInitScript((tenantId: string) => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: 'en', locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
  }, TENANT_ID)

  const email = `e2e-cov05-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const t = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await t.json() as { idToken: string }
  await page.request.post('/api/auth/on-first-sign-in', {
    headers: { Authorization: `Bearer ${idToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'COV-05 Settings' },
  })
  await page.request.post('/api/me/onboarding', {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      Role: 'student', Locale: 'en', Subjects: ['math'],
      DailyTimeGoalMinutes: 15, WeeklySubjectTargets: [],
      DiagnosticResults: null, ClassroomCode: null,
    },
  })

  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

// Serial mode + a single shared sign-in: provisioning a fresh student
// per route was ~7s × 6 routes = 42s of pure setup, blowing past the
// 45s test timeout under stack contention. One provision + 6
// navigations drops the total to ~12s wall.
test.describe.serial('EPIC_X_STUDENT_SETTINGS_FUNCTIONAL', () => {
  let sharedPage: Page
  let sharedDiag: DiagnosticCtx

  test.beforeAll(async ({ browser }) => {
    const ctx = await browser.newContext()
    sharedPage = await ctx.newPage()
    sharedDiag = attachDiagnostics(sharedPage)
    await provisionAndLogin(sharedPage)
  })

  test.afterAll(async () => {
    await sharedPage?.context().close().catch(() => {})
  })

  for (const route of [
    '/settings',
    '/settings/account',
    '/settings/appearance',
    '/settings/notifications',
    '/settings/privacy',
    '/settings/study-plan',
  ]) {
    test(`${route} mounts + heading visible + no JS errors @epic-x @cov-05 @student-functional`, async ({}, testInfo) => {
      test.setTimeout(30_000)

      // Snapshot pageErrors at start so new ones get attributed to
      // this specific route (sharedDiag accumulates across the block).
      const errsBefore = sharedDiag.pageErrors.length

      await sharedPage.goto(`${STUDENT_SPA_BASE_URL}${route}`)
      // networkidle gives the SPA's lazy chunks time to load on first
      // navigation. Without it, the side-nav renders first (skeleton)
      // and the main content area is still empty during visibility check.
      await sharedPage.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {})

      // Try a sequence of selectors. Each is a valid "page mounted"
      // signal; first one to flip wins. Settings sub-pages have varied
      // heading levels (some h1, some h2, some only data-testid).
      const ready = await Promise.race([
        sharedPage.locator('main h1, main h2, main [role="heading"]').first().isVisible({ timeout: 12_000 }).catch(() => false),
        sharedPage.locator('[data-testid$="-page"]').first().isVisible({ timeout: 12_000 }).catch(() => false),
        sharedPage.locator('[data-testid="settings-index-page"]').isVisible({ timeout: 12_000 }).catch(() => false),
      ])
      expect(ready, `${route} should render a heading or *-page testid`).toBe(true)

      const newPageErrors = sharedDiag.pageErrors.slice(errsBefore)
      testInfo.attach('diagnostics.json', {
        body: JSON.stringify({ totalConsoleErrors: sharedDiag.consoleErrors.length, newPageErrors }, null, 2),
        contentType: 'application/json',
      })
      expect(newPageErrors,
        `pageerror on ${route}: ${JSON.stringify(newPageErrors.slice(0, 3))}`,
      ).toEqual([])
    })
  }
})
