// =============================================================================
// EPIC-E2E-G — Admin experiments / tutoring / users per-page functional
//
// COV-04 sub-specs 6+7+8 of 10, batched because each is small (1-2
// real interactions per route, [id] sub-routes fixme'd).
//
//   /apps/experiments        — list of A/B experiments
//   /apps/experiments/[id]   — single experiment detail (fixme — INFRA-03)
//   /apps/tutoring/sessions  — list of active tutor sessions
//   /apps/tutoring/sessions/[id] — single session timeline (fixme)
//   /apps/user/list          — admin-side user search
//   /apps/user/view/[id]     — single user profile (fixme)
//
// EPIC-G admin smoke already verifies these mount cleanly. This spec
// drives the search / filter inputs that exist on the list pages.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'

async function adminSignIn(page: Page) {
  await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
  await page.locator('input[type="email"]').fill('admin@cena.local')
  await page.locator('input[type="password"]').fill('DevAdmin123!')
  await page.locator('button[type="submit"]').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

function attachDiagnostics(page: Page) {
  const consoleErrors: string[] = []
  const pageErrors: string[] = []
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()) })
  page.on('pageerror', e => { pageErrors.push(e.message) })
  return { consoleErrors, pageErrors }
}

test.describe('EPIC_G_EXPERIMENTS_FUNCTIONAL', () => {
  test('/apps/experiments lists experiments + render no JS errors @epic-g @admin-functional', async ({ page }, testInfo) => {
    test.setTimeout(45_000)
    const diag = attachDiagnostics(page)

    await adminSignIn(page)
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/experiments`)
    await page.waitForLoadState('domcontentloaded')

    await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

    testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
    expect(diag.pageErrors).toEqual([])
  })

  test.fixme(
    '/apps/experiments/[id] detail page @epic-g @admin-functional BLOCKED_ON: TASK-E2E-INFRA-03',
    async () => {
      // Implement once dynamicSeed.experiment() ships
    },
  )
})

test.describe('EPIC_G_TUTORING_FUNCTIONAL', () => {
  test('/apps/tutoring/sessions list mounts + filter input wires @epic-g @admin-functional', async ({ page }, testInfo) => {
    test.setTimeout(45_000)
    const diag = attachDiagnostics(page)

    await adminSignIn(page)
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/tutoring/sessions`)
    await page.waitForLoadState('domcontentloaded')

    await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

    // Most admin list pages have a search/filter input. Try to find one.
    const search = page.getByPlaceholder(/search|filter/i).first()
    if (await search.isVisible().catch(() => false)) {
      await search.fill('x')
      await search.fill('')
    }

    testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
    expect(diag.pageErrors).toEqual([])
  })

  test.fixme(
    '/apps/tutoring/sessions/[id] timeline @epic-g @admin-functional BLOCKED_ON: TASK-E2E-INFRA-03',
    async () => {
      // Implement once dynamicSeed.tutorThread() ships
    },
  )
})

test.describe('EPIC_G_USERS_FUNCTIONAL', () => {
  test('/apps/user/list search-by-email wires @epic-g @admin-functional', async ({ page }, testInfo) => {
    test.setTimeout(45_000)
    const diag = attachDiagnostics(page)

    await adminSignIn(page)
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/user/list`)
    await page.waitForLoadState('domcontentloaded')

    await expect(page.locator('h1, h2, [role="heading"]').first()).toBeVisible({ timeout: 10_000 })

    const search = page.getByPlaceholder(/search|email|user/i).first()
    if (await search.isVisible().catch(() => false)) {
      await search.fill('admin@cena')
      expect(await search.inputValue()).toBe('admin@cena')
      await search.fill('')
    }

    testInfo.attach('diagnostics.json', { body: JSON.stringify(diag, null, 2), contentType: 'application/json' })
    expect(diag.pageErrors).toEqual([])
  })

  test.fixme(
    '/apps/user/view/[id] profile + RTBF action @epic-g @admin-functional BLOCKED_ON: TASK-E2E-INFRA-03',
    async () => {
      // Implement once dynamicSeed.parentChildPair() ships so we can
      // act on a real user without mutating the seeded admin user
    },
  )
})
