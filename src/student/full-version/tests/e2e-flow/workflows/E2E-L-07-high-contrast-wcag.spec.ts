// =============================================================================
// EPIC-E2E-L-07 — High-contrast mode + WCAG AA color-contrast sweep
//
// Per the EPIC-L plan: "user picks a11y toolbar → high-contrast mode on
// → primary-color ratchet satisfies WCAG AA on every surface (session,
// pricing, parent dashboard). Contrast ratio assertion (axe + wcag2aa)."
//
// What this spec covers (e2e-flow scope):
//   1. Toggle high-contrast via A11yToolbar (data-testid="a11y-contrast-
//      toggle") and verify the toggle round-trips
//   2. With high-contrast ON, run AxeBuilder.withTags(['wcag2aa']) +
//      .include('color-contrast') across canonical signed-in surfaces:
//      /home /pricing /progress /session
//   3. Zero color-contrast violations after the high-contrast theme
//      applies. The Vuexy primary #7367F0 is locked per memory; the
//      high-contrast mode achieves AA via the lightness/darkness
//      adjustments in the theme, not by alternate hue.
//   4. tests/e2e/a11y-* are the mainline regression set; this spec is
//      the high-contrast-mode-specific overlay (the existing set runs
//      at default contrast)
//
// What's NOT covered (deferred):
//   * Color-blind simulation modes — separate axe ruleset (deferred)
//   * Modal/dialog overlay contrast — modals on /pricing surface during
//     redirect, transient; covered indirectly via the sweep
// =============================================================================

import { test, expect, type Page } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

const SWEEP_ROUTES = [
  '/home',
  '/pricing',
  '/progress',
  '/session',
] as const

interface DiagnosticCtx {
  consoleErrors: string[]
  pageErrors: string[]
}

function attachDiagnostics(page: Page): DiagnosticCtx {
  const ctx: DiagnosticCtx = { consoleErrors: [], pageErrors: [] }
  page.on('console', m => { if (m.type() === 'error') ctx.consoleErrors.push(m.text()) })
  page.on('pageerror', e => { ctx.pageErrors.push(e.message) })
  return ctx
}

async function provisionAndSignIn(page: Page): Promise<void> {
  await page.addInitScript((tenantId: string) => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: 'en', locked: true, version: 1 }),
    )
    window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
  }, TENANT_ID)

  const email = `e2e-l07-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'L-07 HiContrast' },
  })

  const re = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await re.json() as { idToken: string }

  await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${idToken}` },
    data: {
      role: 'student', locale: 'en', subjects: ['math'],
      dailyTimeGoalMinutes: 15, weeklySubjectTargets: [],
      diagnosticResults: null, classroomCode: null,
    },
  })

  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

