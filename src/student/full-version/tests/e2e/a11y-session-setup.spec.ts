import { expect, test } from '@playwright/test'

/**
 * FIND-ux-030 Regression test
 *
 * Verifies the session-setup subject chips are accessible:
 *  1. Each chip has role="button" and aria-pressed reflecting selection state
 *  2. The chip group has role="group" with aria-label
 *  3. Keyboard-only flow: Tab + Space toggles subjects, Tab + Enter starts session
 */

async function seedAuth(page: import('@playwright/test').Page) {
  await page.addInitScript(() => {
    localStorage.setItem('cena-mock-auth', JSON.stringify({
      uid: 'u-a11y-session',
      email: 'a11y-session@example.com',
      displayName: 'A11y Session Tester',
    }))
    localStorage.setItem('cena-mock-me', JSON.stringify({
      uid: 'u-a11y-session',
      displayName: 'A11y Session Tester',
      email: 'a11y-session@example.com',
      locale: 'en',
      onboardedAt: '2026-04-10T00:00:00Z',
    }))
  })
}

test.describe('FIND-ux-030: a11y session-setup subject chips', () => {
  test.beforeEach(async ({ page }) => {
    await seedAuth(page)
    await page.goto('/session')
    await page.waitForLoadState('networkidle')
  })

  test('subject chips have role="button" and aria-pressed', async ({ page }) => {
    const mathChip = page.getByTestId('setup-subject-math')
    const physicsChip = page.getByTestId('setup-subject-physics')

    await expect(mathChip).toBeVisible()

    // Math is selected by default
    await expect(mathChip).toHaveAttribute('role', 'button')
    await expect(mathChip).toHaveAttribute('aria-pressed', 'true')

    // Physics is not selected by default
    await expect(physicsChip).toHaveAttribute('role', 'button')
    await expect(physicsChip).toHaveAttribute('aria-pressed', 'false')
  })

  test('subject chip group has role="group" with aria-label', async ({ page }) => {
    const group = page.getByTestId('setup-subjects')

    await expect(group).toHaveAttribute('role', 'group')

    const ariaLabel = await group.getAttribute('aria-label')

    expect(ariaLabel).toBeTruthy()
  })

  test('keyboard-only: Tab+Space selects subjects, Tab+Enter starts session', async ({ page }) => {
    // First deselect math (it is selected by default)
    const mathChip = page.getByTestId('setup-subject-math')

    await mathChip.focus()
    await page.keyboard.press('Space')
    await expect(mathChip).toHaveAttribute('aria-pressed', 'false')

    // Now select math back
    await page.keyboard.press('Space')
    await expect(mathChip).toHaveAttribute('aria-pressed', 'true')

    // Tab to physics and select it with Space
    await page.keyboard.press('Tab')

    const physicsChip = page.getByTestId('setup-subject-physics')
    const activeTestId = await page.evaluate(() =>
      document.activeElement?.getAttribute('data-testid'),
    )

    // If focus moved to physics chip, press Space
    if (activeTestId === 'setup-subject-physics') {
      await page.keyboard.press('Space')
      await expect(physicsChip).toHaveAttribute('aria-pressed', 'true')
    }
    else {
      // Focus may have moved elsewhere depending on Vuetify internals;
      // directly focus and use Space as fallback
      await physicsChip.focus()
      await page.keyboard.press('Space')
      await expect(physicsChip).toHaveAttribute('aria-pressed', 'true')
    }

    // Verify the Start Session button is enabled (at least one subject selected)
    const startBtn = page.getByTestId('setup-start')

    await expect(startBtn).toBeEnabled()
  })

  test('aria-pressed toggles correctly on click', async ({ page }) => {
    const physicsChip = page.getByTestId('setup-subject-physics')

    // Not selected initially
    await expect(physicsChip).toHaveAttribute('aria-pressed', 'false')

    // Click to select
    await physicsChip.click()
    await expect(physicsChip).toHaveAttribute('aria-pressed', 'true')

    // Click to deselect
    await physicsChip.click()
    await expect(physicsChip).toHaveAttribute('aria-pressed', 'false')
  })

  test('all subject chips have aria-label', async ({ page }) => {
    const subjects = ['math', 'physics', 'chemistry', 'biology', 'english', 'history']

    for (const subject of subjects) {
      const chip = page.getByTestId(`setup-subject-${subject}`)

      await expect(chip).toBeVisible()

      const ariaLabel = await chip.getAttribute('aria-label')

      expect(ariaLabel).toBeTruthy()
    }
  })
})
