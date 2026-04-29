// =============================================================================
// EPIC-E2E-L-06 — Screen-reader-friendly session flow (aria-live)
//
// Per the EPIC-L plan: "run EPIC-E2E-C-02 with aria-live polite regions
// in place — correct/wrong feedback, hint surfacing, session-end summary
// all announced. [aria-live] regions present + correct politeness level,
// no aria-hidden on interactive elements."
//
// What this spec covers (e2e-flow scope):
//   1. Global #cena-live-region exists with aria-live="polite" on every
//      signed-in route (it's mounted in App.vue, so it should be in the
//      DOM at all times the SPA is mounted)
//   2. The live-region is NOT hidden via display:none / visibility:hidden
//      / aria-hidden=true (any of those breaks SR announcements)
//   3. /session-relevant pages render with at least one aria-live or
//      role=status region; nothing-interactive is wrapped in aria-hidden
//   4. Interactive controls (button, a[href], input, select, [tabindex])
//      MUST NOT live inside an aria-hidden=true ancestor — that combo
//      hides them from AT but keeps them keyboard-reachable, which is
//      a WCAG 4.1.2 violation
//
// What's NOT covered (deferred):
//   * Actually triggering correct/wrong feedback via answer submission
//     — needs the C-02 backend session round-trip we didn't seed
//   * Politeness-level audit (polite vs assertive) per surface — needs
//     subjective judgement; deferred to manual SR pass
//   * vitest-axe wcag2aa rule sweep — runs in tests/e2e/a11y-* (separate
//     test family); we focus on aria-live + aria-hidden invariants here
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

