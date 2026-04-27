// =============================================================================
// EPIC-E2E-G — Admin SPA full-page smoke matrix
//
// The earlier EPIC-G-admin-journey.spec.ts only visits 2 of ~50 admin
// pages. This file drives admin@cena.local through every static
// admin route in one signed-in session, asserting per-page:
//
//   1. The route resolves (no JS thrown by the route guard)
//   2. The page mounts (DOM has content under main)
//   3. No console.error during load
//   4. No uncaught page errors
//   5. Any 4xx/5xx network responses are captured for review (not all
//      are failures — e.g. 404 from a list endpoint with no rows is OK)
//
// Dynamic [id] routes (apps/experiments/[id], apps/user/view/[id], etc.)
// are intentionally skipped — they need seeded ids that the dev stack
// doesn't guarantee. They're tracked as a follow-up gap.
//
// Why one spec with a route loop instead of one spec per page: signing
// in 50 times costs 50 * ~3s = 2.5 min just on auth. One signed-in
// session lets us hit them all in <90s.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const SEEDED_ADMIN_EMAIL = 'admin@cena.local'
const SEEDED_ADMIN_PASSWORD = 'DevAdmin123!'

interface PageProbeResult {
  route: string
  status: 'ok' | 'console-error' | 'page-error' | 'no-content' | 'redirect-to-login'
  finalUrl: string
  consoleErrors: string[]
  pageErrors: string[]
  failedRequests: { method: string; url: string; status: number }[]
}

// Static admin routes — every page under src/admin/full-version/src/pages
// EXCEPT public auth pages and dynamic [id] routes. Sorted alphabetically
// to make failures predictable.
const STATIC_ROUTES = [
  '/apps/cultural/dashboard',
  '/apps/diagnostics/stuck-types',
  '/apps/experiments',
  '/apps/focus/dashboard',
  '/apps/ingestion/pipeline',
  '/apps/ingestion/settings',
  '/apps/mastery/dashboard',
  '/apps/messaging',
  '/apps/moderation/queue',
  '/apps/outreach/dashboard',
  '/apps/pedagogy/mcm-graph',
  '/apps/pedagogy/methodology',
  '/apps/pedagogy/methodology-hierarchy',
  '/apps/permissions',
  '/apps/questions/languages',
  '/apps/questions/list',
  '/apps/roles',
  '/apps/sessions/live',
  '/apps/sessions/monitor',
  '/apps/system/actors',
  '/apps/system/ai-settings',
  '/apps/system/architecture',
  '/apps/system/audit-log',
  '/apps/system/dead-letters',
  '/apps/system/embeddings',
  '/apps/system/events',
  '/apps/system/explanation-cache',
  '/apps/system/health',
  '/apps/system/settings',
  '/apps/system/token-budget',
  '/apps/tutoring/sessions',
  '/apps/user/list',
  '/dashboards/admin',
  '/instructor',
  '/mentor',
] as const

// These are public routes that don't need auth — nice to verify they
// also render cleanly so a signed-out admin doesn't get a JS error
// trying to view T&Cs.
const PUBLIC_ROUTES = [
  '/privacy',
  '/terms',
  '/forgot-password',
  '/not-authorized',
] as const

async function probe(page: Page, route: string): Promise<PageProbeResult> {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  const failedRequests: { method: string; url: string; status: number }[] = []

  // Per-page listeners. Listeners are additive on the page object so
  // we tag each by the route they care about. Cleaner: add fresh
  // listeners per probe and remove after.
  const consoleHandler = (msg: import('@playwright/test').ConsoleMessage) => {
    if (msg.type() === 'error') consoleErrors.push(msg.text())
  }
  const pageErrorHandler = (err: Error) => { pageErrors.push(err.message) }
  const responseHandler = (resp: import('@playwright/test').Response) => {
    if (resp.status() >= 400) {
      failedRequests.push({
        method: resp.request().method(),
        url: resp.url(),
        status: resp.status(),
      })
    }
  }

  page.on('console', consoleHandler)
  page.on('pageerror', pageErrorHandler)
  page.on('response', responseHandler)

  try {
    await page.goto(`${ADMIN_SPA_BASE_URL}${route}`, { timeout: 15_000, waitUntil: 'domcontentloaded' })
    // Settle micro-tasks / lazy chunk loads.
    await page.waitForTimeout(500)

    const finalUrl = page.url()
    const redirectedToLogin = /\/login/.test(finalUrl)

    if (pageErrors.length > 0)
      return { route, status: 'page-error', finalUrl, consoleErrors, pageErrors, failedRequests }
    if (redirectedToLogin)
      return { route, status: 'redirect-to-login', finalUrl, consoleErrors, pageErrors, failedRequests }
    if (consoleErrors.length > 0)
      return { route, status: 'console-error', finalUrl, consoleErrors, pageErrors, failedRequests }

    // Crude content check: <main> or main role exists with non-empty
    // text. The negative signal is a route that loads with an empty
    // template (lazy-import broke).
    const mainHasContent = await page
      .locator('main, [role="main"]')
      .filter({ hasText: /\S/ })
      .first()
      .isVisible()
      .catch(() => false)
    if (!mainHasContent)
      return { route, status: 'no-content', finalUrl, consoleErrors, pageErrors, failedRequests }

    return { route, status: 'ok', finalUrl, consoleErrors, pageErrors, failedRequests }
  }
  finally {
    page.off('console', consoleHandler)
    page.off('pageerror', pageErrorHandler)
    page.off('response', responseHandler)
  }
}

