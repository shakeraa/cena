import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw04c'

async function seedAuthedNotOnboarded(page: import('@playwright/test').Page) {
  // addInitScript runs on every navigation, so it seeds auth/me but does
  // NOT wipe the onboarding state — that would break mid-wizard reload tests.
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-new',
      email: 'u-new@example.com',
      displayName: 'New Learner',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-new',
      displayName: 'New Learner',
      email: 'u-new@example.com',
      locale: 'en',
      onboardedAt: null,
    }))
  })
}

async function clearOnboardingState(page: import('@playwright/test').Page) {
  // One-shot wipe of any persisted wizard progress. Must run AFTER the
  // page has navigated somewhere so that localStorage is accessible.
  await page.goto('/')
  await page.evaluate(() => localStorage.removeItem('cena-onboarding-state'))
}

test.describe.serial('STU-W-04C onboarding wizard Phase A', () => {
  test('E2E #1 full welcome → role → language → confirm → /home', async ({ page }) => {
    await seedAuthedNotOnboarded(page)
    await clearOnboardingState(page)
    await page.goto('/onboarding')
    await page.waitForSelector('[data-testid="onboarding-page"]')

    // Welcome step
    await expect(page.locator('[data-testid="onboarding-step-welcome"]')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/welcome.png` })

    await page.locator('[data-testid="onboarding-start"]').click()

    // Role step
    await expect(page.locator('[data-testid="onboarding-step-role"]')).toBeVisible()

    // Next is disabled until a role is picked
    await expect(page.locator('[data-testid="onboarding-next"]')).toBeDisabled()
    await page.locator('[data-testid="role-self-learner"]').click()
    await expect(page.locator('[data-testid="onboarding-next"]')).toBeEnabled()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/role.png` })
    await page.locator('[data-testid="onboarding-next"]').click()

    // Language step
    await expect(page.locator('[data-testid="onboarding-step-language"]')).toBeVisible()
    await page.locator('[data-testid="locale-en"]').click()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/language.png` })
    await page.locator('[data-testid="onboarding-next"]').click()

    // Confirm step
    await expect(page.locator('[data-testid="onboarding-step-confirm"]')).toBeVisible()
    await expect(page.locator('[data-testid="confirm-role"]')).toContainText(/Self-learner/i)
    await expect(page.locator('[data-testid="confirm-language"]')).toContainText(/English/i)
    await page.screenshot({ path: `${SCREENSHOT_DIR}/confirm.png` })
    await page.locator('[data-testid="onboarding-submit"]').click()

    // Lands at /home
    await page.waitForURL(url => new URL(url).pathname === '/home')
    expect(new URL(page.url()).pathname).toBe('/home')
  })

  test('E2E #2 refresh mid-wizard resumes at the persisted step', async ({ page }) => {
    await seedAuthedNotOnboarded(page)
    await clearOnboardingState(page)
    await page.goto('/onboarding')
    await page.waitForSelector('[data-testid="onboarding-step-welcome"]')
    await page.locator('[data-testid="onboarding-start"]').click()
    await page.locator('[data-testid="role-homeschool"]').click()
    await page.locator('[data-testid="onboarding-next"]').click()

    // We should be on language step now
    await expect(page.locator('[data-testid="onboarding-step-language"]')).toBeVisible()

    // Reload — should resume on language step
    await page.reload()
    await page.waitForSelector('[data-testid="onboarding-page"]')
    await expect(page.locator('[data-testid="onboarding-step-language"]')).toBeVisible()
  })

  test('E2E #3 selecting Hebrew flips html[dir] to rtl immediately', async ({ page }) => {
    await seedAuthedNotOnboarded(page)
    await clearOnboardingState(page)
    await page.goto('/onboarding')
    await page.waitForSelector('[data-testid="onboarding-step-welcome"]')
    await page.locator('[data-testid="onboarding-start"]').click()
    await page.locator('[data-testid="role-student"]').click()
    await page.locator('[data-testid="onboarding-next"]').click()

    // Starts ltr
    expect(await page.locator('html').getAttribute('dir')).toBe('ltr')

    await page.locator('[data-testid="locale-he"]').click()

    // Flips to rtl
    await expect.poll(async () => await page.locator('html').getAttribute('dir')).toBe('rtl')
  })

  test('E2E #4 back button returns to previous step', async ({ page }) => {
    await seedAuthedNotOnboarded(page)
    await clearOnboardingState(page)
    await page.goto('/onboarding')
    await page.waitForSelector('[data-testid="onboarding-step-welcome"]')
    await page.locator('[data-testid="onboarding-start"]').click()

    await expect(page.locator('[data-testid="onboarding-step-role"]')).toBeVisible()
    await page.locator('[data-testid="role-test-prep"]').click()
    await page.locator('[data-testid="onboarding-next"]').click()
    await expect(page.locator('[data-testid="onboarding-step-language"]')).toBeVisible()

    // Back to role step
    await page.locator('[data-testid="onboarding-back"]').click()
    await expect(page.locator('[data-testid="onboarding-step-role"]')).toBeVisible()
  })
})
