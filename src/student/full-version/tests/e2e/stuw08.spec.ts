import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw08'

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

test.describe.serial('STU-W-08 tutor UI', () => {
  test('E2E #1 /tutor renders seeded threads list', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/tutor')
    await page.waitForSelector('[data-testid="tutor-page"]')

    await expect(page.locator('[data-testid="tutor-list"]')).toBeVisible()
    await expect(page.locator('[data-testid="tutor-thread-th-1"]')).toBeVisible()
    await expect(page.locator('[data-testid="tutor-thread-th-1"]')).toContainText('Help with quadratic equations')
    await expect(page.locator('[data-testid="tutor-thread-th-2"]')).toContainText('Photosynthesis questions')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/tutor-list.png` })
  })

  test('E2E #2 clicking a thread opens /tutor/:threadId with message history', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/tutor')
    await page.waitForSelector('[data-testid="tutor-thread-th-1"]')
    await page.locator('[data-testid="tutor-thread-th-1"]').click()

    await page.waitForURL(url => new URL(url).pathname === '/tutor/th-1')
    await page.waitForSelector('[data-testid="tutor-thread-page"]')

    // 4 seeded messages should be visible
    await expect(page.locator('[data-testid^="tutor-message-msg-"]')).toHaveCount(4)
    await expect(page.locator('[data-testid="tutor-messages"]')).toContainText('quadratic formula')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/tutor-thread.png`, fullPage: true })
  })

  test('E2E #3 sending a message appends both user + assistant bubbles', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/tutor/th-1')
    await page.waitForSelector('[data-testid="tutor-compose-form"]')

    const before = await page.locator('[data-testid^="tutor-message-"]').count()

    await page.locator('[data-testid="tutor-compose-input"] textarea').first().fill('Can you give me another example?')
    await page.locator('[data-testid="tutor-compose-submit"]').click()

    // Wait for the assistant reply (2 new bubbles: user + assistant)
    await expect.poll(
      async () => await page.locator('[data-testid^="tutor-message-"]').count(),
      { timeout: 5000 },
    ).toBe(before + 2)

    // Last bubble should be the assistant
    const lastRole = await page.locator('[data-testid^="tutor-message-"]').last().getAttribute('data-role')

    expect(lastRole).toBe('assistant')
  })

  test('E2E #4 "New conversation" button creates and navigates to new thread', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/tutor')
    await page.waitForSelector('[data-testid="tutor-new-thread"]')

    await page.locator('[data-testid="tutor-new-thread"]').click()

    // Should navigate to a new /tutor/th-<random> URL
    await page.waitForURL(url => {
      const path = new URL(url).pathname

      return path.startsWith('/tutor/') && path !== '/tutor/' && path !== '/tutor'
    })

    await page.waitForSelector('[data-testid="tutor-thread-page"]')
    // Empty state should be visible
    await expect(page.locator('[data-testid="tutor-messages-empty"]')).toBeVisible()
  })
})
