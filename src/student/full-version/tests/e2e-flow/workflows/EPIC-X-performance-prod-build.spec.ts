// =============================================================================
// EPIC-E2E-X — TASK-E2E-INFRA-04 production-build perf measurement
//
// EPIC-X-performance-budgets.spec.ts measures FCP/LCP/DCL/LOAD/TBT
// against the **dev** stack — Vite dev server, no minification, source
// maps inline. Two consequences make those numbers misleading:
//
//   1. LCP entries don't fire (dev pages are too small to trip
//      the heuristic) — `LCP=-1ms` on every route in the dev spec.
//   2. transferSize is wrong — dev includes HMR runtime + un-minified
//      code; prod is 5-10x smaller.
//
// This spec runs the SAME route matrix against a **production-build**
// preview (`vite build && vite preview`), where:
//   - LCP fires reliably (real heuristic-trippable pages)
//   - transferSize reflects user-visible-bytes
//   - tighter budgets are realistic
//
// PREREQUISITES (one-time, before running this spec):
//   cd src/student/full-version
//   npm run build                  # produces dist/
//   npx vite preview --port 5176   # serves dist/ on :5176
//
// ...and similarly for the admin SPA at :5177 if testing admin routes.
// CI bootstraps both via the e2e-flow workflow's `preview:e2e-perf`
// script step (TASK-E2E-INFRA-06).
//
// If the preview server isn't reachable, the test SKIPS rather than
// failing — keeps local dev iteration fast.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

// Distinct ports from the dev stack so dev (5175) and preview (5176)
// can run side-by-side. Override via env.
const STUDENT_PROD_BASE_URL = process.env.E2E_STUDENT_PROD_URL ?? 'http://localhost:5176'

interface Metrics {
  url: string
  fcp: number
  lcp: number
  dcl: number
  load: number
  tbt: number
  transferSize: number
}

// Production-grade budgets — these are what real users on a 4G mobile
// device should see. The dev-mode budgets in the sibling spec are 2-3x
// looser (FCP ≤ 4s vs ≤ 1.5s here, LCP ≤ 8s vs ≤ 2.5s here).
const PROD_BUDGETS = {
  fcp: 1500,        // First Contentful Paint
  lcp: 2500,        // Largest Contentful Paint
  load: 5000,       // load event
  transferSize: 800_000, // 800 KB per route bundle (gzipped)
} as const

async function isPreviewReachable(baseUrl: string): Promise<boolean> {
  try {
    const c = new AbortController()
    const t = setTimeout(() => c.abort(), 1500)
    const resp = await fetch(`${baseUrl}/`, { signal: c.signal })
    clearTimeout(t)
    return resp.ok || resp.status === 404 // 404 fine — server is up
  }
  catch {
    return false
  }
}

async function measure(page: Page, url: string): Promise<Metrics> {
  await page.goto(url, { timeout: 30_000, waitUntil: 'load' })
  // Allow LCP candidates to settle (LCP is observed via
  // PerformanceObserver and may fire after `load`).
  await page.waitForTimeout(2000)

  return await page.evaluate(() => {
    const paints = performance.getEntriesByType('paint') as PerformancePaintTiming[]
    const fcp = paints.find(p => p.name === 'first-contentful-paint')?.startTime ?? -1

    let lcp = -1
    try {
      const entries = performance.getEntriesByType('largest-contentful-paint') as PerformanceEntry[]
      lcp = entries.length > 0 ? entries[entries.length - 1].startTime : -1
    }
    catch { /* not all browsers expose LCP entries via getEntriesByType */ }

    const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined
    const dcl = nav?.domContentLoadedEventEnd ?? -1
    const load = nav?.loadEventEnd ?? -1

    const longTasks = performance.getEntriesByType('longtask') as PerformanceEntry[]
    const tbt = longTasks.reduce((acc, t) => acc + Math.max(0, t.duration - 50), 0)

    // Sum transferSize of all resources fetched during navigation.
    // Includes the initial HTML + JS chunks + CSS + fonts + images.
    const resources = performance.getEntriesByType('resource') as PerformanceResourceTiming[]
    const transferSize = resources.reduce((acc, r) => acc + (r.transferSize ?? 0), 0)
      + (nav?.transferSize ?? 0)

    return { fcp, lcp, dcl, load, tbt, transferSize }
  }).then(m => ({ url, ...m }))
}

