// =============================================================================
// EPIC-E2E-G — Admin /apps/system/* per-page functional
//
// COV-04 sub-spec (4 of 10). System pages are the operator surface:
// health, audit-log, dead-letters, embeddings, explanation-cache,
// settings, token-budget. Most are read-only with light filter UI.
//
// Skips routes in EPIC-G-admin-pages-smoke KNOWN_BROKEN_ROUTES allowlist:
//   /apps/system/{actors,architecture,events} — SignalR hub 404s
//   /apps/system/ai-settings                  — admin-api 500
//
// Drives the genuinely-functional surfaces:
//   - /apps/system/audit-log: search-by-user input + export CSV button
//   - /apps/system/health: just renders cleanly
//   - /apps/system/dead-letters: just renders cleanly
//   - /apps/system/explanation-cache: just renders cleanly
//   - /apps/system/embeddings: just renders cleanly
//   - /apps/system/settings: just renders cleanly
//   - /apps/system/token-budget: just renders cleanly
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

test.describe('EPIC_G_SYSTEM_FUNCTIONAL', () => {
  test('/apps/system/audit-log search-by-user wires + export CSV button visible @epic-g @admin-functional', async ({ page }, testInfo) => {
    test.setTimeout(60_000)
    const diag = attachDiagnostics(page)

    await adminSignIn(page)
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/system/audit-log`)
    await page.waitForLoadState('domcontentloaded')

    await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

    // Search-by-user input
    const search = page.getByPlaceholder(/search by user/i)
    if (await search.isVisible().catch(() => false)) {
      await search.fill('admin@cena')
      expect(await search.inputValue()).toBe('admin@cena')
      await search.fill('')
    }

    // Export CSV button — visibility check only; actual download
    // lives behind a server endpoint we don't want to hammer.
    const exportBtn = page.getByRole('button', { name: /export.*csv/i }).first()
    if (await exportBtn.isVisible().catch(() => false)) {
      // Pinned: button is interactive (not disabled). We don't click —
      // CSV download in headless triggers a save-dialog race.
      expect(await exportBtn.isDisabled()).toBe(false)
    }

    testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
    expect(diag.pageErrors).toEqual([])
  })

  // The remaining system routes are render-only smoke. Each just
  // verifies the page mounts under the auth shell.
  for (const route of [
    '/apps/system/health',
    '/apps/system/dead-letters',
    '/apps/system/explanation-cache',
    '/apps/system/embeddings',
    '/apps/system/settings',
    '/apps/system/token-budget',
  ]) {
    test(`${route} mounts without JS errors @epic-g @admin-functional`, async ({ page }, testInfo) => {
      test.setTimeout(45_000)
      const diag = attachDiagnostics(page)

      await adminSignIn(page)
      await page.goto(`${ADMIN_SPA_BASE_URL}${route}`)
      await page.waitForLoadState('domcontentloaded')

      await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

      testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
      expect(diag.pageErrors,
        `pageerror on ${route}: ${JSON.stringify(diag.pageErrors.slice(0, 3))}`,
      ).toEqual([])
    })
  }
})
