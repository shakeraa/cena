// =============================================================================
// EPIC-E2E-C — Student learning core journey (real browser drive)
//
// Per tasks/e2e-flow/PROMPT-full-journey-per-epic.md, this spec drives a
// real Chromium walkthrough of the Start-Session UI:
//
//   /register (fresh student)
//   → /login (real form)
//   → /home
//   → /session (setup form)
//   → click subject + duration + mode + setup-start
//   → /session/{sessionId} (runner)
//   → runner-question-card visible
//   → click an answer choice (if MC) or runner-exit
//
// Diagnostics (console / page errors / failed network requests) are
// collected and printed at the end. The goal is observation: what
// errors does a real student see in dev tools when they run a session?
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string; location?: string }
interface NetworkFailure { method: string; url: string; status: number; body?: string }

test.describe('EPIC_C_LEARNING_JOURNEY', () => {
  test('register → login → /session setup → runner → answer click → exit @epic-c', async ({ page }, testInfo) => {
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
        try {
          const text = await resp.text()
          body = text.length > 800 ? `${text.slice(0, 800)}…` : text
        }
        catch { body = '<navigation flushed>' }
        failedRequests.push({ method: resp.request().method(), url: resp.url(), status: resp.status(), body })
      }
    })

    // Locale + tenant seeds
    await page.addInitScript((tenantId: string) => {
      window.localStorage.setItem('cena-student-locale', JSON.stringify({ code: 'en', locked: true, version: 1 }))
      window.localStorage.setItem('cena-e2e-tenant-id', tenantId)
    }, TENANT_ID)

    const email = `epic-c-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
    console.log(`\n=== EPIC_C_LEARNING_JOURNEY for ${email} ===\n`)

    // ── 1. Fresh student bootstrap, fully server-side, before the
    //       SPA ever loads. on-first-sign-in creates AdminUser doc +
    //       StudentProfileSnapshot; /api/me/onboarding marks onboarded
    //       so the SPA's requiresOnboarded route guard lets us into
    //       /session without first wading through the 8-step wizard
    //       (which has its own dedicated journey spec).
    const signupResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok()).toBe(true)
    const { idToken: bootstrapToken } = await signupResp.json() as { idToken: string }
    const onboardResp = await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
      headers: { Authorization: `Bearer ${bootstrapToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'EpicC Tester' },
    })
    expect(onboardResp.status()).toBe(200)

    // Re-sign-in to get a fresh idToken with the just-set custom claims
    // (role=STUDENT etc) — the original signupResp idToken pre-dates
    // those claims.
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
    console.log(`[epic-c] /api/me/onboarding -> ${completeOnboardResp.status()}`)
    expect(completeOnboardResp.status()).toBe(200)

    // ── 2. SPA /login form (real clicks). User is already fully
    //       onboarded server-side, so the route guard sends us to /home
    //       (or whatever the configured signed-in landing is).
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })
    console.log('[epic-c] post-login url:', page.url())

    // ── 3. Navigate to /session setup ──
    await page.goto('/session')
    await expect(page.getByTestId('session-setup-page')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('session-setup-form')).toBeVisible()

    // ── 4. Form already defaults math=selected, duration=15, mode=mc.
    //      Clicking setup-subject-math would toggle it OFF (the form
    //      uses toggleSubject), leaving the array empty and disabling
    //      the start button. Skip the subject click; just verify the
    //      tile is rendered so the DOM-level boundary still asserts.
    const firstSubject = page.locator('[data-testid^="setup-subject-"]').first()
    await firstSubject.waitFor({ state: 'visible', timeout: 10_000 })
    const subjectTestId = await firstSubject.getAttribute('data-testid')
    console.log(`[epic-c] subject visible (default-selected): ${subjectTestId}`)

    const firstDuration = page.locator('[data-testid^="setup-duration-"]').first()
    if (await firstDuration.isVisible().catch(() => false)) {
      const durationId = await firstDuration.getAttribute('data-testid')
      console.log(`[epic-c] duration click: ${durationId}`)
      await firstDuration.click()
    }

    const firstMode = page.locator('[data-testid^="setup-mode-"]').first()
    if (await firstMode.isVisible().catch(() => false)) {
      const modeId = await firstMode.getAttribute('data-testid')
      console.log(`[epic-c] mode click: ${modeId}`)
      await firstMode.click()
    }

    // Capture the /api/sessions/start response so we can report on it
    const sessionStartPromise = page.waitForResponse(
      r => r.url().includes('/api/sessions/start') && r.request().method() === 'POST',
      { timeout: 15_000 },
    ).catch(() => null)

    await page.getByTestId('setup-start').click()

    const sessionStartResp = await sessionStartPromise
    if (sessionStartResp) {
      console.log(`[epic-c] /api/sessions/start -> ${sessionStartResp.status()}`)
    }
    else {
      console.log('[epic-c] /api/sessions/start was not observed within 15s')
    }

    // ── 5. Land on /session/{sessionId} runner ──
    const runnerLanded = await page
      .waitForURL(/\/session\/[^/]+/, { timeout: 20_000 })
      .then(() => true)
      .catch(() => false)
    console.log(`[epic-c] runner-landed=${runnerLanded}, url=${page.url()}`)

    if (runnerLanded) {
      // ── 6. Wait for the runner question card OR an error/loading state ──
      const runnerPage = page.getByTestId('session-runner-page')
      await expect(runnerPage).toBeVisible({ timeout: 15_000 })

      const questionCard = page.getByTestId('runner-question-card')
      const runnerError = page.getByTestId('runner-error')
      const runnerLoading = page.getByTestId('runner-loading')

      const settledIn = 30_000
      const settled = await Promise.race([
        questionCard.waitFor({ state: 'visible', timeout: settledIn }).then(() => 'question'),
        runnerError.waitFor({ state: 'visible', timeout: settledIn }).then(() => 'error'),
      ]).catch(() => 'timeout')
      console.log(`[epic-c] runner settled state: ${settled}`)

      if (settled === 'question') {
        // ── 7. Try to click an answer choice if a multi-choice gate is rendered ──
        const firstChoice = page.locator('[data-testid^="choice-"]').first()
        const choiceVisible = await firstChoice.isVisible().catch(() => false)
        if (choiceVisible) {
          const choiceId = await firstChoice.getAttribute('data-testid')
          console.log(`[epic-c] answer click: ${choiceId}`)
          await firstChoice.click()

          // Did feedback DOM appear?
          const feedbackAppeared = await page.getByTestId('answer-feedback')
            .waitFor({ state: 'visible', timeout: 10_000 })
            .then(() => true)
            .catch(() => false)
          console.log(`[epic-c] answer-feedback visible: ${feedbackAppeared}`)
        }
        else {
          console.log('[epic-c] no choice-* element visible (question kind may not be MC)')
        }
      }

      // ── 8. Exit the runner ──
      const exitVisible = await page.getByTestId('runner-exit').isVisible().catch(() => false)
      if (exitVisible) {
        await page.getByTestId('runner-exit').click()
        console.log('[epic-c] clicked runner-exit')
      }
    }

    // ── 9. Diagnostics ──
    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('page-errors.json', { body: JSON.stringify(pageErrors, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })

    const errs = consoleEntries.filter(e => e.type === 'error')
    const warns = consoleEntries.filter(e => e.type === 'warning')
    console.log('\n=== EPIC_C DIAGNOSTICS SUMMARY ===')
    console.log(`Console: ${consoleEntries.length} total | errors=${errs.length} | warnings=${warns.length}`)
    console.log(`Page errors (uncaught throws): ${pageErrors.length}`)
    console.log(`Failed network requests (4xx/5xx): ${failedRequests.length}`)
    if (errs.length) {
      console.log('— console errors —')
      for (const e of errs.slice(0, 20))
        console.log(`  ${e.text}${e.location ? ` @ ${e.location}` : ''}`)
    }
    if (pageErrors.length) {
      console.log('— page errors —')
      for (const e of pageErrors.slice(0, 10))
        console.log(`  ${e.message}`)
    }
    if (failedRequests.length) {
      console.log('— failed requests —')
      for (const f of failedRequests.slice(0, 30))
        console.log(`  ${f.status} ${f.method} ${f.url} :: ${(f.body ?? '').slice(0, 200)}`)
    }
  })
})
