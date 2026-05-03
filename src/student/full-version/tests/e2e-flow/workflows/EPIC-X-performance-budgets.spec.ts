// =============================================================================
// EPIC-E2E-X — Performance budgets per page
//
// User asked for "correctness + functionality + performance + visibility"
// per each page. Smoke covers correctness; responsive sweep covers
// visibility; this spec adds the performance dimension.
//
// We measure browser-native performance metrics on a canonical set of
// pages (one signed-in student page per area, plus the admin SPA's
// dashboard). Metrics:
//
//   - FCP  (First Contentful Paint)   — time to first non-blank pixel
//   - LCP  (Largest Contentful Paint) — time to main content
//   - DCL  (DOMContentLoaded)         — time to parsed DOM
//   - LOAD (load event)               — time to all subresources loaded
//   - TBT  (Total Blocking Time)      — main-thread blocking ≥50ms
//
// Budgets are intentionally GENEROUS for dev (Vite dev mode is not the
// production build). The point is regression-catching: if a page goes
// from 1.2s LCP to 4.5s LCP, we want to know.
//
// Hard-fail thresholds (dev-mode):
//   FCP  ≤ 4 s   (production budget would be ≤ 1.5 s)
//   LCP  ≤ 8 s   (production budget would be ≤ 2.5 s)
//   LOAD ≤ 15 s
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

interface Metrics {
  url: string
  fcp: number
  lcp: number
  dcl: number
  load: number
  tbt: number
}

const BUDGETS = {
  fcp: 4_000,
  lcp: 8_000,
  load: 15_000,
} as const

async function measure(page: Page, url: string): Promise<Metrics> {
  await page.goto(url, { timeout: 20_000, waitUntil: 'load' })
  // Allow LCP candidates to settle (Largest Contentful Paint is
  // observed via PerformanceObserver and may fire after `load`).
  await page.waitForTimeout(1500)

  const metrics = await page.evaluate(() => {
    // FCP via paint entry.
    const paints = performance.getEntriesByType('paint') as PerformancePaintTiming[]
    const fcp = paints.find(p => p.name === 'first-contentful-paint')?.startTime ?? -1

    // LCP via the buffered observer (the browser maintains entries
    // even after we navigate, but only within this document context).
    let lcp = -1
    try {
      const lcpEntries = performance.getEntriesByType('largest-contentful-paint') as PerformanceEntry[]
      lcp = lcpEntries.length > 0 ? lcpEntries[lcpEntries.length - 1].startTime : -1
    }
    catch {
      lcp = -1
    }

    const navTiming = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined
    const dcl = navTiming?.domContentLoadedEventEnd ?? -1
    const load = navTiming?.loadEventEnd ?? -1

    // TBT approximation: sum of (long-task duration - 50ms) for
    // long-task entries during the load window.
    const longTasks = performance.getEntriesByType('longtask') as PerformanceEntry[]
    const tbt = longTasks.reduce((acc, t) => acc + Math.max(0, t.duration - 50), 0)

    return { fcp, lcp, dcl, load, tbt }
  })

  return { url, ...metrics }
}

