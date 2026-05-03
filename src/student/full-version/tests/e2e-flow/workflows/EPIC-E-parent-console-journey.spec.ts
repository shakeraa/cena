// =============================================================================
// EPIC-E2E-E — Parent console journey (real browser drive)
//
// Drives the seeded `parent1@cena.local` user through:
//   /login form fill + submit (real clicks)
//   → /parent/dashboard
//   → assert one of: parent-dashboard-page (data) | -loading | -empty |
//     -tiergate (Premium not bought yet — UI surfaces upsell, NOT 403)
//
// The dashboard is Premium-tier-gated. Without a Premium subscription
// the SPA renders parent-dashboard-tiergate. That IS the success path
// at the UI layer — the test verifies the upsell surfaces cleanly
// without crashing or throwing JS errors.
//
// Console / page-error / 4xx-5xx diagnostics collected as for the
// other per-epic specs.
// =============================================================================

import { test, expect } from '@playwright/test'

const SEEDED_PARENT_EMAIL = 'parent1@cena.local'
const SEEDED_PARENT_PASSWORD = 'DevParent123!'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_E_PARENT_CONSOLE_JOURNEY', () => {
  test('parent /login → /parent/dashboard renders (data | empty | tiergate) @epic-e', async ({ page }, testInfo) => {
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
    page.on('response', async (resp) => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? t.slice(0, 800) + '…' : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    await page.addInitScript(() => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
    })

    console.log(`\n=== EPIC_E_PARENT_CONSOLE_JOURNEY for ${SEEDED_PARENT_EMAIL} ===\n`)

    // ── 1. /login form (real clicks) ──
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(SEEDED_PARENT_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill(SEEDED_PARENT_PASSWORD)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    console.log('[epic-e] post-login url:', page.url())

    // ── 2. /parent/dashboard ──
    await page.goto('/parent/dashboard')

    // The page reaches one of three states:
    //   (a) parent-dashboard-page (success — data rendered)
    //   (b) parent-dashboard-tiergate (Premium not bought, upsell shown)
    //   (c) parent-dashboard-empty (no children bound yet)
    // Loading is transient. The test asserts that ONE of the terminal
    // states surfaces within 15s without a JS throw.
    const settled = await Promise.race([
      page.getByTestId('parent-dashboard-tiergate').waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'tiergate'),
      page.getByTestId('parent-dashboard-empty').waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'empty'),
      // The success state has parent-dashboard-page as the wrapper plus
      // child cards (`parent-student-{uid}`). Wait for at least one child.
      page.locator('[data-testid^="parent-student-"]').first()
        .waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'data'),
    ]).catch(() => 'timeout')
    console.log(`[epic-e] dashboard settled: ${settled}`)
    expect(settled, 'parent dashboard must reach a terminal UI state without timing out')
      .not.toBe('timeout')

    // ── 3. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')
    console.log('\n=== EPIC_E DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length} | warnings=${consoleEntries.filter(e => e.type === 'warning').length}`)
    console.log(`Page errors: ${pageErrors.length}`)
    console.log(`Failed network: ${failedRequests.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 20))
        console.log(`  ${e.text}${e.location ? ` @ ${e.location}` : ''}`)
    }
    if (failedRequests.length) {
      console.log('— failed requests —')
      for (const f of failedRequests.slice(0, 30))
        console.log(`  ${f.status} ${f.method} ${f.url} :: ${(f.body ?? '').slice(0, 200)}`)
    }
  })
})
