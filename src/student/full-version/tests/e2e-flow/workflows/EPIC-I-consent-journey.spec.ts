// =============================================================================
// EPIC-E2E-I — GDPR / COPPA parental-consent journey (real browser drive)
//
// Drive the /register flow through the parental-consent path:
//   1. age-gate-dob: pick a DOB making the registrant 14 (teen tier)
//   2. age-gate-next click → SPA advances to step 'parental-consent'
//   3. parental-consent-step renders with parent-email + consent-checkbox
//   4. fill parent-email, check consent, click consent-next
//   5. credentials form renders for the under-13/teen user
//   6. fill credentials and submit — backend on-first-sign-in carries
//      the parent-email metadata through to the consent record
//
// This spec covers the FRONT half of the GDPR/COPPA flow — the back
// half (parent receives email, clicks verification link, child becomes
// fully active) lives in TASK-E2E-I-04 audit-export and is gated on
// the consent-email worker shipping. Documented as a gap in the
// REPORT.
//
// Diagnostics collected per the shared pattern.
// =============================================================================

import { test, expect } from '@playwright/test'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

const TENANT_ID = 'cena'

test.describe('EPIC_I_CONSENT_JOURNEY', () => {
  test('register at age 14 → parental-consent step → credentials → onboarding @epic-i', async ({ page }, testInfo) => {
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

    // Lock locale + tenant id so the backend on-first-sign-in path
    // can route the request through trusted-mode for the under-18
    // registrant. Same pattern student-register.spec.ts uses.
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const today = new Date()
    // Age 14 — teen tier (10-15) requires parental consent per the
    // ageGate logic the SPA imports from @cena/age-gate.
    const dob = `${today.getUTCFullYear() - 14}-06-15`

    const childEmail = `e2e-consent-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const parentEmail = `e2e-parent-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    // ── Step 1: age-gate ──
    await page.goto('/register')
    await expect(page.getByTestId('age-gate-step')).toBeVisible({ timeout: 10_000 })
    await page.getByTestId('age-gate-dob').locator('input').fill(dob)

    // The age-gate result for age 14 should surface the "minor" /
    // teen consent indicator. We don't pin the exact testid that
    // marks teen mode (varies by component); we just rely on the
    // fact that age-gate-next routes us to the consent step next.
    await page.getByTestId('age-gate-next').click()

    // ── Step 2: parental consent ──
    await expect(
      page.getByTestId('parental-consent-step'),
      'register flow must advance to parental-consent for age 14',
    ).toBeVisible({ timeout: 10_000 })
    await page.getByTestId('parent-email').locator('input').fill(parentEmail)
    await page.getByTestId('consent-checkbox').locator('input').check()
    await page.getByTestId('consent-next').click()

    // ── Step 3: credentials ──
    await expect(page.getByTestId('email-password-form')).toBeVisible({ timeout: 10_000 })
    await page.getByTestId('auth-display-name').locator('input').fill('Consent Tester')
    await page.getByTestId('auth-email').locator('input').fill(childEmail)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()

    // ── Step 4: SPA lands on /onboarding ──
    await page.waitForURL(url => url.pathname.startsWith('/onboarding'), { timeout: 20_000 })
    await expect(page.getByTestId('onboarding-page')).toBeVisible({ timeout: 10_000 })

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    expect(pageErrors,
      `page errors during consent journey: ${JSON.stringify(pageErrors.slice(0, 3))}`,
    ).toEqual([])
    const errs = consoleEntries.filter(e => e.type === 'error')
    expect(errs,
      `unexpected console errors during consent journey: ${JSON.stringify(errs.slice(0, 3))}`,
    ).toEqual([])
  })
})
