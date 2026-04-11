import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw10'

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

test.describe.serial('STU-W-10 knowledge graph', () => {
  test('E2E #1 /knowledge-graph renders subject tabs and math concept tiles', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/knowledge-graph')
    await page.waitForSelector('[data-testid="knowledge-graph-page"]')

    // Subject tabs
    await expect(page.locator('[data-testid="subject-tab-math"]')).toBeVisible()
    await expect(page.locator('[data-testid="subject-tab-physics"]')).toBeVisible()
    await expect(page.locator('[data-testid="subject-tab-chemistry"]')).toBeVisible()
    await expect(page.locator('[data-testid="subject-tab-biology"]')).toBeVisible()

    // 7 math concepts from the catalog
    await expect(page.locator('[data-testid="concept-math-arith"]')).toBeVisible()
    await expect(page.locator('[data-testid="concept-math-calculus"]')).toBeVisible()

    // Subject summary
    await expect(page.locator('[data-testid="subject-summary"]')).toContainText('2 / 7')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/knowledge-math.png`, fullPage: true })
  })

  test('E2E #2 clicking physics tab re-fetches concepts for that subject', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/knowledge-graph')
    await page.waitForSelector('[data-testid="subject-tab-physics"]')

    await page.locator('[data-testid="subject-tab-physics"]').click()

    // Wait for physics concepts to appear
    await expect(page.locator('[data-testid="concept-physics-kinematics"]')).toBeVisible()
    await expect(page.locator('[data-testid="concept-math-arith"]')).toHaveCount(0)
  })

  test('E2E #3 clicking an available concept opens the detail page', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/knowledge-graph')
    await page.waitForSelector('[data-testid="concept-math-quadratics"]')

    await page.locator('[data-testid="concept-math-quadratics"]').click()

    await page.waitForURL(url => new URL(url).pathname === '/knowledge-graph/concept/math-quadratics')
    await page.waitForSelector('[data-testid="concept-page"]')
    await expect(page.locator('[data-testid="concept-name"]')).toContainText('Quadratic')
  })

  test('E2E #4 concept detail page shows prerequisites + dependencies chains', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/knowledge-graph/concept/math-algebra')
    await page.waitForSelector('[data-testid="concept-page"]')

    // Linear Algebra has one prereq (math-fractions) and two deps (math-quadratics, math-geometry)
    await expect(page.locator('[data-testid="chain-link-math-fractions"]')).toBeVisible()
    await expect(page.locator('[data-testid="chain-link-math-quadratics"]')).toBeVisible()
    await expect(page.locator('[data-testid="chain-link-math-geometry"]')).toBeVisible()

    // Start session CTA is visible (in-progress status)
    await expect(page.locator('[data-testid="concept-start-session"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/concept-detail.png`, fullPage: true })
  })

  test('E2E #5 concept without prereqs shows empty state', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/knowledge-graph/concept/math-arith')
    await page.waitForSelector('[data-testid="concept-page"]')

    // Math arith has no prerequisites
    const prereqChain = page.locator('[data-testid="chain-prerequisites"]')

    await expect(prereqChain).toBeVisible()
    await expect(prereqChain.locator('[data-testid="chain-empty"]')).toBeVisible()
  })
})
