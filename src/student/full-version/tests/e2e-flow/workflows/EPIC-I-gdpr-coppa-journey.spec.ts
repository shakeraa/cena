// =============================================================================
// EPIC-E2E-I — GDPR / COPPA / DSAR journey (real browser drive)
//
// Drives a fresh student through the full data-rights surface:
//   1. Server-side bootstrap (signUp + on-first-sign-in + /api/me/onboarding)
//   2. SPA /login form fill + submit
//   3. /settings/privacy renders (settings-privacy-page testid)
//   4. Click "Download my data" → confirm → assert
//      POST /api/me/gdpr/export returns 200 and the
//      export-success-alert surfaces
//   5. Click "Submit DSAR" → fill message → confirm → assert
//      POST /api/me/dsar returns 200 and dsar-success-alert surfaces
//   6. Skip the irreversible "Delete my data" path so the spec stays
//      replayable per-run; surface the button-visible assertion as proof
//      the UI is fully wired
//
// Diagnostics: console errors, page errors, 4xx/5xx network. EPIC-I
// is a privacy/compliance E2E — the legal posture is that EVERY
// 4xx/5xx in this flow is a real bug. We do not filter expected
// failures here.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_I_GDPR_COPPA_JOURNEY', () => {
  test('student /settings/privacy → export + DSAR end-to-end @epic-i', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string; stack?: string }[] = []
    const failedRequests: NetworkFailure[] = []

    page.on('console', msg => consoleEntries.push({
      type: msg.type(),
      text: msg.text(),
      location: msg.location()?.url ? `${msg.location().url}:${msg.location().lineNumber}` : undefined,
    }))
    page.on('pageerror', err => pageErrors.push({ message: err.message, stack: err.stack }))
    page.on('response', async (resp) => {
      if (resp.status() >= 400) {
        let body: string | undefined
        try { const t = await resp.text(); body = t.length > 800 ? `${t.slice(0, 800)}…` : t }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `epic-i-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_I_GDPR_COPPA_JOURNEY for ${email} ===\n`)

    // ── 1. Server-side bootstrap ──
    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok()).toBe(true)
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }

    const onboardResp = await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'EpicI Tester' },
    })
    expect(onboardResp.status()).toBe(200)

    const reLoginResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: postClaimsToken } = await reLoginResp.json() as { idToken: string }

    const completeOnboardResp = await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
      headers: { Authorization: `Bearer ${postClaimsToken}` },
      data: {
        role: 'student',
        locale: 'en',
        subjects: ['math'],
        dailyTimeGoalMinutes: 15,
        weeklySubjectTargets: [],
        diagnosticResults: null,
        classroomCode: null,
      },
    })
    expect(completeOnboardResp.status()).toBe(200)
    console.log('[epic-i] server-side bootstrap complete')

    // ── 2. SPA /login ──
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    console.log(`[epic-i] post-login url: ${page.url()}`)

    // ── 3. /settings/privacy renders ──
    await page.goto('/settings/privacy')
    await expect(page.getByTestId('settings-privacy-page')).toBeVisible({ timeout: 15_000 })
    console.log('[epic-i] settings-privacy-page visible')

    // ── 4. Data export round-trip ──
    await page.getByTestId('btn-download-data').click()
    await expect(page.getByTestId('export-dialog')).toBeVisible({ timeout: 5_000 })
    await page.getByTestId('export-confirm-btn').click()
    // The mutation hits /api/me/gdpr/export. Wait for the success
    // alert — if the call 4xx/5xx'd the alert never appears and the
    // test fails on this assertion.
    await expect(page.getByTestId('export-success-alert')).toBeVisible({ timeout: 15_000 })
    console.log('[epic-i] data export success alert surfaced')

    // ── 5. DSAR submission ──
    await page.getByTestId('btn-dsar').click()
    await expect(page.getByTestId('dsar-dialog')).toBeVisible({ timeout: 5_000 })
    await page.getByTestId('dsar-message-input').locator('textarea').fill(
      'EPIC-I E2E test: please confirm receipt of this data subject access request.',
    )
    await page.getByTestId('dsar-confirm-btn').click()
    await expect(page.getByTestId('dsar-success-alert')).toBeVisible({ timeout: 15_000 })
    console.log('[epic-i] DSAR success alert surfaced')

    // ── 6. Erasure UI is wired (assert visible only — irreversible) ──
    await expect(page.getByTestId('btn-delete-data')).toBeVisible()
    console.log('[epic-i] erasure button wired (not clicked — irreversible)')

    // ── 7. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')

    console.log('\n=== EPIC_I DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length}`)
    console.log(`Page errors: ${pageErrors.length}`)
    console.log(`Failed network: ${failedRequests.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 10))
        console.log(`  ${e.text}${e.location ? ` @ ${e.location}` : ''}`)
    }
    if (failedRequests.length) {
      console.log('— failed requests —')
      for (const f of failedRequests.slice(0, 15))
        console.log(`  ${f.status} ${f.method} ${f.url} :: ${(f.body ?? '').slice(0, 200)}`)
    }

    // Compliance posture: every failure in this flow is a real bug.
    expect(pageErrors, 'No JS exceptions on the privacy + GDPR flow').toHaveLength(0)
    expect(failedRequests, 'No 4xx/5xx during privacy + GDPR flow').toHaveLength(0)
  })
})
