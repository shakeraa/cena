import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/privacy-gdpr'

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

test.describe.serial('FIND-privacy-003: GDPR self-service privacy settings', () => {
  test('E2E #1 privacy settings page renders all GDPR buttons', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/privacy')
    await page.waitForSelector('[data-testid="settings-privacy-page"]')

    // Existing privacy toggles
    await expect(page.locator('[data-testid="privacy-show-progress"]')).toBeVisible()
    await expect(page.locator('[data-testid="privacy-peer-comparison"]')).toBeVisible()
    await expect(page.locator('[data-testid="privacy-analytics"]')).toBeVisible()

    // GDPR self-service buttons (FIND-privacy-003)
    await expect(page.locator('[data-testid="btn-download-data"]')).toBeVisible()
    await expect(page.locator('[data-testid="btn-delete-data"]')).toBeVisible()
    await expect(page.locator('[data-testid="btn-dsar"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/privacy-page.png` })
  })

  test('E2E #2 download data button opens confirmation dialog', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/privacy')
    await page.waitForSelector('[data-testid="settings-privacy-page"]')

    await page.locator('[data-testid="btn-download-data"]').click()

    await expect(page.locator('[data-testid="export-dialog"]')).toBeVisible()
    await expect(page.locator('[data-testid="export-confirm-btn"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/export-dialog.png` })
  })

  test('E2E #3 delete data button opens confirmation dialog', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/privacy')
    await page.waitForSelector('[data-testid="settings-privacy-page"]')

    await page.locator('[data-testid="btn-delete-data"]').click()

    await expect(page.locator('[data-testid="erasure-dialog"]')).toBeVisible()
    await expect(page.locator('[data-testid="erasure-confirm-btn"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/erasure-dialog.png` })
  })

  test('E2E #4 DSAR button opens dialog with message input', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/settings/privacy')
    await page.waitForSelector('[data-testid="settings-privacy-page"]')

    await page.locator('[data-testid="btn-dsar"]').click()

    await expect(page.locator('[data-testid="dsar-dialog"]')).toBeVisible()
    await expect(page.locator('[data-testid="dsar-message-input"]')).toBeVisible()
    await expect(page.locator('[data-testid="dsar-confirm-btn"]')).toBeDisabled()

    // Type a message to enable the submit button
    await page.locator('[data-testid="dsar-message-input"] textarea').fill('What data do you have about me?')
    await expect(page.locator('[data-testid="dsar-confirm-btn"]')).toBeEnabled()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/dsar-dialog.png` })
  })
})
