import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/find-ux-024'

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

test.describe.serial('FIND-ux-024 class-feed reaction toggle + error surface', () => {
  test('heart click increments count, second click decrements', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social')
    await page.waitForSelector('[data-testid="social-feed-page"]')
    await page.waitForSelector('[data-testid="feed-list"]')

    // Read the initial count from item f1 (should be 12)
    const reactBtn = page.locator('[data-testid="react-f1"]')

    await expect(reactBtn).toBeVisible()

    const initialText = await reactBtn.innerText()
    const initialCount = Number.parseInt(initialText.trim(), 10)

    expect(initialCount).toBe(12)

    // Click once: 12 -> 13
    await reactBtn.click()
    await page.waitForTimeout(500) // wait for mutation + refresh cycle

    // After refresh the feed re-renders; re-locate
    const afterFirst = page.locator('[data-testid="react-f1"]')

    await expect.poll(
      async () => {
        const text = await afterFirst.innerText()

        return Number.parseInt(text.trim(), 10)
      },
      { timeout: 5000 },
    ).toBe(initialCount + 1)

    // Click again: 13 -> 12 (toggle off)
    await afterFirst.click()
    await page.waitForTimeout(500)

    const afterSecond = page.locator('[data-testid="react-f1"]')

    await expect.poll(
      async () => {
        const text = await afterSecond.innerText()

        return Number.parseInt(text.trim(), 10)
      },
      { timeout: 5000 },
    ).toBe(initialCount)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/react-toggle.png`, fullPage: true })
  })

  test('heart button has accessible aria-label', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social')
    await page.waitForSelector('[data-testid="feed-list"]')

    const reactBtn = page.locator('[data-testid="react-f1"]')
    const ariaLabel = await reactBtn.getAttribute('aria-label')

    expect(ariaLabel).toMatch(/Like, \d+ reactions/)
  })

  test('peer vote buttons still surface properly', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/peers')
    await page.waitForSelector('[data-testid="peers-page"]')

    const upvoteBtn = page.locator('[data-testid="upvote-sol-1"]')

    await expect(upvoteBtn).toBeVisible()

    // Read initial count
    const initialText = await upvoteBtn.innerText()
    const initialCount = Number.parseInt(initialText.trim(), 10)

    // Click upvote
    await upvoteBtn.click()
    await page.waitForTimeout(500)

    const afterUpvote = page.locator('[data-testid="upvote-sol-1"]')

    await expect.poll(
      async () => {
        const text = await afterUpvote.innerText()

        return Number.parseInt(text.trim(), 10)
      },
      { timeout: 5000 },
    ).toBe(initialCount + 1)
  })

  test('friend accept flow still works', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/social/friends')
    await page.waitForSelector('[data-testid="friends-page"]')

    const before = await page.locator('[data-testid^="friend-u-"]').count()

    await page.locator('[data-testid="accept-req-1"]').click()

    await expect.poll(
      async () => await page.locator('[data-testid^="friend-u-"]').count(),
      { timeout: 5000 },
    ).toBe(before + 1)

    // The request card should be gone
    await expect(page.locator('[data-testid="request-req-1"]')).toHaveCount(0)
  })
})
