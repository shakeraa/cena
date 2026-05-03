// =============================================================================
// EPIC-E2E-X — TASK-E2E-COV-03 visual regression sweep
//
// Captures Playwright snapshot baselines for the canonical SPA routes
// across role × locale × viewport. First run captures goldens; later
// runs compare and fail if a route's render diverges by more than
// `maxDiffPixelRatio`.
//
// Complementary to (NOT replacing):
//   - tests/e2e/rtl-visual-regression.spec.ts (RDY-002 — emits PNG
//     artifacts for human review of Arabic RTL pages; capture-only,
//     no diff check). Stays as-is.
//
// This spec adds AUTOMATED diff checking + admin SPA coverage + LTR
// baselines. Tradeoff: maintenance tax — every legitimate UI change
// requires `--update-snapshots`. Mitigated by a 2% pixel-diff
// tolerance + masking the volatile time/streak surfaces.
//
// Run:
//   cd src/student/full-version
//   CENA_TEST_PROBE_TOKEN=... npx playwright test \
//     --config playwright.e2e-flow.config.ts \
//     --grep "EPIC_X_VISUAL_REGRESSION"
//
//   # First run / accepted UI change:
//     ... --update-snapshots
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

// Generous tolerance — anti-aliasing + font hinting jitter is normal
// across runs; we only want to catch real layout regressions.
const MAX_DIFF_PIXEL_RATIO = 0.03

const VIEWPORTS = [
  { name: 'mobile',  width: 375,  height: 812  },
  { name: 'desktop', width: 1440, height: 900  },
] as const

/** Mask volatile DOM regions that change every render but aren't
 * regressions: timestamps, real-time greeting copy, animated dots. */
async function getVolatileMasks(page: Page) {
  return [
    page.locator('[data-testid="dynamic-timestamp"]'),
    page.locator('[data-testid="home-streak"]'),
    page.locator('[data-testid="header-now"]'),
    // Vue devtools surfaces in dev mode — never on prod
    page.locator('[data-v-devtools]'),
    page.locator('.vue-devtools__anchor-btn'),
  ]
}

async function provisionAndLogin(page: Page, locale: 'en' | 'ar' | 'he' = 'en'): Promise<void> {
  await page.addInitScript((opts: { tenantId: string; locale: string }) => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: opts.locale, locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', opts.tenantId)
  }, { tenantId: TENANT_ID, locale })

  const email = `e2e-vr-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'VR Test' },
  })
  await page.request.post('/api/me/onboarding', {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      Role: 'student', Locale: locale, Subjects: ['math'],
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

async function adminLogin(page: Page): Promise<void> {
  await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
  await page.locator('input[type="email"]').fill('admin@cena.local')
  await page.locator('input[type="password"]').fill('DevAdmin123!')
  await page.locator('button[type="submit"]').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

/** Settle layout: wait for fonts + reduce-motion + LCP candidates. */
async function settleForSnapshot(page: Page) {
  await page.waitForLoadState('networkidle').catch(() => {})
  // Force prefers-reduced-motion so flow ambient + transitions are static
  await page.emulateMedia({ reducedMotion: 'reduce' })
  // Give Vue a tick to settle re-renders
  await page.waitForTimeout(800)
}

// ─── Block 1: Student × en, key signed-in routes × 2 viewports ─────────────
test.describe('EPIC_X_VISUAL_REGRESSION (student en)', () => {
  for (const route of ['/home', '/pricing', '/profile', '/settings', '/tutor']) {
    for (const vp of VIEWPORTS) {
      test(`student${route} en ${vp.name} matches baseline @epic-x @visual @student`, async ({ page }) => {
        test.setTimeout(60_000)
        await page.setViewportSize({ width: vp.width, height: vp.height })
        await provisionAndLogin(page, 'en')
        await page.goto(`${STUDENT_SPA_BASE_URL}${route}`)
        await settleForSnapshot(page)

        const masks = await getVolatileMasks(page)
        await expect(page).toHaveScreenshot(
          `student${route.replace(/\//g, '-')}-en-${vp.name}.png`,
          { maxDiffPixelRatio: MAX_DIFF_PIXEL_RATIO, animations: 'disabled', mask: masks },
        )
      })
    }
  }
})

// ─── Block 2: Student × ar/he, key signed-in routes × mobile viewport ──────
// Limited to mobile + 3 routes to keep wall time reasonable. RTL bugs
// almost always reproduce at mobile width (where Arabic glyph mass is
// hardest to fit).
test.describe('EPIC_X_VISUAL_REGRESSION (student rtl)', () => {
  for (const locale of ['ar', 'he'] as const) {
    for (const route of ['/home', '/pricing', '/session']) {
      test(`student${route} ${locale} mobile matches baseline @epic-x @visual @student @rtl`, async ({ page }) => {
        test.setTimeout(60_000)
        await page.setViewportSize({ width: 375, height: 812 })
        await provisionAndLogin(page, locale)
        await page.goto(`${STUDENT_SPA_BASE_URL}${route}`)
        await settleForSnapshot(page)

        // Sanity: confirm the SPA actually flipped direction so we're
        // snapshotting the RTL render.
        const dir = await page.evaluate(() => document.documentElement.dir || 'ltr')
        expect(dir, `locale=${locale} expected dir=rtl, got ${dir}`).toBe('rtl')

        const masks = await getVolatileMasks(page)
        await expect(page).toHaveScreenshot(
          `student${route.replace(/\//g, '-')}-${locale}-mobile.png`,
          { maxDiffPixelRatio: MAX_DIFF_PIXEL_RATIO, animations: 'disabled', mask: masks },
        )
      })
    }
  }
})

// ─── Block 3: Admin SPA, key surfaces × 2 viewports ────────────────────────
test.describe('EPIC_X_VISUAL_REGRESSION (admin)', () => {
  for (const route of ['/dashboards/admin', '/apps/permissions', '/apps/moderation/queue', '/apps/system/health']) {
    for (const vp of VIEWPORTS) {
      test(`admin${route} ${vp.name} matches baseline @epic-x @visual @admin`, async ({ page }) => {
        test.setTimeout(60_000)
        await page.setViewportSize({ width: vp.width, height: vp.height })
        await adminLogin(page)
        await page.goto(`${ADMIN_SPA_BASE_URL}${route}`)
        await settleForSnapshot(page)

        const masks = await getVolatileMasks(page)
        await expect(page).toHaveScreenshot(
          `admin${route.replace(/\//g, '-')}-${vp.name}.png`,
          { maxDiffPixelRatio: MAX_DIFF_PIXEL_RATIO, animations: 'disabled', mask: masks },
        )
      })
    }
  }
})