async function enableHighContrast(page: Page): Promise<void> {
  // Tightened 2026-04-29 per honest gap audit A.9 — drives the REAL
  // user UX path (open A11yToolbar drawer → click contrast toggle)
  // instead of bypassing via a localStorage seed. The earlier seed
  // shortcut tested whether the theme picks up the preference, NOT
  // whether the toolbar UX itself works. Both classes of regression
  // matter; this version exercises the click path end-to-end.
  //
  // Flow:
  //   1. Click the always-visible a11y-toolbar-handle (mounted by
  //      App.vue's <A11yToolbar />, present on every signed-in route).
  //   2. Wait for the drawer to open (data-testid="a11y-toolbar-drawer").
  //   3. Toggle contrast (data-testid="a11y-contrast-toggle"). Read
  //      pre-state so a re-run doesn't flip OFF a previously-on session.
  //   4. Verify localStorage 'cena-a11y-prefs' reflects highContrast:true
  //      — that's the canonical persistence the toolbar writes.
  //
  // If the handle isn't reachable on a route (regression class — the
  // toolbar should be globally mounted), the test fails loudly so we
  // know about it.
  const handle = page.getByTestId('a11y-toolbar-handle')
  await handle.waitFor({ state: 'visible', timeout: 8_000 })
  await handle.click()

  const drawer = page.getByTestId('a11y-toolbar-drawer')
  await drawer.waitFor({ state: 'visible', timeout: 5_000 })

  const toggleRoot = page.getByTestId('a11y-contrast-toggle')
  await toggleRoot.waitFor({ state: 'visible', timeout: 5_000 })

  // Vuetify's VSwitch doesn't always propagate a click on the wrapper
  // to the inner <input>. Drive the checkbox directly so the
  // `update:model-value` event fires (which calls onContrastToggle →
  // a11y.toggleContrast() → store mutation → localStorage write).
  //
  // The drawer is scrollable; the contrast toggle may be below the
  // fold. Scroll it into view before clicking.
  const checkbox = toggleRoot.locator('input[type="checkbox"]').first()
  await checkbox.scrollIntoViewIfNeeded({ timeout: 5_000 }).catch(() => {})
  const before = await checkbox.isChecked().catch(() => false)
  if (!before) {
    // Use force:true because Vuetify visually hides the input under
    // the slider thumb (the input is the semantic source of truth,
    // but isn't directly hit-testable without force).
    await checkbox.click({ force: true })
    // VSwitch dispatches change synchronously; the store watch flushes
    // localStorage on next microtask. Wait one frame for both.
    await page.waitForTimeout(200)
  }

  // Verify the persistence layer reflects the toggle. This is what
  // the SPA reads on every subsequent route mount; if it's not set
  // here, navigating away will revert.
  // Canonical key + shape from src/stores/a11yStore.ts:
  //   STORAGE_KEY = 'cena-student-a11y-prefs'
  //   prefs.contrast: 'normal' | 'high'
  const persisted = await page.evaluate(() => {
    try {
      return JSON.parse(localStorage.getItem('cena-student-a11y-prefs') ?? '{}')
    }
    catch {
      return {}
    }
  })
  if (persisted?.contrast !== 'high') {
    throw new Error(
      'A11yToolbar contrast toggle click did NOT persist contrast="high" ' +
      `to cena-student-a11y-prefs. Got: ${JSON.stringify(persisted)}. ` +
      'Regression class: store write decoupled from toggle click, OR ' +
      'localStorage key drift.',
    )
  }
}

test.describe('E2E_L_07_HIGH_CONTRAST_WCAG', () => {
  test('high-contrast mode + WCAG AA color-contrast sweep across signed-in surfaces @epic-l @l-07 @a11y @wcag2aa @high-contrast', async ({ page }, testInfo) => {
    test.setTimeout(180_000)
    const diag = attachDiagnostics(page)

    await provisionAndSignIn(page)

    // Land on /home before flipping high-contrast — the toolbar is
    // available across the SPA shell once signed in.
    await page.goto('/home', { waitUntil: 'domcontentloaded' })
    await enableHighContrast(page)
    console.log('[l-07] high-contrast enabled via real toolbar click + persistence verified')

    const violationsByRoute: Record<string, Array<{ id: string; nodes: number; impact?: string }>> = {}

    for (const route of SWEEP_ROUTES) {
      await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' })
      await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {})

      // Run axe scoped to color-contrast under wcag2aa.
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2aa'])
        .options({ runOnly: { type: 'rule', values: ['color-contrast'] } })
        .analyze()

      violationsByRoute[route] = results.violations.map(v => ({
        id: v.id,
        nodes: v.nodes.length,
        impact: v.impact ?? undefined,
      }))
    }

    testInfo.attach('l-07-contrast-sweep.json', {
      body: JSON.stringify({
        usedToolbarToggle: true,
        violations: violationsByRoute,
        diag,
      }, null, 2),
      contentType: 'application/json',
    })

    const totalViolations = Object.values(violationsByRoute)
      .flat()
      .reduce((acc, v) => acc + v.nodes, 0)

    expect(totalViolations,
      `WCAG 2 AA color-contrast violations under high-contrast mode: ${totalViolations}. ` +
      `Per-route breakdown: ${JSON.stringify(violationsByRoute)}`,
    ).toBe(0)

    const appErrors = diag.pageErrors.filter(e =>
      !/localStorage.*access is denied|opaque origin/i.test(e),
    )
    expect(appErrors).toEqual([])
  })
})