async function provisionAndLogin(page: Page): Promise<void> {
  await page.addInitScript((tenantId: string) => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: 'en', locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
  }, TENANT_ID)

  const email = `e2e-perf-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Perf Test' },
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

test.describe('EPIC_X_PERFORMANCE_BUDGETS', () => {
  test('student canonical pages stay within performance budgets @epic-x @perf @student', async ({ page }, testInfo) => {
    test.setTimeout(180_000)
    await provisionAndLogin(page)

    // COV-01: full route matrix. Each route gets ~3 s in `measure`
    // (goto + 1.5 s LCP-settle + perf-entry read). 29 routes × 3 s
    // ≈ 90 s wall, which fits inside the 180 s test timeout.
    const STUDENT_PATHS = [
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
    ]
    const PAGES = STUDENT_PATHS.map(p => `${STUDENT_SPA_BASE_URL}${p}`)

    const all: Metrics[] = []
    for (const url of PAGES) {
      const m = await measure(page, url)
      all.push(m)
    }

    testInfo.attach('student-perf-metrics.json', {
      body: JSON.stringify(all, null, 2),
      contentType: 'application/json',
    })

    const breaches: { url: string; metric: string; value: number; budget: number }[] = []
    for (const m of all) {
      if (m.fcp > 0 && m.fcp > BUDGETS.fcp)
        breaches.push({ url: m.url, metric: 'FCP', value: m.fcp, budget: BUDGETS.fcp })
      if (m.lcp > 0 && m.lcp > BUDGETS.lcp)
        breaches.push({ url: m.url, metric: 'LCP', value: m.lcp, budget: BUDGETS.lcp })
      if (m.load > 0 && m.load > BUDGETS.load)
        breaches.push({ url: m.url, metric: 'LOAD', value: m.load, budget: BUDGETS.load })
    }

    console.log(`\n=== STUDENT performance summary (dev-mode budgets) ===`)
    for (const m of all) {
      const path = new URL(m.url).pathname
      console.log(`${path.padEnd(20)} FCP=${Math.round(m.fcp)}ms  LCP=${Math.round(m.lcp)}ms  DCL=${Math.round(m.dcl)}ms  LOAD=${Math.round(m.load)}ms  TBT=${Math.round(m.tbt)}ms`)
    }

    expect(breaches,
      `${breaches.length} student page(s) breach perf budgets:\n` +
      JSON.stringify(breaches, null, 2),
    ).toEqual([])
  })

  test('admin canonical pages stay within performance budgets @epic-x @perf @admin', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
    await page.locator('input[type="email"]').fill('admin@cena.local')
    await page.locator('input[type="password"]').fill('DevAdmin123!')
    await page.locator('button[type="submit"]').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    // COV-01: full route matrix. Skips routes in EPIC-G admin-pages-smoke
    // KNOWN_BROKEN_ROUTES allowlist — those have backend gaps that
    // distort load timings (some return 500 fast, some hang on SignalR
    // negotiate). Once BG-01..05 land they can be added back.
    const ADMIN_PATHS = [
      '/dashboards/admin',
      '/apps/cultural/dashboard',
      '/apps/diagnostics/stuck-types',
      '/apps/experiments',
      '/apps/focus/dashboard',
      '/apps/ingestion/settings',
      '/apps/mastery/dashboard',
      '/apps/messaging',
      '/apps/moderation/queue',
      '/apps/outreach/dashboard',
      '/apps/pedagogy/mcm-graph',
      '/apps/pedagogy/methodology',
      '/apps/pedagogy/methodology-hierarchy',
      '/apps/permissions',
      '/apps/roles',
      '/apps/system/audit-log',
      '/apps/system/dead-letters',
      '/apps/system/embeddings',
      '/apps/system/explanation-cache',
      '/apps/system/health',
      '/apps/system/settings',
      '/apps/system/token-budget',
      '/apps/tutoring/sessions',
      '/apps/user/list',
    ]
    const PAGES = ADMIN_PATHS.map(p => `${ADMIN_SPA_BASE_URL}${p}`)

    const all: Metrics[] = []
    for (const url of PAGES) {
      all.push(await measure(page, url))
    }

    testInfo.attach('admin-perf-metrics.json', {
      body: JSON.stringify(all, null, 2),
      contentType: 'application/json',
    })

    const breaches: { url: string; metric: string; value: number; budget: number }[] = []
    for (const m of all) {
      if (m.fcp > 0 && m.fcp > BUDGETS.fcp)
        breaches.push({ url: m.url, metric: 'FCP', value: m.fcp, budget: BUDGETS.fcp })
      if (m.lcp > 0 && m.lcp > BUDGETS.lcp)
        breaches.push({ url: m.url, metric: 'LCP', value: m.lcp, budget: BUDGETS.lcp })
      if (m.load > 0 && m.load > BUDGETS.load)
        breaches.push({ url: m.url, metric: 'LOAD', value: m.load, budget: BUDGETS.load })
    }

    console.log(`\n=== ADMIN performance summary (dev-mode budgets) ===`)
    for (const m of all) {
      const path = new URL(m.url).pathname
      console.log(`${path.padEnd(28)} FCP=${Math.round(m.fcp)}ms  LCP=${Math.round(m.lcp)}ms  DCL=${Math.round(m.dcl)}ms  LOAD=${Math.round(m.load)}ms  TBT=${Math.round(m.tbt)}ms`)
    }

    expect(breaches,
      `${breaches.length} admin page(s) breach perf budgets:\n` +
      JSON.stringify(breaches, null, 2),
    ).toEqual([])
  })
})
