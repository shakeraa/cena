import { expect, test } from '@playwright/test'

const SCREENSHOT_DIR = 'test-results/worked-examples'

/**
 * RDY-013: Worked Examples UI Rendering — E2E tests
 *
 * These tests validate:
 *  - Worked example renders at Full scaffolding with step data
 *  - Progressive step reveal via "Next step" button
 *  - Faded example renders at Partial scaffolding
 *  - No worked example at HintsOnly/None scaffolding
 *  - Accessibility: aria-label, aria-live, aria-current, aria-disabled
 *  - Math expressions are LTR-wrapped
 *
 * Data setup: Tests rely on MSW mock handlers injecting workedExample
 * data into SessionQuestionDto responses. The mock server must be
 * configured before these tests run (see msw/handlers.ts).
 */

test.describe('RDY-013 Worked Examples', () => {
  test.beforeEach(async ({ page }) => {
    // Inject mock auth so session pages are accessible
    await page.addInitScript(() => {
      localStorage.setItem('cena-mock-auth', JSON.stringify({
        uid: 'test-user',
        email: 'student@test.com',
        token: 'mock-jwt',
      }))
    })
  })

  test('Full scaffolding: renders worked example panel with steps', async ({ page }) => {
    // Mock a question with Full scaffolding and structured worked example
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_001',
          questionIndex: 0,
          totalQuestions: 5,
          prompt: 'Calculate 12 × 8',
          questionType: 'multiple-choice',
          choices: ['86', '96', '106', '116'],
          subject: 'Math',
          expectedTimeSeconds: 60,
          scaffoldingLevel: 'Full',
          workedExample: {
            steps: [
              { description: 'Break down the multiplication', math: '12 \\times 8 = (10 + 2) \\times 8', explanation: 'Use the distributive property' },
              { description: 'Multiply each part', math: '10 \\times 8 + 2 \\times 8', explanation: 'Distribute the 8' },
              { description: 'Calculate', math: '80 + 16 = 96', explanation: 'Add the partial products' },
            ],
          },
          hintsAvailable: 3,
          hintsRemaining: 3,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="question-card"]')

    // Panel should be visible
    const panel = page.locator('[data-testid="worked-example-panel"]')
    await expect(panel).toBeVisible()

    // First step should be visible
    const steps = panel.locator('[data-testid="worked-example-step"]')
    await expect(steps.first()).toBeVisible()

    // Should have aria-label on first step
    await expect(steps.first()).toHaveAttribute('aria-label', /Step 1:/)

    // Active step should have aria-current="step"
    await expect(steps.first()).toHaveAttribute('aria-current', 'step')

    // "Next step" button should be visible (not all steps revealed yet)
    const nextBtn = page.locator('[data-testid="next-step-btn"]')
    await expect(nextBtn).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/full-step-1.png` })

    // Click "Next step" to reveal step 2
    await nextBtn.click()
    await expect(steps.nth(1)).toBeVisible()

    // Step announcement should fire (aria-live region)
    const announcement = page.locator('[data-testid="step-announcement"]')
    await expect(announcement).toContainText('Step 2 of 3')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/full-step-2.png` })

    // Click again to reveal step 3
    await nextBtn.click()

    // "Next step" button should disappear (all steps revealed)
    await expect(nextBtn).not.toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/full-all-steps.png` })
  })

  test('Partial scaffolding: renders faded example with input fields', async ({ page }) => {
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_002',
          questionIndex: 1,
          totalQuestions: 5,
          prompt: 'Solve: 3x + 6 = 15',
          questionType: 'short-answer',
          choices: [],
          subject: 'Algebra',
          expectedTimeSeconds: 90,
          scaffoldingLevel: 'Partial',
          workedExample: {
            steps: [
              { description: 'Subtract 6 from both sides', math: '3x = 9', explanation: 'Isolate the variable term' },
              { description: 'Divide both sides by 3', math: 'x = 3', explanation: 'Solve for x' },
            ],
          },
          hintsAvailable: 2,
          hintsRemaining: 2,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="question-card"]')

    const panel = page.locator('[data-testid="worked-example-panel"]')
    await expect(panel).toBeVisible()

    // Faded steps should have inputs
    const fadedInputs = panel.locator('[data-testid="faded-step-input"]')
    // At least one faded step input should exist
    const inputCount = await fadedInputs.count()
    expect(inputCount).toBeGreaterThan(0)

    // Faded steps should have aria-disabled="true"
    const steps = panel.locator('[data-testid="worked-example-step"]')
    const lastStep = steps.last()
    await expect(lastStep).toHaveAttribute('aria-disabled', 'true')

    // Faded steps should NOT be hidden from accessibility tree (opacity, not display:none)
    await expect(lastStep).toHaveAttribute('aria-hidden', 'false')

    await page.screenshot({ path: `${SCREENSHOT_DIR}/partial-faded.png` })

    // Fill in a faded step and check
    await fadedInputs.first().fill('x = 3')
    await page.locator('[data-testid="faded-step-check"]').first().click()

    // Feedback should appear
    const feedback = page.locator('[data-testid="faded-step-feedback"]')
    await expect(feedback.first()).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/partial-feedback.png` })
  })

  test('HintsOnly scaffolding: no worked example shown', async ({ page }) => {
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_003',
          questionIndex: 2,
          totalQuestions: 5,
          prompt: 'What is 7 × 9?',
          questionType: 'multiple-choice',
          choices: ['56', '63', '72', '81'],
          subject: 'Math',
          expectedTimeSeconds: 30,
          scaffoldingLevel: 'HintsOnly',
          workedExample: null,
          hintsAvailable: 1,
          hintsRemaining: 1,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="question-card"]')

    await expect(page.locator('[data-testid="worked-example-panel"]')).not.toBeVisible()
    await expect(page.locator('[data-testid="question-worked-example"]')).not.toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/hints-only.png` })
  })

  test('None scaffolding: no worked example and no hint button', async ({ page }) => {
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_004',
          questionIndex: 3,
          totalQuestions: 5,
          prompt: 'Simplify: (x² - 4) / (x - 2)',
          questionType: 'short-answer',
          choices: [],
          subject: 'Algebra',
          expectedTimeSeconds: 60,
          scaffoldingLevel: 'None',
          workedExample: null,
          hintsAvailable: 0,
          hintsRemaining: 0,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="question-card"]')

    await expect(page.locator('[data-testid="worked-example-panel"]')).not.toBeVisible()
    await expect(page.locator('[data-testid="question-hint-request"]')).not.toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/none-scaffolding.png` })
  })

  test('Accessibility: math expressions wrapped in LTR bdi', async ({ page }) => {
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_005',
          questionIndex: 0,
          totalQuestions: 1,
          prompt: 'Calculate',
          questionType: 'multiple-choice',
          choices: ['1', '2'],
          subject: 'Math',
          expectedTimeSeconds: 30,
          scaffoldingLevel: 'Full',
          workedExample: {
            steps: [
              { description: 'Step one', math: '2 + 2 = 4' },
            ],
          },
          hintsAvailable: 1,
          hintsRemaining: 1,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="worked-example-panel"]')

    // Math content should be inside a bdi[dir="ltr"] element
    const mathBdi = page.locator('[data-testid="worked-example-panel"] bdi[dir="ltr"]')
    await expect(mathBdi.first()).toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/math-ltr.png` })
  })

  test('Keyboard navigation: "Next step" button is keyboard-accessible', async ({ page }) => {
    await page.route('**/api/sessions/*/question', route => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          questionId: 'q_we_006',
          questionIndex: 0,
          totalQuestions: 1,
          prompt: 'Test keyboard',
          questionType: 'multiple-choice',
          choices: ['a', 'b'],
          subject: 'Math',
          expectedTimeSeconds: 30,
          scaffoldingLevel: 'Full',
          workedExample: {
            steps: [
              { description: 'First', math: '1' },
              { description: 'Second', math: '2' },
            ],
          },
          hintsAvailable: 1,
          hintsRemaining: 1,
        }),
      })
    })

    await page.goto('/session/mock-session-id')
    await page.waitForSelector('[data-testid="next-step-btn"]')

    // Tab to the "Next step" button and press Enter
    const nextBtn = page.locator('[data-testid="next-step-btn"]')
    await nextBtn.focus()
    await page.keyboard.press('Enter')

    // Second step should now be visible
    const steps = page.locator('[data-testid="worked-example-step"]')
    await expect(steps.nth(1)).toBeVisible()

    // "Next step" button should be gone (only 2 steps)
    await expect(nextBtn).not.toBeVisible()

    await page.screenshot({ path: `${SCREENSHOT_DIR}/keyboard-nav.png` })
  })
})
