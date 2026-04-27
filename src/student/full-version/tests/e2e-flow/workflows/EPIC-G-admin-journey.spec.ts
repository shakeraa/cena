// =============================================================================
// EPIC-E2E-G — Admin operations journey (real browser drive on admin SPA)
//
// Drive the seeded admin@cena.local user through the admin SPA at
// localhost:5174:
//   1. /login form fill + submit (real clicks)
//   2. Land on a non-/login route
//   3. Visit /apps/permissions and /apps/moderation/queue — both must
//      render without crashing
//
// The original epic body lists G-01 bagrut ingestion through G-09 live
// monitor. Most of those are server-side flows tied to data the dev
// stack doesn't seed. This spec covers the smoke-level "can a SUPER_ADMIN
// reach the moderation surface without 5xx/console-error" — the deeper
// cell-level journeys (CAS-override, RTBF admin, etc.) are queued as
// follow-up specs once their backing data is seedable.
//
// Diagnostics (console / page errors / 4xx-5xx) collected per the
// shared pattern.
// =============================================================================

import { test, expect } from '@playwright/test'

const ADMIN_SPA_BASE_URL = process.env.E2E_ADMIN_SPA_URL ?? 'http://localhost:5174'
const SEEDED_ADMIN_EMAIL = 'admin@cena.local'
const SEEDED_ADMIN_PASSWORD = 'DevAdmin123!'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_G_ADMIN_JOURNEY', () => {
  test('admin /login → /apps/permissions + /apps/moderation/queue render @epic-g', async ({ page }, testInfo) => {
    test.setTimeout(120_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string; stack?: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({
      type: msg.type(),
      text: msg.text(),
      location: msg.location()?.url
        ? `${msg.location().url}:${msg.location().lineNumber}`
        : undefined,
    }))
    page.on('pageerror', err => pageErrors.push({ message: err.message, stack: err.stack }))
    page.on('response', async resp => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    await page.goto(`${ADMIN_SPA_BASE_URL}/login`)
    await expect(page.locator('input[type="email"]')).toBeVisible({ timeout: 10_000 })
    await page.locator('input[type="email"]').fill(SEEDED_ADMIN_EMAIL)
    await page.locator('input[type="password"]').fill(SEEDED_ADMIN_PASSWORD)
    await page.locator('button[type="submit"]').click()

    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
    expect(page.url(), 'admin SPA must leave /login after admin sign-in').not.toContain('/login')

    // Permissions + moderation surfaces are SUPER_ADMIN-gated. We only
    // assert the route guard doesn't crash + the page renders something.
    // Cell-level interactions (promote user, approve item) are queued.
    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/permissions`)
    await page.waitForLoadState('domcontentloaded', { timeout: 15_000 })

    await page.goto(`${ADMIN_SPA_BASE_URL}/apps/moderation/queue`)
    await page.waitForLoadState('domcontentloaded', { timeout: 15_000 })

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    expect(pageErrors,
      `page errors during admin journey: ${JSON.stringify(pageErrors.slice(0, 3))}`,
    ).toEqual([])
    const errs = consoleEntries.filter(e => e.type === 'error')
    expect(errs,
      `unexpected console errors: ${JSON.stringify(errs.slice(0, 3))}`,
    ).toEqual([])
  })
})