const SIGNED_IN_PROBE_ROUTES = [
  '/home',
  '/session',
  '/progress',
  '/notifications',
  '/settings',
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

  const email = `e2e-l06-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'L-06 SR' },
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

interface AriaAudit {
  liveRegionPresent: boolean
  liveRegionPoliteness: string | null
  liveRegionVisible: boolean
  hiddenInteractives: string[]
}

async function auditAria(page: Page): Promise<AriaAudit> {
  return await page.evaluate(() => {
    const live = document.getElementById('cena-live-region')
    let liveRegionVisible = false
    let liveRegionPoliteness: string | null = null
    if (live) {
      liveRegionPoliteness = live.getAttribute('aria-live')
      const cs = window.getComputedStyle(live)
      // sr-only is a "visually hidden but a11y-tree visible" idiom —
      // display:none/visibility:hidden are NOT acceptable. The class
      // sr-only typically uses clip + position absolute, so the
      // computed display will not be 'none'.
      liveRegionVisible = cs.display !== 'none'
        && cs.visibility !== 'hidden'
        && live.getAttribute('aria-hidden') !== 'true'
    }

    // Find any focusable inside an aria-hidden=true subtree. WCAG 4.1.2.
    const focusableSelector = 'a[href], button, input:not([type="hidden"]), select, textarea, [tabindex]:not([tabindex="-1"])'
    const focusables = Array.from(document.querySelectorAll(focusableSelector))
    const hiddenInteractives: string[] = []
    for (const el of focusables) {
      // Walk up ancestors; if any has aria-hidden=true, flag.
      let n: Element | null = el.parentElement
      while (n) {
        if (n.getAttribute && n.getAttribute('aria-hidden') === 'true') {
          const sample = (el.textContent ?? '').slice(0, 60).trim()
          const tid = el.getAttribute('data-testid')
          hiddenInteractives.push(`${el.tagName.toLowerCase()}${tid ? `[testid=${tid}]` : ''}: "${sample}"`)
          break
        }
        n = n.parentElement
      }
    }

    return {
      liveRegionPresent: live !== null,
      liveRegionPoliteness,
      liveRegionVisible,
      hiddenInteractives,
    }
  })
}

test.describe('E2E_L_06_SCREEN_READER_SESSION', () => {
  test('aria-live region present + interactives never inside aria-hidden across signed-in routes @epic-l @l-06 @a11y @aria', async ({ page }, testInfo) => {
    test.setTimeout(120_000)
    const diag = attachDiagnostics(page)

    await provisionAndSignIn(page)

    const allFindings: Record<string, AriaAudit> = {}
    const allHiddenInteractives: { route: string; entry: string }[] = []

    for (const route of SIGNED_IN_PROBE_ROUTES) {
      await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' })
      await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {})

      const audit = await auditAria(page)
      allFindings[route] = audit

      expect(audit.liveRegionPresent,
        `#cena-live-region must be in the DOM on ${route} (it's mounted in App.vue and persists across routes)`,
      ).toBe(true)

      expect(audit.liveRegionPoliteness,
        `#cena-live-region on ${route} must carry aria-live="polite"`,
      ).toBe('polite')

      expect(audit.liveRegionVisible,
        `#cena-live-region on ${route} must NOT be hidden via display:none / visibility:hidden / aria-hidden=true ` +
        `(SR-only via clip is fine, but those three break SR announcements)`,
      ).toBe(true)

      for (const e of audit.hiddenInteractives) {
        allHiddenInteractives.push({ route, entry: e })
      }
    }

    testInfo.attach('l-06-aria-audit.json', {
      body: JSON.stringify({
        findings: allFindings,
        hiddenInteractives: allHiddenInteractives,
        diag,
      }, null, 2),
      contentType: 'application/json',
    })

    expect(allHiddenInteractives,
      `Found ${allHiddenInteractives.length} interactive element(s) inside aria-hidden=true subtree across signed-in routes. ` +
      `WCAG 4.1.2 violation — these are reachable by Tab but invisible to AT. ` +
      `${allHiddenInteractives.length === 0 ? '' : 'First 3: ' + JSON.stringify(allHiddenInteractives.slice(0, 3))}`,
    ).toEqual([])

    // ── Runtime announcement check (added 2026-04-29 per honest gap
    //    audit A.8). The earlier version of this spec only verified that
    //    #cena-live-region was present + visible — it didn't verify that
    //    text inserted into the region actually CHANGES (the polite live
    //    region's whole purpose). A regression where someone clears the
    //    region's MutationObserver wiring or replaces it with an
    //    aria-live="off" wrapper would NOT have been caught.
    //
    //    This block uses a MutationObserver injected from the test side
    //    to watch #cena-live-region for textContent changes, then
    //    triggers a known announce path (A11yToolbar's contrast toggle
    //    calls announce() which writes to the region per
    //    A11yToolbar.vue:announce). If textContent never updates, the
    //    announcement pipeline is broken end-to-end.
    await page.goto(`${STUDENT_SPA_BASE_URL}/home`, { waitUntil: 'domcontentloaded' })
    await page.waitForLoadState('networkidle', { timeout: 8_000 }).catch(() => {})

    // Hook a MutationObserver before triggering the announce.
    await page.evaluate(() => {
      (window as unknown as { __liveRegionUpdates: string[] }).__liveRegionUpdates = []
      const region = document.getElementById('cena-live-region')
      if (!region) return
      const obs = new MutationObserver(() => {
        const text = (region.textContent ?? '').trim()
        if (text.length > 0) {
          (window as unknown as { __liveRegionUpdates: string[] }).__liveRegionUpdates.push(text)
        }
      })
      obs.observe(region, { childList: true, characterData: true, subtree: true })
    })

    // Trigger the announce via the canonical UX path: toolbar handle →
    // drawer → contrast toggle. The toggle's onContrastToggle invokes
    // announce() which writes localised text into #cena-live-region.
    await page.getByTestId('a11y-toolbar-handle').click()
    await page.getByTestId('a11y-toolbar-drawer').waitFor({ state: 'visible', timeout: 5_000 })
    const checkbox = page.getByTestId('a11y-contrast-toggle').locator('input[type="checkbox"]').first()
    await checkbox.scrollIntoViewIfNeeded({ timeout: 5_000 }).catch(() => {})
    await checkbox.click({ force: true })
    await page.waitForTimeout(500)

    const announceUpdates = await page.evaluate(() =>
      (window as unknown as { __liveRegionUpdates: string[] }).__liveRegionUpdates,
    )
    expect(announceUpdates.length,
      `aria-live region must receive at least one textContent update after the contrast toggle ` +
      `(A11yToolbar's onContrastToggle → announce() pipeline). Got ${announceUpdates.length} updates: ` +
      `${JSON.stringify(announceUpdates)}. ` +
      `Regression class: announce() helper unwired from store mutations, OR #cena-live-region replaced.`,
    ).toBeGreaterThan(0)

    const appErrors = diag.pageErrors.filter(e =>
      !/localStorage.*access is denied|opaque origin/i.test(e),
    )
    expect(appErrors).toEqual([])
  })
})
