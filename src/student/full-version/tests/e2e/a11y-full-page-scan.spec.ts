// =============================================================================
// RDY-030: Systematic axe-core scan across all major student pages.
//
// Runs WCAG 2.1 AA rules via @axe-core/playwright against every top-level
// route. Fails on any serious/critical violation (regression guard).
// Baseline: captured via snapshot on first run; later runs must stay below.
// =============================================================================

import AxeBuilder from '@axe-core/playwright'
import type { Page } from '@playwright/test'
import { expect, test } from '@playwright/test'

/** Pages scanned. Each route is loaded, fully hydrated, then axe-analyzed. */
const PAGES = [
  { route: '/home',       name: 'home' },
  { route: '/session',    name: 'session' },
  { route: '/progress',   name: 'progress' },
  { route: '/profile',    name: 'profile' },
  { route: '/onboarding', name: 'onboarding' },
] as const

/** Mock auth so authenticated pages render real content. */
async function seedAuth(page: Page) {
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

/**
 * Run axe with WCAG 2.1 AA rule set, excluding Vuetify portals and dev chrome
 * that are not part of the shipped UI surface.
 */
async function runAxe(page: Page) {
  return new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .exclude('.v-overlay-container')
    .exclude('.vue-devtools__anchor-btn')
    .exclude('[data-v-devtools]')
    .analyze()
}

test.describe('RDY-030: full-page axe scan', () => {
  test.beforeEach(async ({ page }) => {
    await seedAuth(page)
  })

  for (const { route, name } of PAGES) {
    test(`${name} has no serious/critical WCAG 2.1 AA violations`, async ({ page }) => {
      const response = await page.goto(route)
      expect(response?.ok(), `page ${route} should load`).toBe(true)

      await page.waitForLoadState('networkidle')

      const results = await runAxe(page)

      // Fail only on serious/critical — minor/moderate are warnings (baseline may
      // still contain them). Zero-tolerance policy applies to new violations only.
      const blocking = results.violations.filter(
        v => v.impact === 'serious' || v.impact === 'critical',
      )

      if (blocking.length > 0) {
        // Include rule id + affected nodes in the failure message so the CI log
        // points directly at the offending selector.
        const summary = blocking.map(v => ({
          rule: v.id,
          impact: v.impact,
          help: v.help,
          nodes: v.nodes.map(n => n.target).slice(0, 3), // first 3 selectors
        }))
        console.error(`[${name}] axe violations:`, JSON.stringify(summary, null, 2))
      }

      expect(
        blocking.length,
        `${name} should have 0 serious/critical WCAG 2.1 AA violations`,
      ).toBe(0)
    })
  }
})
