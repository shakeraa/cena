import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw13'

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

test.describe.serial('STU-W-13 social leaderboard full page', () => {
  test('E2E #1 /social/leaderboard renders global scope with your rank banner', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/leaderboard')
    await page.waitForSelector('[data-testid="leaderboard-page"]')

    // Scope tabs
    await expect(page.locator('[data-testid="scope-global"]')).toBeVisible()
    await expect(page.locator('[data-testid="scope-class"]')).toBeVisible()
    await expect(page.locator('[data-testid="scope-friends"]')).toBeVisible()

    // Your rank banner
    await expect(page.locator('[data-testid="your-rank-banner"]')).toContainText('#5')

    // Global scope has 20 entries
    await expect(page.locator('[data-testid^="leaderboard-entry-"]')).toHaveCount(20)

    // Current student highlighted at rank 5
    const rank5 = page.locator('[data-testid="leaderboard-entry-5"]')

    await expect(rank5).toContainText('Dev Student')
    await expect(rank5.locator('[data-testid="entry-you-chip"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/leaderboard-global.png`, fullPage: true })
  })

  test('E2E #2 switching to "My class" scope re-fetches + updates your rank', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/leaderboard')
    await page.waitForSelector('[data-testid="scope-class"]')

    await page.locator('[data-testid="scope-class"]').click()

    // Class scope has 8 entries; current student is at rank 1
    await expect.poll(async () => await page.locator('[data-testid^="leaderboard-entry-"]').count()).toBe(8)
    await expect(page.locator('[data-testid="your-rank-banner"]')).toContainText('#1')
    await expect(page.locator('[data-testid="leaderboard-entry-1"]')).toContainText('Dev Student')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/leaderboard-class.png`, fullPage: true })
  })

  test('E2E #3 switching to "Friends" scope shows the friends list with correct rank', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/leaderboard')
    await page.waitForSelector('[data-testid="scope-friends"]')

    await page.locator('[data-testid="scope-friends"]').click()

    // Friends scope has 5 entries; current student is at rank 3
    await expect.poll(async () => await page.locator('[data-testid^="leaderboard-entry-"]').count()).toBe(5)
    await expect(page.locator('[data-testid="your-rank-banner"]')).toContainText('#3')
  })
})
