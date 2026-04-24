// =============================================================================
// PRR-A11Y-PLAYWRIGHT-RATCHET — cross-layout smoke spec for A11yToolbar +
// FirstRunLanguageChooser.
//
// Coverage:
//   - Toolbar handle is present on every layout (default, auth, blank).
//   - Language radio flips html[dir] RTL/LTR correctly.
//   - Text-size slider value survives a full route navigation.
//   - data-a11y-* attributes on <html> reflect store state after any change.
//   - First-run chooser blocks route content when `cena-student-locale` is
//     absent, does NOT re-appear after a lock commit.
// =============================================================================
import { expect, test, type Page } from '@playwright/test'

async function seedLockedLocale(page: Page, code: 'en' | 'ar' | 'he' = 'en') {
  await page.addInitScript((c) => {
    // Match the PRR-A11Y-FIRST-RUN-CHOOSER v1 schema so the chooser does not
    // block the page for these ratchet tests.
    localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: c, locked: true, version: 1 }),
    )
  }, code)
}

async function seedAuth(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-a11y-ratchet',
      email: 'ratchet@example.com',
      displayName: 'A11y Ratchet',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-a11y-ratchet',
      displayName: 'A11y Ratchet',
      email: 'ratchet@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
  })
}

test.describe('A11yToolbar cross-layout ratchet', () => {
  test('handle is present on default layout (authenticated /home)', async ({ page }) => {
    await seedLockedLocale(page)
    await seedAuth(page)
    await page.goto('/home')
    await page.waitForLoadState('networkidle')

    const handle = page.getByTestId('a11y-toolbar-handle')

    await expect(handle).toBeVisible()
  })

  test('handle is present on auth layout (/login)', async ({ page }) => {
    await seedLockedLocale(page)
    await page.goto('/login')
    await page.waitForLoadState('networkidle')

    const handle = page.getByTestId('a11y-toolbar-handle')

    await expect(handle).toBeVisible()
  })

  test('handle is present on blank layout (/onboarding)', async ({ page }) => {
    await seedLockedLocale(page)
    await seedAuth(page)
    await page.goto('/onboarding')
    await page.waitForLoadState('networkidle')

    const handle = page.getByTestId('a11y-toolbar-handle')

    await expect(handle).toBeVisible()
  })

  test('language radio flips html[dir] to RTL when AR selected', async ({ page }) => {
    await seedLockedLocale(page)
    await seedAuth(page)
    await page.goto('/home')
    await page.waitForLoadState('networkidle')

    await page.getByTestId('a11y-toolbar-handle').click()
    const arRadio = page.locator('[data-testid="a11y-language-ar"] input[type="radio"]')

    await arRadio.check()

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar')
  })

  test('data-a11y-* attributes reflect store state after toggle', async ({ page }) => {
    await seedLockedLocale(page)
    await seedAuth(page)
    await page.goto('/home')
    await page.waitForLoadState('networkidle')

    await page.getByTestId('a11y-toolbar-handle').click()

    // Flip high-contrast via the switch wrapper (Vuetify).
    await page.getByTestId('a11y-contrast-toggle').click()

    await expect(page.locator('html')).toHaveAttribute('data-a11y-contrast', 'high')
  })

  test('text-size persists across a route navigation', async ({ page }) => {
    await seedLockedLocale(page)
    await seedAuth(page)
    await page.goto('/home')
    await page.waitForLoadState('networkidle')

    await page.getByTestId('a11y-toolbar-handle').click()
    await page.getByTestId('a11y-text-larger').click()
    const sizeAfterBump = await page.locator('html').getAttribute('data-a11y-text-size')

    expect(sizeAfterBump).not.toBe('1')

    // Full route navigation.
    await page.goto('/progress')
    await page.waitForLoadState('networkidle')

    await expect(page.locator('html')).toHaveAttribute('data-a11y-text-size', sizeAfterBump!)
  })
})

test.describe('FirstRunLanguageChooser mount conditions', () => {
  test('chooser blocks on very first visit (no cena-student-locale key)', async ({ page }) => {
    await page.goto('/login')
    await page.waitForLoadState('domcontentloaded')

    const chooser = page.getByTestId('first-run-chooser')

    await expect(chooser).toBeVisible()
    await expect(page.getByTestId('first-run-tile-en')).toBeVisible()
    await expect(page.getByTestId('first-run-tile-ar')).toBeVisible()
  })

  test('chooser does NOT appear after locale is locked', async ({ page }) => {
    await seedLockedLocale(page, 'en')
    await page.goto('/login')
    await page.waitForLoadState('networkidle')

    const chooser = page.getByTestId('first-run-chooser')

    await expect(chooser).toHaveCount(0)
  })

  test('clicking a tile commits and the chooser unmounts', async ({ page }) => {
    await page.goto('/login')
    await page.waitForLoadState('domcontentloaded')

    await page.getByTestId('first-run-tile-en').click()

    await expect(page.getByTestId('first-run-chooser')).toHaveCount(0)
    // localStorage now carries the v1 locked schema.
    const stored = await page.evaluate(() => localStorage.getItem('cena-student-locale'))

    expect(JSON.parse(stored!)).toMatchObject({ code: 'en', locked: true, version: 1 })
  })
})
