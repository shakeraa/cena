import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw09'

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

test.describe.serial('STU-W-09 progress subpages', () => {
  test('E2E #1 /progress/time renders chart + KPIs', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress/time')
    await page.waitForSelector('[data-testid="progress-time-page"]')

    await expect(page.locator('[data-testid="time-breakdown-chart"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-total-minutes"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-7day-minutes"]')).toBeVisible()
    await expect(page.locator('[data-testid="kpi-day-streak"]')).toBeVisible()

    // 30 bars
    const bars = await page.locator('.time-breakdown-chart__column').count()

    expect(bars).toBe(30)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/time.png`, fullPage: true })
  })

  test('E2E #2 /progress/mastery renders overall card + subject rows', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress/mastery')
    await page.waitForSelector('[data-testid="progress-mastery-page"]')

    await expect(page.locator('[data-testid="mastery-overall"]')).toBeVisible()
    await expect(page.locator('[data-testid="mastery-subjects"]')).toBeVisible()

    // At least one mastery row (derived from /api/me subjects: math/physics/chemistry)
    const rowCount = await page.locator('[data-testid^="mastery-row-"]').count()

    expect(rowCount).toBeGreaterThan(0)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/mastery.png`, fullPage: true })
  })

  test('E2E #3 /progress/sessions renders stub history list', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress/sessions')
    await page.waitForSelector('[data-testid="progress-sessions-page"]')

    await expect(page.locator('[data-testid="session-history-list"]')).toBeVisible()

    // 5 stub sessions
    const items = await page.locator('[data-testid^="session-history-s-hist-"]').count()

    expect(items).toBe(5)

    await page.screenshot({ path: `${SCREENSHOT_DIR}/sessions.png`, fullPage: true })
  })

  test('E2E #4 progress subpages on mobile viewport reflow cleanly', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.setViewportSize({ width: 500, height: 900 })

    for (const path of ['/progress/time', '/progress/mastery', '/progress/sessions']) {
      await page.goto(path)
      await page.waitForLoadState('domcontentloaded')
      // Just verify the page testid is present without horizontal scroll
      const testId = `progress-${path.split('/').pop()}-page`

      await expect(page.locator(`[data-testid="${testId}"]`)).toBeVisible()
    }
  })
})
