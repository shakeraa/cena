import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/rtl-visual-regression'

async function seedAuth(
  page: import('@playwright/test').Page,
  opts: {
    uid: string
    locale: 'en' | 'ar' | 'he'
    onboardedAt?: string | null
  },
) {
  const resolvedOnboardedAt
    = 'onboardedAt' in opts ? opts.onboardedAt : '2026-04-10T00:00:00Z'

  await page.addInitScript(
    o => {
      localStorage.setItem('cena-mock-auth', JSON.stringify({
        uid: o.uid,
        email: `${o.uid}@example.com`,
        displayName: 'RTL Test User',
      }))
      localStorage.setItem('cena-mock-me', JSON.stringify({
        uid: o.uid,
        displayName: 'RTL Test User',
        email: `${o.uid}@example.com`,
        locale: o.locale,
        onboardedAt: o.onboardedAt,
      }))
      localStorage.setItem('cena-student-locale', o.locale)
    },
    { uid: opts.uid, locale: opts.locale, onboardedAt: resolvedOnboardedAt },
  )
}

test.describe.serial('RDY-002 RTL visual regression', () => {
  test('captures Arabic major pages with RTL shell and LTR math islands', async ({ page }) => {
    await seedAuth(page, { uid: 'u-rtl-ar', locale: 'ar' })

    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await expect(page.locator('aside.layout-vertical-nav')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/home-ar.png`, fullPage: true })

    await page.goto('/session')
    await page.waitForSelector('[data-testid="session-setup-page"]')
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/session-setup-ar.png`, fullPage: true })

    await page.goto('/session/rtl-demo')
    await page.waitForSelector('[data-testid="question-card"]')
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await expect(page.locator('[data-testid="question-prompt"] bdi[dir="ltr"]')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/session-question-ar.png`, fullPage: true })

    await page.goto('/progress')
    await page.waitForSelector('[data-testid="progress-page"]')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/progress-ar.png`, fullPage: true })

    await page.goto('/progress/sessions')
    await page.waitForSelector('[data-testid="progress-sessions-page"]')
    await expect(page.locator('[data-testid="student-breadcrumbs"]')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/progress-sessions-ar.png`, fullPage: true })

    await page.goto('/profile')
    await page.waitForSelector('[data-testid="profile-page"]')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/profile-ar.png`, fullPage: true })
  })

  test('captures Arabic onboarding in RTL for first-run users', async ({ page }) => {
    await seedAuth(page, {
      uid: 'u-rtl-onboarding',
      locale: 'ar',
      onboardedAt: null,
    })

    await page.goto('/home')
    await page.waitForURL(/\/onboarding/)
    await page.waitForSelector('[data-testid="onboarding-page"]')
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/onboarding-ar.png`, fullPage: true })
  })

  test('keeps Hebrew RTL enabled when the build flag exposes it', async ({ page }) => {
    test.skip(
      !['true', '1', 'yes'].includes((process.env.VITE_ENABLE_HEBREW ?? '').toLowerCase()),
      'Hebrew is disabled in this build',
    )

    await seedAuth(page, { uid: 'u-rtl-he', locale: 'he' })

    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await expect(page.locator('html')).toHaveAttribute('lang', 'he')
    await page.screenshot({ path: `${SCREENSHOT_DIR}/home-he.png`, fullPage: true })
  })
})
