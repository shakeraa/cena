import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw14'

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

test.describe.serial('STU-W-14 notifications + profile + settings', () => {
  test('E2E #1 /notifications renders list with unread banner', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/notifications')
    await page.waitForSelector('[data-testid="notifications-page"]')

    await expect(page.locator('[data-testid="unread-banner"]')).toContainText('3')
    await expect(page.locator('[data-testid="mark-all-read"]')).toBeVisible()

    const items = await page.locator('[data-testid^="notification-n-"]').count()

    expect(items).toBe(7)

    // First one is unread
    const first = page.locator('[data-testid="notification-n-1"]')

    await expect(first).toHaveAttribute('data-read', 'false')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/notifications.png`, fullPage: true })
  })

  test('E2E #2 clicking an unread notification marks it as read', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/notifications')
    await page.waitForSelector('[data-testid="notification-n-1"]')

    await page.locator('[data-testid="notification-n-1"]').click()

    await expect.poll(
      async () => await page.locator('[data-testid="notification-n-1"]').getAttribute('data-read'),
    ).toBe('true')
  })

  test('E2E #3 mark-all-read clears the unread banner', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/notifications')
    await page.waitForSelector('[data-testid="mark-all-read"]')

    await page.locator('[data-testid="mark-all-read"]').click()

    await expect.poll(
      async () => await page.locator('[data-testid="unread-banner"]').count(),
      { timeout: 5000 },
    ).toBe(0)
  })

  test('E2E #4 /profile renders name + email + favorite subjects', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/profile')
    await page.waitForSelector('[data-testid="profile-page"]')

    await expect(page.locator('[data-testid="profile-name"]')).toContainText('Dev Student')
    await expect(page.locator('[data-testid="profile-email"]')).toContainText('dev-student@example.com')
    await expect(page.locator('[data-testid="profile-subjects"]')).toBeVisible()
    await expect(page.locator('[data-testid="profile-edit-btn"]')).toBeVisible()
  })

  test('E2E #5 /profile/edit saves a new display name', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/profile/edit')
    await page.waitForSelector('[data-testid="profile-edit-page"]')

    await page.locator('[data-testid="edit-display-name"] input').first().fill('New Name')
    await page.locator('[data-testid="edit-save"]').click()

    await expect(page.locator('[data-testid="edit-saved"]')).toBeVisible()
  })

  test('E2E #6 /settings renders 4 section cards', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings')
    await page.waitForSelector('[data-testid="settings-index-page"]')

    await expect(page.locator('[data-testid="settings-link-account"]')).toBeVisible()
    await expect(page.locator('[data-testid="settings-link-appearance"]')).toBeVisible()
    await expect(page.locator('[data-testid="settings-link-notifications"]')).toBeVisible()
    await expect(page.locator('[data-testid="settings-link-privacy"]')).toBeVisible()
  })

  test('E2E #7 /settings/appearance theme picker toggles dark mode', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/appearance')
    await page.waitForSelector('[data-testid="settings-appearance-page"]')

    await page.locator('[data-testid="theme-dark"]').click()

    const storedTheme = await page.evaluate(() => localStorage.getItem('cena-student-theme'))

    expect(storedTheme).toBe('dark')
  })

  test('E2E #8 /settings/account shows student id + sign out button', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/account')
    await page.waitForSelector('[data-testid="settings-account-page"]')

    await expect(page.locator('[data-testid="account-student-id"]')).toBeVisible()
    await expect(page.locator('[data-testid="account-role"]')).toBeVisible()
    await expect(page.locator('[data-testid="account-sign-out"]')).toBeVisible()
  })

  test('E2E #9 /settings/notifications toggles persist to localStorage', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/notifications')
    await page.waitForSelector('[data-testid="settings-notifications-page"]')

    await page.locator('[data-testid="pref-push"] input').first().click()

    const stored = await page.evaluate(() => localStorage.getItem('cena-notification-prefs'))

    expect(stored).toContain('pushNotifications')
  })

  test('E2E #10 /settings/privacy toggles persist to localStorage', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/privacy')
    await page.waitForSelector('[data-testid="settings-privacy-page"]')

    await page.locator('[data-testid="privacy-analytics"] input').first().click()

    const stored = await page.evaluate(() => localStorage.getItem('cena-privacy-prefs'))

    expect(stored).toContain('shareAnalytics')
  })
})
