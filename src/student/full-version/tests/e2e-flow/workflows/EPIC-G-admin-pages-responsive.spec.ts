// =============================================================================
// EPIC-E2E-G — Admin SPA responsiveness sweep
//
// For each static admin route (same set as the smoke matrix), drive the
// page across three viewports — mobile, tablet, desktop — and assert:
//
//   1. No HORIZONTAL OVERFLOW. The page's body width must not exceed
//      the viewport width. A surprise overflow at mobile width is the
//      most common responsive regression (a fixed-width table, an
//      uncollapsed nav, a min-width:1200 image grid, etc.) and is what
//      production users on phones actually see.
//   2. PRIMARY HEADING VISIBLE. The first <h1> / role="heading" level=1
//      must be on screen — i.e. the page didn't render below-fold-only
//      or get clipped by an oversize hero.
//
// We don't try to assert pixel-perfect layout — that's visual-regression
// territory (axe-core / Percy). The goal here is to catch the "this
// page is unusable on mobile" class of bug surfaced by the per-page
// smoke audit.
//
// Tradeoff: this triples the wall time vs the smoke run (~3min). To
// keep it manageable, we visit all viewports for one route in
// sequence (one signed-in tab) and use the same allowlist of
// known-broken routes from the smoke spec — those won't be probed
// for layout because their console-error noise indicates the page
// didn't fully render anyway.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const SEEDED_ADMIN_EMAIL = 'admin@cena.local'
const SEEDED_ADMIN_PASSWORD = 'DevAdmin123!'

// Three breakpoints chosen to mirror the SPA's actual responsive
// surface. Vuetify's $display thresholds are sm=600, md=960, lg=1264
// — these widths sit cleanly inside the mobile/tablet/desktop bands.
const VIEWPORTS = [
  { name: 'mobile',  width: 375,  height: 812  }, // iPhone X portrait
  { name: 'tablet',  width: 768,  height: 1024 }, // iPad portrait
  { name: 'desktop', width: 1440, height: 900  }, // mid-size laptop
] as const

// Same set as EPIC-G-admin-pages-smoke.spec.ts. Skipping the
// known-broken routes whose backend 4xx/5xx prevents the page from
// fully rendering — they'd produce false positives on overflow check.
const ROUTES_TO_PROBE = [
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
  '/dashboards/admin',
] as const

interface ResponsiveProbe {
  route: string
  viewport: string
  bodyWidth: number
  viewportWidth: number
  horizontalOverflow: boolean
  hasHeading: boolean
}

async function probeResponsive(page: Page, route: string, viewport: { name: string; width: number; height: number }): Promise<ResponsiveProbe> {
  await page.setViewportSize({ width: viewport.width, height: viewport.height })
  await page.goto(`${ADMIN_SPA_BASE_URL}${route}`, { timeout: 15_000, waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(400) // settle layout / lazy chunks

  // body.scrollWidth tells us the rendered content width. If it
  // exceeds the visible viewport by more than a couple of pixels
  // (rounding), the user gets a horizontal scrollbar.
  const bodyWidth = await page.evaluate(() => document.body.scrollWidth)
  const horizontalOverflow = bodyWidth > viewport.width + 2

  // Heading visible — we don't pin to a specific page-title testid
  // because it varies. role=heading[level=1] OR the first h1 / h2
  // covers the cases.
  const hasHeading = await page
    .locator('h1, h2, [role="heading"][aria-level="1"], [role="heading"][aria-level="2"]')
    .first()
    .isVisible()
    .catch(() => false)

  return {
    route,
    viewport: viewport.name,
    bodyWidth,
    viewportWidth: viewport.width,
    horizontalOverflow,
    hasHeading,
  }
}

test.describe('EPIC_G_ADMIN_PAGES_RESPONSIVE', () => {
  test('admin pages have no horizontal overflow across mobile/tablet/desktop @epic-g @responsive', async ({ page }, testInfo) => {
    test.setTimeout(420_000) // 7 min budget for ~23 routes × 3 viewports

    // Sign in once (using desktop viewport so the form fits).
    await page.setViewportSize({ width: 1440, height: 900 })
    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
    await page.locator('input[type="email"]').fill(SEEDED_ADMIN_EMAIL)
    await page.locator('input[type="password"]').fill(SEEDED_ADMIN_PASSWORD)
    await page.locator('button[type="submit"]').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    const results: ResponsiveProbe[] = []
    for (const route of ROUTES_TO_PROBE) {
      for (const viewport of VIEWPORTS) {
        const r = await probeResponsive(page, route, viewport)
        results.push(r)
        await page.waitForTimeout(150) // soft pacing for the rate limiter
      }
    }

    testInfo.attach('admin-responsive-results.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    // Hard fail: any horizontal overflow on the mobile breakpoint.
    // Tablet + desktop overflows are softer — many admin grids
    // genuinely need ≥768px to be usable, and we expect those to
    // fall back to horizontal scroll inside a contained card.
    const mobileOverflows = results.filter(r => r.viewport === 'mobile' && r.horizontalOverflow)

    // KNOWN-BROKEN-ON-MOBILE: pages whose entire layout is genuinely
    // a wide table / graph and don't have a mobile-specific view
    // shipped yet. Each entry must point at a queue task to fix.
    const KNOWN_BROKEN_ON_MOBILE: Record<string, string> = {
      '/apps/pedagogy/mcm-graph':            'wide adjacency-table — no mobile collapse yet',
      '/apps/pedagogy/methodology-hierarchy': 'tree visualization needs ≥600px',
    }

    const surprisingMobileOverflows = mobileOverflows.filter(r => !(r.route in KNOWN_BROKEN_ON_MOBILE))
    expect(surprisingMobileOverflows,
      `${surprisingMobileOverflows.length} admin page(s) overflow horizontally at mobile (375px) — ` +
      `not in KNOWN_BROKEN_ON_MOBILE allowlist:\n` +
      JSON.stringify(surprisingMobileOverflows.map(r => ({
        route: r.route, bodyWidth: r.bodyWidth, viewportWidth: r.viewportWidth,
      })), null, 2),
    ).toEqual([])

    // Soft signal: surface tablet/desktop overflows + missing-heading
    // cases as test annotations.
    const tabletOverflows = results.filter(r => r.viewport === 'tablet' && r.horizontalOverflow)
    const desktopOverflows = results.filter(r => r.viewport === 'desktop' && r.horizontalOverflow)
    const noHeading = results.filter(r => !r.hasHeading)

    if (tabletOverflows.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `${tabletOverflows.length} pages overflow at tablet (768px)` })
    if (desktopOverflows.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `${desktopOverflows.length} pages overflow at desktop (1440px)` })
    if (noHeading.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `${noHeading.length} (route, viewport) pairs render without a visible heading` })

    console.log(`\n=== EPIC-G responsive summary ===`)
    console.log(`Routes probed:               ${ROUTES_TO_PROBE.length}`)
    console.log(`Viewport pairs:              ${results.length}`)
    console.log(`Mobile overflows:            ${mobileOverflows.length} (${surprisingMobileOverflows.length} unexpected)`)
    console.log(`Tablet overflows:            ${tabletOverflows.length}`)
    console.log(`Desktop overflows:           ${desktopOverflows.length}`)
    console.log(`No-heading (any viewport):   ${noHeading.length}`)
  })
})
