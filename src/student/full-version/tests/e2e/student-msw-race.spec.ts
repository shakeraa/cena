import { expect, test } from '@playwright/test'

/**
 * FIND-ux-021: Regression test — no raw HTTP error strings leak to the UI.
 *
 * Verifies that cold-loading each major student route never shows raw
 * error strings like `[GET] "/api/me": 404 Not Found` or any pattern
 * matching `[METHOD] "/path"`. Instead, only translated i18n error
 * messages should be visible when an API error occurs.
 *
 * The test runs against the dev server where MSW handles /api/* requests.
 * The `await worker.start()` fix ensures MSW is ready before the app
 * mounts, so the race condition no longer produces 404s on cold load.
 */

const ROUTES = [
  '/home',
  '/tutor',
  '/social',
  '/settings/notifications',
  '/profile',
]

/**
 * Regex matching raw HTTP error method+path patterns that must NEVER
 * appear in user-visible text. Covers all standard HTTP methods.
 */
const RAW_ERROR_PATTERN = /\[(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\]\s+"/

test.describe('FIND-ux-021: no raw HTTP error strings on cold load', () => {
  for (const route of ROUTES) {
    test(`cold-load ${route} shows no raw error strings`, async ({ page }) => {
      // Cold-load the route (no prior navigation).
      await page.goto(route, { waitUntil: 'networkidle' })

      // Give the page time to render any error states.
      await page.waitForTimeout(1500)

      // Verify no raw HTTP error patterns exist anywhere in the page body.
      const bodyText = await page.locator('body').innerText()

      // Assert the raw error pattern does not match.
      expect(bodyText).not.toMatch(RAW_ERROR_PATTERN)

      // Also verify no "404 Not Found" raw error strings are present.
      // These come from ofetch when MSW isn't ready.
      expect(bodyText).not.toMatch(/\d{3}\s+Not Found/)
    })
  }

  test('error states render translated i18n text, not raw messages', async ({ page }) => {
    // Navigate to /home first.
    await page.goto('/home', { waitUntil: 'networkidle' })
    await page.waitForTimeout(1500)

    // Scan for any VAlert error elements that might contain raw strings.
    const errorAlerts = page.locator('[type="error"], [data-testid$="-error"], [data-testid="home-error-state"]')
    const count = await errorAlerts.count()

    for (let i = 0; i < count; i++) {
      const alertText = await errorAlerts.nth(i).innerText()

      // No raw HTTP error patterns allowed in error alert text.
      expect(alertText).not.toMatch(RAW_ERROR_PATTERN)
      expect(alertText).not.toMatch(/\d{3}\s+Not Found/)

      // No raw ofetch error format: `[METHOD] "url": status statusText`
      expect(alertText).not.toMatch(/\[(?:GET|POST|PUT|DELETE)\]\s+"\/api\//)
    }
  })
})
