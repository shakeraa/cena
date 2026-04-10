import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw11'

async function seedAuthedOnboarded(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-dev-student',
      email: 'dev-student@example.com',
      displayName: 'Dev Student',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-dev-student',
      displayName: 'Dev Student',
      email: 'dev-student@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
  })
}

test.describe.serial('STU-W-11 challenges hub', () => {
  test('E2E #1 /challenges renders daily + boss + chains + tournaments sections', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/challenges')
    await page.waitForSelector('[data-testid="challenges-page"]')

    // Daily challenge hero
    await expect(page.locator('[data-testid="daily-challenge-card"]')).toBeVisible()
    await expect(page.locator('[data-testid="daily-challenge-card"]')).toContainText('Mental Math Sprint')
    await expect(page.locator('[data-testid="daily-difficulty"]')).toBeVisible()
    await expect(page.locator('[data-testid="daily-start"]')).toContainText('Start')

    // Boss battles — 3 available + 2 locked
    await expect(page.locator('[data-testid="boss-section"]')).toBeVisible()
    await expect(page.locator('[data-testid="boss-boss-algebra-overlord"]')).toBeVisible()
    await expect(page.locator('[data-testid="boss-boss-calculus-king"]')).toBeVisible()

    // Chains
    await expect(page.locator('[data-testid="chains-section"]')).toBeVisible()
    await expect(page.locator('[data-testid="chain-chain-algebra-fundamentals"]')).toContainText('Algebra Fundamentals')
    await expect(page.locator('[data-testid="chain-chain-algebra-fundamentals"]')).toContainText('6 / 10')

    // Tournaments
    await expect(page.locator('[data-testid="tournaments-section"]')).toBeVisible()
    await expect(page.locator('[data-testid="tournament-active-tourn-weekly-47"]')).toContainText('Weekly Sprint #47')
    await expect(page.locator('[data-testid="tournament-upcoming-tourn-spring-2026"]')).toContainText('Spring Math Masters 2026')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/challenges-desktop.png`, fullPage: true })
  })

  test('E2E #2 locked boss battle shows the required mastery level', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/challenges')
    await page.waitForSelector('[data-testid="challenges-page"]')

    const locked = page.locator('[data-testid="boss-boss-calculus-king"]')

    await expect(locked).toBeVisible()
    await expect(locked.locator('[data-testid="boss-lock-reason"]')).toContainText('8')
  })

  test('E2E #3 /challenges on mobile viewport reflows without overflow', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.setViewportSize({ width: 500, height: 900 })
    await page.goto('/challenges')
    await page.waitForSelector('[data-testid="challenges-page"]')

    await expect(page.locator('[data-testid="daily-challenge-card"]')).toBeVisible()
    await expect(page.locator('[data-testid="boss-boss-algebra-overlord"]')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/challenges-mobile.png`, fullPage: true })
  })
})
