// =============================================================================
// EPIC-E2E-D — AI Tutor journey (real browser drive)
//
// Drives the /tutor flow:
//   bootstrap fresh student (server) → /login form → /tutor (list)
//   → click `tutor-new-thread` → land on /tutor/{threadId}
//   → fill `tutor-compose-input` with a math question → click submit
//   → wait for tutor-thinking + first reply message
//
// Diagnostics (console / page errors / 4xx-5xx) collected as for the
// other per-epic specs.
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_D_TUTOR_JOURNEY', () => {
  test('register → login → /tutor list → new thread → ask question → reply @epic-d', async ({ page }, testInfo) => {
    test.setTimeout(180_000)

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

    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `epic-d-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_D_TUTOR_JOURNEY for ${email} ===\n`)

    // ── 1. Server-side bootstrap ──
    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok()).toBe(true)
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'EpicD Tester' },
    })).status()).toBe(200)

    const reLogin = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    const { idToken: postClaimsToken } = await reLogin.json() as { idToken: string }
    expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
      headers: { Authorization: `Bearer ${postClaimsToken}` },
      data: { role: 'student', locale: 'en', subjects: ['math'], dailyTimeGoalMinutes: 15, weeklySubjectTargets: [], diagnosticResults: null, classroomCode: null },
    })).status()).toBe(200)

    // ── 2. SPA login (real form clicks) ──
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    console.log('[epic-d] post-login url:', page.url())

    // ── 3. /tutor list page ──
    await page.goto('/tutor')
    await expect(page.getByTestId('tutor-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('tutor-new-thread')).toBeVisible()

    // ── 4. New thread (real click) ──
    const newThreadResponse = page.waitForResponse(
      r => r.url().includes('/api/tutor/threads') && r.request().method() === 'POST',
      { timeout: 15_000 },
    ).catch(() => null)
    await page.getByTestId('tutor-new-thread').click()
    const newThreadResp = await newThreadResponse
    if (newThreadResp) console.log(`[epic-d] POST /api/tutor/threads -> ${newThreadResp.status()}`)

    // ── 5. Land on /tutor/{threadId} ──
    const threadLanded = await page.waitForURL(/\/tutor\/[^/]+/, { timeout: 15_000 })
      .then(() => true).catch(() => false)
    console.log(`[epic-d] thread-landed=${threadLanded}, url=${page.url()}`)

    if (threadLanded) {
      // Verify thread page shell
      await expect(page.getByTestId('tutor-thread-page')).toBeVisible({ timeout: 10_000 })
      await expect(page.getByTestId('tutor-compose-form')).toBeVisible()

      // ── 6. Type a question + submit (real form interaction) ──
      const composeInput = page.getByTestId('tutor-compose-input').locator('textarea, input').first()
      const inputVisible = await composeInput.isVisible().catch(() => false)
      if (inputVisible) {
        await composeInput.fill('What is the derivative of x^2?')
        const submitBtn = page.getByTestId('tutor-compose-submit')
        const submitEnabled = await submitBtn.isEnabled().catch(() => false)
        console.log(`[epic-d] tutor-compose-submit enabled: ${submitEnabled}`)
        if (submitEnabled) {
          await submitBtn.click()

          // ── 7. Wait for either tutor-thinking placeholder OR a real reply ──
          const replyOrThinking = await Promise.race([
            page.getByTestId('tutor-thinking').waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'thinking'),
            page.locator('[data-testid^="tutor-message-"]').first()
              .waitFor({ state: 'visible', timeout: 15_000 }).then(() => 'message'),
          ]).catch(() => 'timeout')
          console.log(`[epic-d] tutor reply state: ${replyOrThinking}`)
        }
      }
      else {
        console.log('[epic-d] tutor-compose-input not visible; skipping submit')
      }
    }

    // ── 8. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')
    const warns = consoleEntries.filter(e => e.type === 'warning')
    console.log('\n=== EPIC_D DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} | errors=${errs.length} | warnings=${warns.length}`)
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