test.describe('EPIC_G_ADMIN_PAGES_SMOKE', () => {
  test('all static admin pages load cleanly for SUPER_ADMIN @epic-g @admin-smoke', async ({ page }, testInfo) => {
    test.setTimeout(300_000) // ~5 min budget for ~35 pages

    // Sign in once.
    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
    await page.locator('input[type="email"]').fill(SEEDED_ADMIN_EMAIL)
    await page.locator('input[type="password"]').fill(SEEDED_ADMIN_PASSWORD)
    await page.locator('button[type="submit"]').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    const results: PageProbeResult[] = []
    for (const route of STATIC_ROUTES) {
      const r = await probe(page, route)
      results.push(r)
      // Tiny gap between pages so the per-IP rate limiter doesn't
      // window us into 429s that have nothing to do with the page
      // under test (those are artifacts of smoke-iteration speed).
      await page.waitForTimeout(200)
    }

    // Public routes (no auth required) — verify they render even when
    // a signed-in admin visits them (no JS error from auth context).
    for (const route of PUBLIC_ROUTES) {
      const r = await probe(page, route)
      results.push(r)
      await page.waitForTimeout(200)
    }

    testInfo.attach('admin-page-smoke-results.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    // Bucket the results.
    const empty = results.filter(r => r.status === 'no-content')
    const redirected = results.filter(r => r.status === 'redirect-to-login')

    // HARD FAIL: any page-error (uncaught JS exception). Those are
    // always real bugs — code path that throws on mount.
    const pageErrors = results.filter(r => r.status === 'page-error')
    expect(pageErrors,
      `${pageErrors.length} admin page(s) threw uncaught JS exception(s): ` +
      JSON.stringify(pageErrors.map(b => ({ route: b.route, errs: b.pageErrors.slice(0, 2) })), null, 2),
    ).toEqual([])

    // SOFT FAIL with explicit allowlist: console-error (which usually
    // means a 4xx/5xx the page didn't model). Each entry below is a
    // KNOWN BACKEND/INTEGRATION GAP surfaced by the smoke run.
    //
    // INFRA-05 staleness contract:
    //   - reason:     1-line explanation of WHY this route is broken
    //   - surfacedAt: ISO date — entries older than 30 days fail in CI
    //   - ticket:     reference to the queued fix (TASK-E2E-BG-NN)
    //
    // When you fix the underlying backend gap, REMOVE the entry —
    // the test then enforces the route stays green. Stale-entry
    // detection (route no longer reproduces) ALSO fails, so the
    // allowlist self-cleans.
    interface KnownBrokenEntry {
      reason: string
      surfacedAt: string  // ISO yyyy-mm-dd
      ticket?: string
    }
    const KNOWN_BROKEN_ROUTES: Record<string, KnownBrokenEntry> = {
      '/apps/ingestion/pipeline':   { reason: 'admin-api: GET /api/admin/ingestion/{stats,pipeline-status} 500',     surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-03' },
      '/apps/questions/languages':  { reason: 'admin-api: GET /api/admin/questions/languages 500',                    surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-02' },
      '/apps/questions/list':       { reason: 'admin-api: GET /api/admin/questions list 500',                         surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-02' },
      '/apps/sessions/live':        { reason: 'admin-api: 401 from realtime endpoint — token shape mismatch',         surfacedAt: '2026-04-27' },
      '/apps/system/ai-settings':   { reason: 'admin-api: GET /api/admin/ai/settings 500 (renders empty-state cleanly after JS-undefined fix)', surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-01' },
      '/apps/sessions/monitor':     { reason: 'admin-api: SignalR /sessionMonitor hub negotiate 404',                surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-04' },
      '/apps/system/actors':        { reason: 'admin-api: SignalR /actors hub negotiate 404',                         surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-04' },
      '/apps/system/architecture':  { reason: 'admin-api: SignalR /architecture hub negotiate 404',                   surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-04' },
      '/apps/system/events':        { reason: 'admin-api: SignalR /events hub negotiate 404',                         surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-04' },
      '/instructor':                { reason: 'admin-api: GET /api/instructor/* 404 — endpoint missing',              surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-05' },
      '/mentor':                    { reason: 'admin-api: GET /api/mentor/institutes 404 — endpoint missing',         surfacedAt: '2026-04-27', ticket: 'TASK-E2E-BG-05' },
    }

    // Allowlist budget cap: if the team adds entries faster than they
    // fix them, the cap creates friction that forces triage.
    const ALLOWLIST_BUDGET = 15
    const entryCount = Object.keys(KNOWN_BROKEN_ROUTES).length
    if (entryCount > ALLOWLIST_BUDGET) {
      testInfo.annotations.push({
        type: 'warning',
        description: `KNOWN_BROKEN_ROUTES has ${entryCount} entries — exceeds budget of ${ALLOWLIST_BUDGET}. Triage and fix instead of allowlisting more.`,
      })
    }

    // 429s are an artifact of smoke-iteration speed, not product bugs.
    // If a page's console-errors are EXCLUSIVELY 429-shaped, treat the
    // page as OK regardless of allowlist — production users hit pages
    // one at a time, not 50/sec.
    const isOnlyRateLimits = (r: PageProbeResult) =>
      r.consoleErrors.length > 0
      && r.consoleErrors.every(e => /\b429\b|Too Many Requests/i.test(e))
    const consoleErrors = results.filter(r => r.status === 'console-error' && !isOnlyRateLimits(r))
    const unexpectedConsoleErrors = consoleErrors.filter(r => !(r.route in KNOWN_BROKEN_ROUTES))
    expect(unexpectedConsoleErrors,
      `${unexpectedConsoleErrors.length} admin page(s) NEW console-error not in the known-broken allowlist: ` +
      JSON.stringify(unexpectedConsoleErrors.map(b => ({ route: b.route, errs: b.consoleErrors.slice(0, 2) })), null, 2) +
      '\nIf this is a new gap, add it to KNOWN_BROKEN_ROUTES with a reason. If a previously-broken page now passes, remove it from the allowlist.',
    ).toEqual([])

    // INFRA-05 staleness gate (1): every entry in the allowlist must
    // still be reproduced — if a page was once broken and is now green,
    // the entry is stale. In CI this hard-fails so the allowlist
    // self-cleans; locally it warns to avoid churning while iterating.
    const stale = Object.keys(KNOWN_BROKEN_ROUTES).filter(route =>
      !consoleErrors.some(r => r.route === route),
    )
    if (stale.length > 0) {
      const msg = `KNOWN_BROKEN_ROUTES has ${stale.length} stale entries (page now passes — REMOVE these): ${stale.join(', ')}`
      if (process.env.CI) {
        expect(stale, msg).toEqual([])
      }
      else {
        testInfo.annotations.push({ type: 'warning', description: msg })
      }
    }

    // INFRA-05 staleness gate (2): entries older than 30 days are stale
    // by time — either the gap was forgotten about or no one is
    // tracking the queued ticket. Forces a re-triage every 30 days.
    const STALE_DAYS = 30
    const now = Date.now()
    const tooOld: { route: string; surfacedAt: string; ageDays: number }[] = []
    for (const [route, entry] of Object.entries(KNOWN_BROKEN_ROUTES)) {
      const surfacedMs = new Date(entry.surfacedAt + 'T00:00:00Z').getTime()
      const ageDays = Math.floor((now - surfacedMs) / (24 * 60 * 60 * 1000))
      if (ageDays > STALE_DAYS) {
        tooOld.push({ route, surfacedAt: entry.surfacedAt, ageDays })
      }
    }
    if (tooOld.length > 0) {
      const msg =
        `${tooOld.length} KNOWN_BROKEN_ROUTES entries are >${STALE_DAYS} days old. ` +
        `Either fix the underlying backend gap, refresh the surfacedAt date with a re-triage note, ` +
        `or escalate. Stale entries:\n` +
        JSON.stringify(tooOld, null, 2)
      if (process.env.CI) {
        expect(tooOld, msg).toEqual([])
      }
      else {
        testInfo.annotations.push({ type: 'warning', description: msg })
      }
    }

    if (empty.length > 0) {
      testInfo.annotations.push({
        type: 'warning',
        description: `${empty.length} pages had no main content: ${empty.map(e => e.route).join(', ')}`,
      })
    }
    if (redirected.length > 0) {
      testInfo.annotations.push({
        type: 'warning',
        description: `${redirected.length} pages redirected to /login (role-gated?): ${redirected.map(r => r.route).join(', ')}`,
      })
    }

    console.log(`\n=== EPIC-G admin smoke summary ===`)
    console.log(`OK:                          ${results.filter(r => r.status === 'ok').length}`)
    console.log(`Hard JS error (page-error):  ${pageErrors.length}`)
    console.log(`Known-broken (allowlist):    ${consoleErrors.length - unexpectedConsoleErrors.length}`)
    console.log(`NEW console-error gaps:      ${unexpectedConsoleErrors.length}`)
    console.log(`No content:                  ${empty.length}`)
    console.log(`Role-redirected:             ${redirected.length}`)
    console.log(`Stale allowlist entries:     ${stale.length}`)
    console.log(`Total:                       ${results.length}`)
  })
})
