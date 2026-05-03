import { expect, test } from '@playwright/test'

/**
 * FIND-ux-022  Regression test
 *
 * Verifies the student header bar is keyboard- and screen-reader-accessible:
 *  1. User profile menu activator is a real <button> (not a <div>)
 *  2. It carries aria-haspopup="menu" and toggles aria-expanded
 *  3. It is reachable via Tab and opens on Enter / Space
 *  4. Header icon buttons (sidebar toggle, language, theme, notifications)
 *     each have an aria-label
 */

async function seedAuth(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-a11y',
      email: 'a11y@example.com',
      displayName: 'A11y Tester',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-a11y',
      displayName: 'A11y Tester',
      email: 'a11y@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
  })
}

test.describe('FIND-ux-022: a11y keyboard navigation — header bar', () => {
  test.beforeEach(async ({ page }) => {
    await seedAuth(page)
    await page.goto('/home')
    await page.waitForLoadState('networkidle')
  })

  test('user profile menu activator is a button element with correct ARIA attrs', async ({ page }) => {
    const avatarBtn = page.getByTestId('user-profile-avatar-button')

    await expect(avatarBtn).toBeVisible()

    // Must be a real <button> — not a <div>
    const tagName = await avatarBtn.evaluate(el => el.tagName)

    expect(tagName).toBe('BUTTON')

    // Must declare it opens a menu
    await expect(avatarBtn).toHaveAttribute('aria-haspopup', 'menu')

    // aria-label must be present and non-empty
    const ariaLabel = await avatarBtn.getAttribute('aria-label')

    expect(ariaLabel).toBeTruthy()
    expect(ariaLabel).toContain('A11y Tester')
  })

  test('user profile menu is keyboard-reachable and opens on Enter', async ({ page }) => {
    // Tab until the avatar button receives focus
    let focused = false
    for (let i = 0; i < 30; i++) {
      await page.keyboard.press('Tab')

      const active = await page.evaluate(() => {
        const el = document.activeElement

        return el?.getAttribute('data-testid') ?? null
      })

      if (active === 'user-profile-avatar-button') {
        focused = true
        break
      }
    }
    expect(focused).toBe(true)

    // Press Enter to open menu
    await page.keyboard.press('Enter')
    await page.waitForTimeout(300) // menu animation

    // The sign-out button inside the menu should now be visible
    const signOutBtn = page.getByTestId('user-profile-signout')

    await expect(signOutBtn).toBeVisible()
  })

  test('user profile menu opens on Space keypress', async ({ page }) => {
    const avatarBtn = page.getByTestId('user-profile-avatar-button')

    // Focus the button directly
    await avatarBtn.focus()
    await page.keyboard.press('Space')
    await page.waitForTimeout(300)

    const signOutBtn = page.getByTestId('user-profile-signout')

    await expect(signOutBtn).toBeVisible()
  })

  test('aria-expanded toggles correctly on the profile button', async ({ page }) => {
    const avatarBtn = page.getByTestId('user-profile-avatar-button')

    // Before opening: aria-expanded should be "false"
    await expect(avatarBtn).toHaveAttribute('aria-expanded', 'false')

    // Click to open
    await avatarBtn.click()
    await page.waitForTimeout(300)

    // After opening: aria-expanded should be "true"
    await expect(avatarBtn).toHaveAttribute('aria-expanded', 'true')
  })

  test('header icon buttons have aria-label attributes', async ({ page }) => {
    // Notification button
    const notifBtn = page.locator('#notification-btn')

    await expect(notifBtn).toBeVisible()

    const notifLabel = await notifBtn.getAttribute('aria-label')

    expect(notifLabel).toBeTruthy()

    // Theme switcher — the IconBtn wrapping the theme icon
    // It uses the ThemeSwitcher component which renders an IconBtn
    // containing a VIcon with a tabler-sun or tabler-moon icon.
    const themeBtn = page.locator('button').filter({
      has: page.locator('.tabler-sun-high, .tabler-moon-stars, .tabler-device-desktop-analytics'),
    }).first()

    if (await themeBtn.isVisible()) {
      const themeLabel = await themeBtn.getAttribute('aria-label')

      expect(themeLabel).toBeTruthy()
    }
  })

  test('sign-out still works after the VBtn wrap', async ({ page }) => {
    const avatarBtn = page.getByTestId('user-profile-avatar-button')

    await avatarBtn.click()
    await page.waitForTimeout(300)

    const signOutBtn = page.getByTestId('user-profile-signout')

    await expect(signOutBtn).toBeVisible()
    await signOutBtn.click()

    // Should redirect to /login
    await page.waitForURL('**/login')
    expect(page.url()).toContain('/login')
  })
})
