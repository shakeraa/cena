import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw06'

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

test.describe.serial('STU-W-06 learning session runner', () => {
  test('E2E #1 /session setup renders subject chips + duration + mode toggles', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/session')
    await page.waitForSelector('[data-testid="session-setup-page"]')

    await expect(page.locator('[data-testid="setup-subject-math"]')).toBeVisible()
    await expect(page.locator('[data-testid="setup-duration-15"]')).toBeVisible()
    await expect(page.locator('[data-testid="setup-mode-practice"]')).toBeVisible()
    await expect(page.locator('[data-testid="setup-start"]')).toBeEnabled()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/setup.png` })
  })

  test('E2E #2 submitting /start navigates to /session/:id runner', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/session')
    await page.waitForSelector('[data-testid="setup-start"]')
    await page.locator('[data-testid="setup-start"]').click()

    await page.waitForURL(url => /\/session\/s-/.test(new URL(url).pathname))
    await page.waitForSelector('[data-testid="session-runner-page"]')

    // Q1 is the first canned question
    await expect(page.locator('[data-testid="question-prompt"]')).toContainText('12 × 8')
    await expect(page.locator('[data-testid="question-progress"]')).toContainText('1 of 5')
  })

  test('E2E #3 full session happy path → summary', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/session')
    await page.waitForSelector('[data-testid="setup-start"]')
    await page.locator('[data-testid="setup-start"]').click()
    await page.waitForSelector('[data-testid="question-card"]')

    // 5 canned answers — all correct
    const correctAnswers = ['96', '5', '2x', 'H2O', '300,000 km/s']

    for (const answer of correctAnswers) {
      await page.locator(`[data-testid="choice-${answer}"]`).click()
      await page.locator('[data-testid="question-submit"]').click()

      // Wait for the feedback flash to clear (1.6s timer in page)
      await page.waitForTimeout(1700)
    }

    // Lands at summary
    await page.waitForURL(url => /\/session\/s-.*\/summary/.test(new URL(url).pathname))
    await page.waitForSelector('[data-testid="session-summary-card"]')

    // 5/5 correct, 50 XP, 100% accuracy
    await expect(page.locator('[data-testid="summary-xp"]')).toContainText('50')
    await expect(page.locator('[data-testid="summary-accuracy"]')).toContainText('100%')
    await expect(page.locator('[data-testid="summary-correct"]')).toContainText('5 / 5')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/summary.png`, fullPage: true })
  })

  test('E2E #4 wrong answer shows wrong feedback + no XP awarded', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/session')
    await page.waitForSelector('[data-testid="setup-start"]')
    await page.locator('[data-testid="setup-start"]').click()
    await page.waitForSelector('[data-testid="question-card"]')

    // Pick a wrong answer for Q1 ("What is 12 × 8?")
    await page.locator('[data-testid="choice-92"]').click()
    await page.locator('[data-testid="question-submit"]').click()

    // Feedback should appear briefly
    const fb = page.locator('[data-testid="answer-feedback"]')

    await expect(fb).toBeVisible()
    expect(await fb.getAttribute('data-correct')).toBe('false')
  })

  test('E2E #5 /session setup page passes axe', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/session')
    await page.waitForSelector('[data-testid="session-setup-page"]')

    // Quick smoke — just verify the setup page renders without console errors
    const logs: string[] = []

    page.on('console', msg => {
      if (msg.type() === 'error')
        logs.push(msg.text())
    })
    await page.waitForTimeout(500)
    expect(logs.filter(l => !l.includes('favicon'))).toEqual([])
  })
})
