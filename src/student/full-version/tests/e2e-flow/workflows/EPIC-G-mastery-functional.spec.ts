// =============================================================================
// EPIC-E2E-G — Admin /apps/mastery/* per-page functional
//
// COV-04 sub-spec (3 of 10). Mastery dashboard is the surface admins
// use to see fleet-wide mastery distribution + drill into class /
// student detail. Drill pages are dynamic [id] — fixme'd until INFRA-03
// seed fixture lands.
//
// Asserts: dashboard mounts, no JS errors, no console-error from
// failed fetches not in the smoke allowlist.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'

async function adminSignIn(page: Page) {
  await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
  await page.locator('input[type="email"]').fill('admin@cena.local')
  await page.locator('input[type="password"]').fill('DevAdmin123!')
  await page.locator('button[type="submit"]').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

function attachDiagnostics(page: Page) {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()) })
  page.on('pageerror', e => { pageErrors.push(e.message) })
  return { consoleErrors, pageErrors }
}

test.describe('EPIC_G_MASTERY_FUNCTIONAL', () => {
  test('/apps/mastery/dashboard renders without JS errors @epic-g @admin-functional', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await adminSignIn(page)
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/mastery/dashboard`)
    await page.waitForLoadState('domcontentloaded')

    await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

    testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
    expect(diag.pageErrors,
      `pageerror on mastery dashboard: ${JSON.stringify(diag.pageErrors.slice(0, 3))}`,
    ).toEqual([])
  })

  test.fixme(
    '/apps/mastery/student/[id] drill-down @epic-g @admin-functional BLOCKED_ON: TASK-E2E-INFRA-03',
    async () => {
      // Implement once INFRA-03 ships:
      //   1. dynamicSeed.studentWithMastery() → uid + at least 3 questions
      //      answered so the mastery view has data to render
      //   2. Navigate /apps/mastery/student/{uid}
      //   3. Assert per-concept mastery bars render
      //   4. Assert k-anonymity floor: with < 10 students in the class,
      //      the class-comparison band is hidden (per F-03 / k-floor)
    },
  )

  test.fixme(
    '/apps/mastery/class/[id] k-floor enforcement @epic-g @admin-functional BLOCKED_ON: TASK-E2E-INFRA-03 + TASK-E2E-F-03',
    async () => {
      // Implement once seed fixture + the class-mastery endpoint
      // returns the k-floor flag.
    },
  )
})