test.describe('EPIC_X_PERFORMANCE_PROD_BUILD', () => {
  test('student prod-build canonical pages stay within tight perf budgets @epic-x @perf @prod-build', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    if (!(await isPreviewReachable(STUDENT_PROD_BASE_URL))) {
      testInfo.skip(true, `prod preview unreachable at ${STUDENT_PROD_BASE_URL}; ` +
        'run `cd src/student/full-version && npm run build && npx vite preview --port 5176`')
      return
    }

    // Anonymous canonical pages — public routes that don't require
    // sign-in. Signed-in routes need a Firebase emu side-channel which
    // doesn't exist against a static prod preview (no dev proxy to
    // student-api). For signed-in perf, the dev-stack spec covers it
    // (FCP/DCL/LOAD numbers are still useful in dev mode).
    const PAGES = [
      `${STUDENT_PROD_BASE_URL}/`,
      `${STUDENT_PROD_BASE_URL}/login`,
      `${STUDENT_PROD_BASE_URL}/register`,
      `${STUDENT_PROD_BASE_URL}/pricing`,
      `${STUDENT_PROD_BASE_URL}/forgot-password`,
      `${STUDENT_PROD_BASE_URL}/privacy`,
      `${STUDENT_PROD_BASE_URL}/terms`,
    ]

    const results: Metrics[] = []
    for (const url of PAGES) {
      results.push(await measure(page, url))
    }

    testInfo.attach('student-prod-perf-metrics.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    const breaches: { url: string; metric: string; value: number; budget: number }[] = []
    for (const m of results) {
      if (m.fcp > 0 && m.fcp > PROD_BUDGETS.fcp)
        breaches.push({ url: m.url, metric: 'FCP', value: m.fcp, budget: PROD_BUDGETS.fcp })
      if (m.lcp > 0 && m.lcp > PROD_BUDGETS.lcp)
        breaches.push({ url: m.url, metric: 'LCP', value: m.lcp, budget: PROD_BUDGETS.lcp })
      if (m.load > 0 && m.load > PROD_BUDGETS.load)
        breaches.push({ url: m.url, metric: 'LOAD', value: m.load, budget: PROD_BUDGETS.load })
      if (m.transferSize > PROD_BUDGETS.transferSize)
        breaches.push({ url: m.url, metric: 'transferSize', value: m.transferSize, budget: PROD_BUDGETS.transferSize })
    }

    console.log(`\n=== STUDENT prod-build perf summary (production budgets) ===`)
    for (const m of results) {
      const path = new URL(m.url).pathname
      const kb = Math.round(m.transferSize / 1024)
      console.log(`${path.padEnd(20)} FCP=${Math.round(m.fcp)}ms  LCP=${Math.round(m.lcp)}ms  DCL=${Math.round(m.dcl)}ms  LOAD=${Math.round(m.load)}ms  TBT=${Math.round(m.tbt)}ms  ${kb}KB`)
    }

    // Surface the LCP-fired count — if every page is still LCP=-1 in
    // prod, the spec didn't actually exercise the prod LCP heuristic.
    const lcpFired = results.filter(r => r.lcp > 0).length
    testInfo.annotations.push({
      type: 'note',
      description: `LCP fired on ${lcpFired}/${results.length} pages. If 0, the preview server may be rendering empty bodies.`,
    })

    expect(breaches,
      `${breaches.length} prod-build page(s) breach perf budgets:\n` +
      JSON.stringify(breaches, null, 2),
    ).toEqual([])
  })
})
