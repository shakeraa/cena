import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

const SCREENSHOT_DIR = 'test-results/stuw07'

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

test.describe.serial('STU-W-07 gamification progress page', () => {
  test('E2E #1 /progress renders xp + streak + badges + leaderboard from real /api/gamification/*', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress')
    await page.waitForSelector('[data-testid="progress-page"]')

    // XP card wired to /api/gamification/xp (mockXp.currentLevel = 7)
    await expect(page.locator('[data-testid="xp-progress-card"]')).toBeVisible()
    await expect(page.locator('[data-testid="xp-current-level"]')).toHaveText('7')
    await expect(page.locator('[data-testid="xp-current-xp"]')).toContainText('180')

    // Streak reused from home (mockStreak.currentDays = 12)
    await expect(page.locator('[data-testid="streak-widget"]')).toBeVisible()
    await expect(page.locator('[data-testid="streak-widget"]')).toContainText('12')

    // Badges — 3 earned + 5 locked
    await expect(page.locator('[data-testid="badge-grid"]')).toBeVisible()
    await expect(page.locator('[data-testid="badge-counter"]')).toHaveText('3 of 8')
    await expect(page.locator('[data-testid="badge-earned-first-steps"]')).toBeVisible()
    await expect(page.locator('[data-testid="badge-earned-quiz-master"]')).toBeVisible()
    await expect(page.locator('[data-testid="badge-locked-perfectionist"]')).toBeVisible()

    // Leaderboard — current student at rank 5
    await expect(page.locator('[data-testid="leaderboard-preview"]')).toBeVisible()
    await expect(page.locator('[data-testid="leaderboard-your-rank"]')).toContainText('5')
    await expect(page.locator('[data-testid="leaderboard-entry-5"]')).toContainText('Dev Student')
    await expect(page.locator('[data-testid="leaderboard-entry-5"]')).toContainText('You')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/progress-desktop.png`, fullPage: true })
  })

  test('E2E #2 leaderboard limit shows only top 5', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress')
    await page.waitForSelector('[data-testid="leaderboard-preview"]')

    const entries = await page.locator('[data-testid^="leaderboard-entry-"]').count()

    expect(entries).toBe(5)
  })

  test('E2E #3 /progress on mobile viewport renders without overflow', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.setViewportSize({ width: 500, height: 900 })
    await page.goto('/progress')
    await page.waitForSelector('[data-testid="progress-page"]')

    await expect(page.locator('[data-testid="xp-progress-card"]')).toBeVisible()
    await expect(page.locator('[data-testid="badge-grid"]')).toBeVisible()
    await page.screenshot({ path: `${SCREENSHOT_DIR}/progress-mobile.png`, fullPage: true })
  })

  test('E2E #4 /progress content passes axe in light mode', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress')
    await page.waitForSelector('[data-testid="progress-page"]')

    // Scope to the /progress page content only — the Vuexy navbar/sidebar
    // chrome has known pre-existing aria-allowed-attr + button-name issues
    // that belong to layout cleanup (STU-A11Y-LAYOUT), not to STU-W-07.
    const results = await new AxeBuilder({ page })
      .include('[data-testid="progress-page"]')
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
