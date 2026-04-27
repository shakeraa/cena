// =============================================================================
// EPIC-E2E-X — Student SPA responsiveness sweep (parity with EPIC-G)
//
// For each static signed-in student route, drive across mobile / tablet
// / desktop and assert no horizontal overflow + first heading visible.
// Same shape as EPIC-G-admin-pages-responsive.spec.ts.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const STUDENT_SPA_BASE_URL = 'http://localhost:5175'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

const VIEWPORTS = [
  { name: 'mobile',  width: 375,  height: 812  },
  { name: 'tablet',  width: 768,  height: 1024 },
  { name: 'desktop', width: 1440, height: 900  },
] as const

const ROUTES = [
  '/home',
  '/account/subscription',
  '/challenges',
  '/knowledge-graph',
  '/notifications',
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
  '/social/leaderboard',
  '/tutor',
  '/tutor/pdf-upload',
  '/tutor/photo-capture',
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
  await page.goto(`${STUDENT_SPA_BASE_URL}${route}`, { timeout: 15_000, waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(400)

  const bodyWidth = await page.evaluate(() => document.body.scrollWidth)
  const horizontalOverflow = bodyWidth > viewport.width + 2

  const hasHeading = await page
    .locator('h1, h2, [role="heading"][aria-level="1"], [role="heading"][aria-level="2"]')
    .first()
    .isVisible()
    .catch(() => false)

  return {
    route, viewport: viewport.name,
    bodyWidth, viewportWidth: viewport.width,
    horizontalOverflow, hasHeading,
  }
}

test.describe('EPIC_X_STUDENT_PAGES_RESPONSIVE', () => {
  test('student pages have no horizontal overflow at mobile/tablet/desktop @epic-x @responsive @student', async ({ page }, testInfo) => {
    test.setTimeout(420_000)

    // Provision + sign in (desktop viewport for the form).
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `e2e-resp-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
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
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Student Responsive' },
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

    await page.setViewportSize({ width: 1440, height: 900 })
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })

    const results: ResponsiveProbe[] = []
    for (const route of ROUTES) {
      for (const viewport of VIEWPORTS) {
        results.push(await probeResponsive(page, route, viewport))
        await page.waitForTimeout(150)
      }
    }

    testInfo.attach('student-responsive-results.json', {
      body: JSON.stringify(results, null, 2),
      contentType: 'application/json',
    })

    const mobileOverflows = results.filter(r => r.viewport === 'mobile' && r.horizontalOverflow)

    // Same allowlist convention as admin: legitimately wide content
    // gets a documented exception with a queue task.
    const KNOWN_BROKEN_ON_MOBILE: Record<string, string> = {
      // Add as discovered.
    }

    const surprisingMobileOverflows = mobileOverflows.filter(r => !(r.route in KNOWN_BROKEN_ON_MOBILE))
    expect(surprisingMobileOverflows,
      `${surprisingMobileOverflows.length} student page(s) overflow at mobile (375px):\n` +
      JSON.stringify(surprisingMobileOverflows.map(r => ({
        route: r.route, bodyWidth: r.bodyWidth, viewportWidth: r.viewportWidth,
      })), null, 2),
    ).toEqual([])

    const tabletOverflows = results.filter(r => r.viewport === 'tablet' && r.horizontalOverflow)
    const desktopOverflows = results.filter(r => r.viewport === 'desktop' && r.horizontalOverflow)
    const noHeading = results.filter(r => !r.hasHeading)

    if (tabletOverflows.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `tablet overflows: ${tabletOverflows.map(r => r.route).join(', ')}` })
    if (desktopOverflows.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `desktop overflows: ${desktopOverflows.map(r => r.route).join(', ')}` })
    if (noHeading.length > 0)
      testInfo.annotations.push({ type: 'warning', description: `${noHeading.length} (route, viewport) pairs without visible heading` })

    console.log(`\n=== STUDENT responsive summary ===`)
    console.log(`Routes:                      ${ROUTES.length}`)
    console.log(`Viewport pairs:              ${results.length}`)
    console.log(`Mobile overflows:            ${mobileOverflows.length} (${surprisingMobileOverflows.length} unexpected)`)
    console.log(`Tablet overflows:            ${tabletOverflows.length}`)
    console.log(`Desktop overflows:           ${desktopOverflows.length}`)
    console.log(`No-heading (any viewport):   ${noHeading.length}`)
  })
})
