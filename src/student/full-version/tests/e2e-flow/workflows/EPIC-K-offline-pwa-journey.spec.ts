// =============================================================================
// EPIC-E2E-K — Offline / PWA journey (real browser drive)
//
// Drives a fresh student through:
//   1. Server-side bootstrap + SPA login
//   2. /home renders with a healthy network
//   3. Toggle the browser context offline (navigator.onLine = false)
//   4. Assert the OfflineBanner snackbar surfaces (.offline-banner CSS
//      class on the VSnackbar wrapper)
//   5. Toggle back online — the banner disappears (timeout=-1 means it
//      stays mounted until isOnline flips back to true)
//   6. Assert the cached offline queue (`cena-offline-queue` localStorage
//      key) hasn't grown — we did not submit answers, so nothing should
//      be queued
//
// What's NOT covered (intentional):
//   * Service-worker offline page caching — VitePWA's registerType is
//     'prompt' and the dev server does not register the SW (main.ts
//     skips registerServiceWorker() in dev). Asserting cached page
//     loads requires a prod build, outside e2e-flow scope.
//   * K-02 offline answer-queue drain — would need a session+answer
//     drive against the chaos network. Worth its own follow-up spec.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string }

test.describe('EPIC_K_OFFLINE_PWA_JOURNEY', () => {
  test('OfflineBanner surfaces when navigator.onLine flips to false @epic-k', async ({ page, context }, testInfo) => {
    test.setTimeout(120_000)

    const consoleEntries: ConsoleEntry[] = []
    const pageErrors: { message: string }[] = []

    page.on('console', msg => consoleEntries.push({ type: msg.type(), text: msg.text() }))
    page.on('pageerror', err => pageErrors.push({ message: err.message }))

    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `epic-k-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_K_OFFLINE_PWA_JOURNEY for ${email} ===\n`)

    // ── 1. Bootstrap ──
    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok()).toBe(true)
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }

    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'EpicK Tester' },
    })).status()).toBe(200)

    const reLoginResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: postClaimsToken } = await reLoginResp.json() as { idToken: string }

    expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
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
    })).status()).toBe(200)

    // ── 2. /login → /home ──
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    await page.goto('/home')
    console.log(`[epic-k] /home loaded online`)

    // OfflineBanner must NOT be in the DOM with isOffline=false. The
    // VSnackbar mounts conditionally via v-model; assert it's hidden.
    await expect(page.locator('.offline-banner')).not.toBeVisible()

    // ── 3. Simulate the browser going offline ──
    //
    // Implementation note: in dev (Vite HMR over WebSocket), calling
    // context.setOffline(true) tears the dev-server connection down,
    // and the SPA layout unmounts as Vite tries (and fails) to
    // reconcile. That's a *dev-server* artifact, not a SPA bug — in a
    // production build the SW would intercept and the page would stay
    // mounted. To exercise the OfflineBanner contract without yanking
    // the dev server, we dispatch the `offline` window event directly.
    // useNetworkStatus subscribes to that event (line 47 of
    // useNetworkStatus.ts), and the rest of the offline-aware UI keys
    // off `isOnline.value`. This is sufficient evidence that the
    // SPA-side reactive chain is wired correctly; an end-to-end
    // network-down assertion belongs in a prod-build smoke test.
    await page.evaluate(() => {
      Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false })
      window.dispatchEvent(new Event('offline'))
    })

    await expect(page.locator('.offline-banner')).toBeVisible({ timeout: 5_000 })
    console.log('[epic-k] OfflineBanner surfaced after offline event')

    const bannerText = await page.locator('.offline-banner').innerText()
    expect(bannerText.trim().length).toBeGreaterThan(0)

    // ── 4. Simulate the browser coming back online ──
    await page.evaluate(() => {
      Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => true })
      window.dispatchEvent(new Event('online'))
    })
    await expect(page.locator('.offline-banner')).not.toBeVisible({ timeout: 5_000 })
    console.log('[epic-k] OfflineBanner cleared after online event')

    // ── 5. The offline queue must be empty (we never submitted) ──
    const queue = await page.evaluate(() => {
      const raw = window.localStorage.getItem('cena-offline-queue')
      return raw ? JSON.parse(raw) : []
    })
    expect(Array.isArray(queue)).toBe(true)
    expect(queue.length, 'offline queue must be empty — we did not submit answers').toBe(0)

    // ── 6. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')
    console.log('\n=== EPIC_K DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length}`)
    console.log(`Page errors: ${pageErrors.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 10))
        console.log(`  ${e.text}`)
    }

    expect(pageErrors, 'No JS exceptions during offline/online toggle').toHaveLength(0)
  })
})
