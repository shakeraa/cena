import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const SCREENSHOT_DIR = 'test-results/stuw04b'

async function clearAuth(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.removeItem('cena-mock-auth')
    localStorage.removeItem('cena-mock-me')
  })
}

test.describe.serial('STU-W-04B OAuth provider buttons', () => {
  test('E2E #1 all four providers render on /login', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="auth-provider-buttons"]')
    await expect(page.locator('[data-testid="auth-provider-google"]')).toBeVisible()
    await expect(page.locator('[data-testid="auth-provider-apple"]')).toBeVisible()
    await expect(page.locator('[data-testid="auth-provider-microsoft"]')).toBeVisible()
    await expect(page.locator('[data-testid="auth-provider-phone"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/login-providers.png`, fullPage: true })
  })

  test('E2E #2 clicking Google signs the user in and navigates to /home', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="auth-provider-google"]')
    await page.locator('[data-testid="auth-provider-google"]').click()
    await page.waitForURL(url => new URL(url).pathname === '/home')
    expect(new URL(page.url()).pathname).toBe('/home')
  })

  test('E2E #3 clicking Google on /register lands the fresh account at /onboarding', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/register')
    await page.waitForSelector('[data-testid="auth-provider-google"]')
    await page.locator('[data-testid="auth-provider-google"]').click()
    await page.waitForURL(url => new URL(url).pathname === '/onboarding')
    expect(new URL(page.url()).pathname).toBe('/onboarding')
  })

  test('E2E #4 clicking Phone shows the coming-soon placeholder and stays on /login', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="auth-provider-phone"]')
    await page.locator('[data-testid="auth-provider-phone"]').click()
    await expect(page.locator('[data-testid="auth-provider-phone-message"]')).toBeVisible()
    expect(new URL(page.url()).pathname).toBe('/login')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/phone-coming-soon.png` })
  })

  test('E2E #5 returnTo is honored after OAuth sign-in on /login', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login?returnTo=/progress/mastery')
    await page.waitForSelector('[data-testid="auth-provider-apple"]')
    await page.locator('[data-testid="auth-provider-apple"]').click()
    await page.waitForURL(url => new URL(url).pathname === '/progress/mastery')
    expect(new URL(page.url()).pathname).toBe('/progress/mastery')
  })

  test('E2E #6 /login with provider buttons passes axe in light mode', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="auth-provider-buttons"]')

    const results = await new AxeBuilder({ page })
      .exclude('.v-overlay-container')
      .exclude('.vue-devtools__anchor-btn')
      .disableRules(['color-contrast'])
      .analyze()

    const serious = results.violations.filter(v => ['serious', 'critical'].includes(v.impact || ''))
    if (serious.length > 0)
      console.warn('axe violations:', JSON.stringify(serious, null, 2))
    expect(serious.length).toBe(0)
  })
})
