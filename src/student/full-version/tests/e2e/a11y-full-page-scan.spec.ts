// =============================================================================
// RDY-030 + COV-02: Systematic axe-core WCAG 2.1 AA scan.
//
// Original RDY-030 scope: 5 top-level student pages, en locale.
// COV-02 extension (2026-04-27, claude-1):
//   - 4 student-route locale variants (en + ar + he) for the 5 base pages
//   - 24 admin SPA routes scanned for the admin auth-shell
// Single source of truth — extension lives in this file (don't fork into
// e2e-flow/) so the WCAG enforcement rule is in one place.
//
// Runs WCAG 2.1 AA rules via @axe-core/playwright. Fails on any
// serious/critical violation. Minor/moderate logged as warnings.
//
// Israeli law context: Equal Rights for Persons with Disabilities Law
// 5758-1998 + Accessibility Regulations 5773-2013 require WCAG 2.1 AA.
// =============================================================================

import AxeBuilder from '@axe-core/playwright'
import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'

/** Student pages scanned in en locale (original RDY-030 set). */
const STUDENT_PAGES = [
  { route: '/home',       name: 'home' },
  { route: '/session',    name: 'session' },
  { route: '/progress',   name: 'progress' },
  { route: '/profile',    name: 'profile' },
  { route: '/onboarding', name: 'onboarding' },
] as const

/** Locales the SPA supports. ar + he are RTL. */
const LOCALES = ['en', 'ar', 'he'] as const
type Locale = typeof LOCALES[number]

/** Admin routes scanned — same matrix as EPIC-G admin smoke (signed-in
 * pages without known-broken backend gaps). Skipping the 11 KNOWN_BROKEN
 * routes because their console-error noise correlates with partial DOM
 * which would distort axe's `nodes` reporting. */
const ADMIN_PAGES = [
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
] as const

/** Mock auth so authenticated student pages render real content. */
async function seedStudentAuth(page: Page, locale: Locale = 'en') {
  await page.addInitScript((opts: { locale: string }) => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-a11y',
      email: 'a11y@example.com',
      displayName: 'A11y Tester',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-a11y',
      displayName: 'A11y Tester',
      email: 'a11y@example.com',
      locale: opts.locale,
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
    localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: opts.locale, locked: true, version: 1 }),
    )
  }, { locale })
}

/**
 * Run axe with WCAG 2.1 AA rule set, excluding Vuetify portals and dev chrome
 * that are not part of the shipped UI surface.
 */
async function runAxe(page: Page) {
  return new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .exclude('.v-overlay-container')
    .exclude('.vue-devtools__anchor-btn')
    .exclude('[data-v-devtools]')
    .analyze()
}

/** Strict mode: fail only on serious/critical — minor/moderate are
 * warnings (baseline may still contain them). Used for the original
 * RDY-030 student × en surface where the baseline is already clean.
 * Zero-tolerance policy applies to new violations only. */
function assertNoBlockingViolations(
  label: string,
  violations: { id: string; impact?: string | null; help: string; nodes: { target: string[] }[] }[],
) {
  const blocking = violations.filter(
    v => v.impact === 'serious' || v.impact === 'critical',
  )

  if (blocking.length > 0) {
    const summary = blocking.map(v => ({
      rule: v.id,
      impact: v.impact,
      help: v.help,
      nodes: v.nodes.map(n => n.target).slice(0, 3),
    }))
    console.error(`[${label}] axe violations:`, JSON.stringify(summary, null, 2))
  }

  expect(
    blocking.length,
    `${label} should have 0 serious/critical WCAG 2.1 AA violations`,
  ).toBe(0)
}

/** Lenient mode: count + log violations but pass. Used for the new
 * COV-02 surface (student ar/he, admin SPA) where the baseline is
 * unknown — we don't want CI to block while the design team triages.
 * The numeric count is attached as a test annotation so a baseline
 * cleanup task can read it and track decay. Promote individual blocks
 * to strict mode once their baseline goes clean. */
