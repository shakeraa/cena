import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw05a'

async function seedAuthedOnboarded(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-home',
      email: 'u-home@example.com',
      displayName: 'Home User',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-home',
      displayName: 'Home User',
      email: 'u-home@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
  })
}

test.describe.serial('STU-W-05A home dashboard', () => {
  test('E2E #1 /home renders greeting + KPI grid + quick actions', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    // Greeting visible
    await expect(page.locator('.home-greeting')).toBeVisible()

    // KPI cards all rendered
    await expect(page.locator('[data-testid="streak-widget"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-minutes-today"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-questions"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-accuracy"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-level"]')).toBeVisible()

    // Quick actions grid
    await expect(page.locator('[data-testid="quick-actions"]')).toBeVisible()
    await expect(page.locator('[data-testid="quick-action-session"]')).toBeVisible()
    await expect(page.locator('[data-testid="quick-action-tutor"]')).toBeVisible()
    await expect(page.locator('[data-testid="quick-action-challenge"]')).toBeVisible()
    await expect(page.locator('[data-testid="quick-action-progress"]')).toBeVisible()

    // Resume session card: STU-W-05A had it from hard-coded mock data,
    // STU-W-05B hid it until /api/sessions/active from STB-01 lands
    // (STU-W-05C wires the real endpoint). Just assert it's NOT rendered
    // in the /api/me-driven state.
    await expect(page.locator('[data-testid="resume-session-card"]')).toHaveCount(0)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/home-desktop.png`, fullPage: true })
  })

  test('E2E #2 clicking Start Session quick action navigates to /session', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="quick-action-session"]')
    await page.locator('[data-testid="quick-action-session"]').click()
    await page.waitForURL(url => new URL(url).pathname === '/session')
    expect(new URL(page.url()).pathname).toBe('/session')
  })

  test.skip('E2E #3 resume-session CTA deep-links to the active session', async () => {
    // Skipped in STU-W-05B — the resume session card is hidden until
    // /api/sessions/active from STB-01 lands. STU-W-05C re-enables this
    // test once the real active-session endpoint is wired.
  })

  test('E2E #4 /home on mobile viewport — KPI grid reflows + bottom nav visible', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.setViewportSize({ width: 500, height: 900 })
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    // Bottom nav should be visible on narrow viewport
    await expect(page.locator('[data-testid="bottom-nav-home"]').first()).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/home-mobile.png`, fullPage: true })
  })
})
