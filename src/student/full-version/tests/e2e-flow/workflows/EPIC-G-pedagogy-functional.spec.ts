// =============================================================================
// EPIC-E2E-G — Admin /apps/pedagogy/* per-page functional
//
// COV-04 sub-spec (5 of 10). Three pedagogy surfaces:
//   /apps/pedagogy/methodology         — methodology config table
//   /apps/pedagogy/methodology-hierarchy — tree visualization
//   /apps/pedagogy/mcm-graph           — error-type × concept matrix
//
// All three render-only smoke. mcm-graph had a JS-undefined regression
// fixed earlier (data.errorTypes ?? []) — that fix lives in
// src/admin/.../pedagogy/mcm-graph.vue, this spec catches future
// regressions of the same shape.
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

test.describe('EPIC_G_PEDAGOGY_FUNCTIONAL', () => {
  for (const route of [
    '/apps/pedagogy/methodology',
    '/apps/pedagogy/methodology-hierarchy',
    '/apps/pedagogy/mcm-graph',
  ]) {
    test(`${route} mounts + handles empty-corpus without JS errors @epic-g @admin-functional`, async ({ page }, testInfo) => {
      test.setTimeout(45_000)
      const diag = attachDiagnostics(page)

      await adminSignIn(page)
      await page.goto(`${ADMIN_SPA_BASE_URL}${route}`)
      await page.waitForLoadState('domcontentloaded')

      // Heading visible — proves the page mounted under the auth shell
      // and the route guard didn't reject the SUPER_ADMIN session.
      await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

      // Specific regression: mcm-graph had `Cannot read properties of
      // undefined (reading 'length')` on partial API body. Fixed in
      // a3cfcc5c. This catches future regressions of the same shape.
      testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
      expect(diag.pageErrors,
        `pageerror on ${route}: ${JSON.stringify(diag.pageErrors.slice(0, 3))}`,
      ).toEqual([])
    })
  }
})
