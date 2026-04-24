import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw12'

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

test.describe.serial('STU-W-12 social pages', () => {
  test('E2E #1 /social class feed renders 8 items', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social')
    await page.waitForSelector('[data-testid="social-feed-page"]')

    await expect(page.locator('[data-testid="feed-item-f1"]')).toBeVisible()

    const items = await page.locator('[data-testid^="feed-item-f"]').count()

    expect(items).toBe(8)

    // First item is an achievement
    await expect(page.locator('[data-testid="feed-item-f1"]')).toContainText('Achievement')
    await expect(page.locator('[data-testid="feed-item-f1"]')).toContainText('Alex Chen')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/feed.png`, fullPage: true })
  })

  test('E2E #2 /social/peers renders 5 peer solutions with vote buttons', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/peers')
    await page.waitForSelector('[data-testid="peers-page"]')

    const cards = await page.locator('[data-testid^="peer-solution-sol-"]').count()

    expect(cards).toBe(5)
    await expect(page.locator('[data-testid="peer-solution-sol-1"]')).toContainText('distributive')
    await expect(page.locator('[data-testid="upvote-sol-1"]')).toBeVisible()
    await expect(page.locator('[data-testid="downvote-sol-1"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/peers.png`, fullPage: true })
  })

  test('E2E #3 /social/friends renders pending requests + friends list', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/friends')
    await page.waitForSelector('[data-testid="friends-page"]')

    await expect(page.locator('[data-testid="pending-requests-section"]')).toBeVisible()
    await expect(page.locator('[data-testid="request-req-1"]')).toContainText('Casey Kim')
    await expect(page.locator('[data-testid="request-req-2"]')).toContainText('Riley Evans')

    const friends = await page.locator('[data-testid^="friend-u-"]').count()

    expect(friends).toBeGreaterThanOrEqual(4)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/friends.png`, fullPage: true })
  })

  test('E2E #4 accepting a friend request moves it to the friends list', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/friends')
    await page.waitForSelector('[data-testid="friends-page"]')

    const before = await page.locator('[data-testid^="friend-u-"]').count()

    await page.locator('[data-testid="accept-req-1"]').click()

    // After mutation + refetch, friend count goes up and the request disappears
    await expect.poll(
      async () => await page.locator('[data-testid^="friend-u-"]').count(),
      { timeout: 5000 },
    ).toBe(before + 1)
    await expect(page.locator('[data-testid="request-req-1"]')).toHaveCount(0)
  })
})
