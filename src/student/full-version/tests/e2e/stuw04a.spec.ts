import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const SCREENSHOT_DIR = 'test-results/stuw04a'

async function clearAuth(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.removeItem('cena-mock-auth')
    localStorage.removeItem('cena-mock-me')
  })
}

test.describe.serial('STU-W-04A auth UI', () => {
  test('E2E #1 /login renders form and submits valid credentials → /home', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="email-password-form"]')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/login-empty.png` })

    await page.locator('[data-testid="auth-email"] input').fill('user@example.com')
    await page.locator('[data-testid="auth-password"] input').fill('secret123')
    await page.locator('[data-testid="auth-submit"]').click()

    await page.waitForURL(url => new URL(url).pathname === '/home')
    expect(new URL(page.url()).pathname).toBe('/home')
  })

  test('E2E #2 /login?returnTo=/progress/mastery → sign in → lands at returnTo', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login?returnTo=/progress/mastery')
    await page.waitForSelector('[data-testid="email-password-form"]')
    await page.locator('[data-testid="auth-email"] input').fill('user@example.com')
    await page.locator('[data-testid="auth-password"] input').fill('secret123')
    await page.locator('[data-testid="auth-submit"]').click()

    await page.waitForURL(url => new URL(url).pathname === '/progress/mastery')
    expect(new URL(page.url()).pathname).toBe('/progress/mastery')
  })

  test('E2E #3 /login rejects fail@test.com with inline error', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="email-password-form"]')
    await page.locator('[data-testid="auth-email"] input').fill('fail@test.com')
    await page.locator('[data-testid="auth-password"] input').fill('secret123')
    await page.locator('[data-testid="auth-submit"]').click()

    await expect(page.locator('[data-testid="auth-error"]')).toBeVisible()
    expect(new URL(page.url()).pathname).toBe('/login')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/login-error.png` })
  })

  test('E2E #3b /login soft-locks submit after 3 failed attempts', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/login')
    await page.waitForSelector('[data-testid="email-password-form"]')

    for (let i = 0; i < 3; i++) {
      await page.locator('[data-testid="auth-email"] input').fill('fail@test.com')
      await page.locator('[data-testid="auth-password"] input').fill('secret123')
      await page.locator('[data-testid="auth-submit"]').click()
      await expect(page.locator('[data-testid="auth-error"]')).toBeVisible()
    }

    // After 3 failures the submit button is disabled (soft lockout)
    await expect(page.locator('[data-testid="auth-submit"]')).toBeDisabled()
  })

  test('E2E #4 /register fills form, submits → /onboarding', async ({ page }) => {
    await clearAuth(page)
    await page.goto('/register')
    await page.waitForSelector('[data-testid="email-password-form"]')
    await page.locator('[data-testid="auth-display-name"] input').fill('Alice')
    await page.locator('[data-testid="auth-email"] input').fill('alice@example.com')
    await page.locator('[data-testid="auth-password"] input').fill('secret123')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/register-filled.png` })
    await page.locator('[data-testid="auth-submit"]').click()

    await page.waitForURL(url => new URL(url).pathname === '/onboarding')
    expect(new URL(page.url()).pathname).toBe('/onboarding')
  })

  test('E2E #5 /forgot-password submits email → POST /api/auth/password-reset → 204 → success message', async ({ page }) => {
    await clearAuth(page)

    // Track outbound requests to /api/auth/password-reset.
    const passwordResetRequests: { url: string; method: string; status: number }[] = []

    // Intercept the POST and return 204 No Content (the backend contract).
    await page.route('**/api/auth/password-reset', async route => {
      const request = route.request()

      passwordResetRequests.push({
        url: request.url(),
        method: request.method(),
        status: 204,
      })
      await route.fulfill({ status: 204, body: '' })
    })

    await page.goto('/forgot-password')

    // Form, email input, and submit button are visible.
    await expect(page.locator('[data-testid="forgot-password-form"]')).toBeVisible()
    await expect(page.locator('[data-testid="forgot-email"]')).toBeVisible()
    await expect(page.locator('[data-testid="forgot-submit"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/forgot-form.png` })

    // Fill in the email and submit.
    await page.locator('[data-testid="forgot-email"] input').fill('student@example.com')
    await page.locator('[data-testid="forgot-submit"]').click()

    // After successful 204, the success state replaces the form.
    await expect(page.locator('[data-testid="forgot-success-icon"]')).toBeVisible()
    await expect(page.locator('[data-testid="forgot-return-to-login"]')).toBeVisible()

    // The form is no longer visible.
    await expect(page.locator('[data-testid="forgot-password-form"]')).toHaveCount(0)

    // Exactly one POST /api/auth/password-reset was issued.
    expect(passwordResetRequests).toHaveLength(1)
    expect(passwordResetRequests[0].method).toBe('POST')
    expect(passwordResetRequests[0].status).toBe(204)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/forgot-success.png` })
  })

  test('E2E #6 auth pages pass axe in light mode', async ({ page }) => {
    await clearAuth(page)
    for (const path of ['/login', '/register', '/forgot-password'] as const) {
      await page.goto(path)
      await page.waitForSelector('[data-testid="email-password-form"], [data-testid="forgot-password-form"]')

      const results = await new AxeBuilder({ page })
        .exclude('.v-overlay-container')
        .exclude('.vue-devtools__anchor-btn')
        .disableRules(['color-contrast'])
        .analyze()

      const serious = results.violations.filter(v => ['serious', 'critical'].includes(v.impact || ''))
      if (serious.length > 0)
        console.warn(`axe violations on ${path}:`, JSON.stringify(serious, null, 2))
      expect(serious.length, `serious axe violations on ${path}`).toBe(0)
    }
  })
})