function recordBaselineViolations(
  testInfo: import('@playwright/test').TestInfo,
  label: string,
  violations: { id: string; impact?: string | null; help: string; nodes: { target: string[] }[] }[],
) {
  const blocking = violations.filter(
    v => v.impact === 'serious' || v.impact === 'critical',
  )

  if (blocking.length > 0) {
    const summary = blocking.map(v => ({
      rule: v.id,
      impact: v.impact,
      help: v.help,
      nodes: v.nodes.map(n => n.target).slice(0, 3),
    }))
    console.warn(`[${label}] BASELINE axe violations (${blocking.length} serious/critical):`,
      JSON.stringify(summary, null, 2))

    testInfo.annotations.push({
      type: 'a11y-baseline',
      description: `${label}: ${blocking.length} serious/critical — promote to strict once cleaned. Rules: ${[...new Set(blocking.map(v => v.id))].join(', ')}`,
    })

    testInfo.attach(`${label.replace(/[^a-z0-9]/gi, '-')}-axe-baseline.json`, {
      body: JSON.stringify({ label, totalBlocking: blocking.length, summary }, null, 2),
      contentType: 'application/json',
    })
  }
}

// ── Block 1: Student pages × en locale (original RDY-030 — keep as-is) ──
test.describe('RDY-030: student full-page axe scan (en)', () => {
  test.beforeEach(async ({ page }) => {
    await seedStudentAuth(page, 'en')
  })

  for (const { route, name } of STUDENT_PAGES) {
    test(`${name} has no serious/critical WCAG 2.1 AA violations`, async ({ page }) => {
      const response = await page.goto(route)
      expect(response?.ok(), `page ${route} should load`).toBe(true)
      await page.waitForLoadState('networkidle')

      const results = await runAxe(page)
      assertNoBlockingViolations(name, results.violations)
    })
  }
})

// ── Block 2: Student pages × ar/he locales (RTL) ──
// Israeli law requires WCAG 2.1 AA; the student SPA explicitly supports
// ar + he as RTL locales. RTL-specific a11y bugs (mis-mirrored aria,
// dir=ltr leaks on icons) need their own scan pass.
test.describe('COV-02: student full-page axe scan (ar + he)', () => {
  for (const locale of ['ar', 'he'] as const) {
    test.describe(`locale=${locale}`, () => {
      test.beforeEach(async ({ page }) => {
        await seedStudentAuth(page, locale)
      })

      for (const { route, name } of STUDENT_PAGES) {
        test(`${name} ${locale} a11y baseline (lenient — promote to strict once clean)`, async ({ page }, testInfo) => {
          const response = await page.goto(route)
          expect(response?.ok(), `page ${route} should load`).toBe(true)
          await page.waitForLoadState('networkidle')

          // Confirm the SPA actually flipped direction so axe scans the
          // RTL render. If `dir` stayed `ltr` after seeding, the locale
          // wiring regressed — that IS a hard fail (route guard / i18n
          // contract).
          const dir = await page.evaluate(() => document.documentElement.dir || 'ltr')
          expect(dir, `locale=${locale} expected dir=rtl, got ${dir}`).toBe('rtl')

          const results = await runAxe(page)
          recordBaselineViolations(testInfo, `${name}-${locale}`, results.violations)
        })
      }
    })
  }
})

// ── Block 3: Admin SPA routes (en, signed-in admin) ──
// Admin SPA was uncovered by axe before COV-02. Same WCAG 2.1 AA bar.
// The admin SPA mounts on a different port (:5174) so we drive its
// /login form to seed real Firebase IndexedDB; mock-auth doesn't apply
// because admin pages depend on an actual JWT with the SUPER_ADMIN role.
test.describe('COV-02: admin full-page axe scan', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await page.locator('input[type="email"]').fill('admin@cena.local')
    await page.locator('input[type="password"]').fill('DevAdmin123!')
    await page.locator('button[type="submit"]').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
  })

  for (const route of ADMIN_PAGES) {
    test(`admin ${route} a11y baseline (lenient — promote to strict once clean)`, async ({ page }, testInfo) => {
      const response = await page.goto(`${ADMIN_SPA_BASE_URL}${route}`)
      expect(response?.ok(), `admin ${route} should load`).toBe(true)
      await page.waitForLoadState('networkidle')

      const results = await runAxe(page)
      recordBaselineViolations(testInfo, `admin${route}`, results.violations)
    })
  }
})
