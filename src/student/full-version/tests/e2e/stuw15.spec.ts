import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/stuw15'

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

test.describe.serial('STU-W-15 Phase A web enhancements', () => {
  test('E2E #1 pressing ? opens the keyboard shortcut cheatsheet', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    await page.keyboard.press('?')

    await expect(page.locator('[data-testid="shortcut-cheatsheet"]')).toBeVisible()
    await expect(page.locator('[data-testid="cheatsheet-group-global"]')).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/cheatsheet.png` })
  })

  test('E2E #2 pressing Cmd+K opens the command palette', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    await page.keyboard.press('Meta+k')

    await expect(page.locator('[data-testid="command-palette"]')).toBeVisible()
    await expect(page.locator('[data-testid="command-palette-input"]')).toBeFocused()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/palette.png` })
  })

  test('E2E #3 typing in the palette filters commands', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    await page.keyboard.press('Meta+k')
    await page.waitForSelector('[data-testid="command-palette-input"]')
    await page.locator('[data-testid="command-palette-input"]').fill('tutor')

    await expect(page.locator('[data-testid="command-nav.tutor"]')).toBeVisible()
    // Only matching commands visible
    await expect(page.locator('[data-testid="command-nav.home"]')).toHaveCount(0)
  })

  test('E2E #4 pressing Enter runs the selected command and navigates', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    await page.keyboard.press('Meta+k')
    await page.locator('[data-testid="command-palette-input"]').fill('knowledge')
    await page.keyboard.press('Enter')

    await page.waitForURL(url => new URL(url).pathname === '/knowledge-graph')
  })

  test('E2E #5 g h sequence navigates to /home', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/progress')
    await page.waitForSelector('[data-testid="progress-page"]')

    await page.keyboard.press('g')
    await page.keyboard.press('h')

    await page.waitForURL(url => new URL(url).pathname === '/home')
  })

  test('E2E #6 Escape closes an open palette', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')
    await page.waitForSelector('[data-testid="home-page"]')

    await page.keyboard.press('Meta+k')
    await expect(page.locator('[data-testid="command-palette"]')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.locator('[data-testid="command-palette"]')).not.toBeVisible()
  })

  test('E2E #7 manifest is linked from index.html', async ({ page }) => {
    await seedAuthedOnboarded(page)
    await page.goto('/home')

    const manifestHref = await page.locator('link[rel="manifest"]').getAttribute('href')

    expect(manifestHref).toBe('/manifest.webmanifest')

    // Verify the manifest endpoint actually serves valid JSON
    const response = await page.request.get('/manifest.webmanifest')

    expect(response.ok()).toBe(true)
    const manifest = await response.json()

    expect(manifest.name).toBe('Cena Student')
    expect(manifest.start_url).toBe('/home')
  })

  test('E2E #8 offline.html fallback page is servable', async ({ page }) => {
    await seedAuthedOnboarded(page)

    const response = await page.request.get('/offline.html')

    expect(response.ok()).toBe(true)
    const body = await response.text()

    expect(body).toContain('offline')
  })
})
