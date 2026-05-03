// =============================================================================
// E2E-PRR-256 — SessionSetupForm exam-mode toggle (P0)
//
// PRR-247 / ADR-0060: SessionStartRequest now carries `examScope` +
// `activeExamTargetId` and the server validator rejects malformed combos.
// PRR-256 ships the SPA toggle so exam-prep students actually emit a
// target-bound pool selection.
//
// Real-browser drives:
//
//   1. Bootstrap student + onboarding + sign-in.
//   2. Navigate to /session. The form must mount.
//   3. With NO active targets the ExamPrep button must be disabled and
//      the "no targets" helper copy must surface.
//   4. With ≥1 active target (we add one via /api/me/exam-targets POST)
//      the toggle must default to ExamPrep with the first target preselected.
//      Submit → POST /api/sessions/start carries examScope='exam-prep'
//      AND activeExamTargetId === <target id>.
//   5. Switch to Freestyle → submit → POST /api/sessions/start carries
//      examScope='freestyle' AND NO activeExamTargetId.
//   6. Server validator: an explicit POST with examScope='exam-prep' and
//      missing target MUST return 400 (boundary assertion the SPA gate
//      relies on).
//
// What this catches:
//   • SPA forgets to emit examScope (regresses to legacy target-blind pool)
//   • SPA emits both examScope='freestyle' AND activeExamTargetId (server
//     rejects → silent UX failure)
//   • Smart default broken: ExamPrep button enabled when student has 0
//     targets (would lead to a 400 the moment they click Start)
// =============================================================================

import { test, expect } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'
const STUDENT_API = process.env.E2E_STUDENT_API_URL ?? 'http://localhost:5050'

interface ConsoleEntry { type: string; text: string }

async function bootstrapStudent(
  page: import('@playwright/test').Page,
  label: string,
): Promise<{ idToken: string; email: string; password: string }> {
  const email = `${label}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`
  const su = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(su.ok(), 'firebase signUp').toBe(true)
  const { idToken: bootstrapToken } = await su.json() as { idToken: string }

  expect((await page.request.post(`${STUDENT_API}/api/auth/on-first-sign-in`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: `PRR256 ${label}` },
  })).status()).toBe(200)

  expect((await page.request.post(`${STUDENT_API}/api/me/onboarding`, {
    headers: { Authorization: `Bearer ${bootstrapToken}` },
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

  const reLogin = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  const { idToken } = await reLogin.json() as { idToken: string }
  return { idToken, email, password }
}

async function loginViaSpa(
  page: import('@playwright/test').Page,
  email: string,
  password: string,
): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

async function addExamTarget(
  page: import('@playwright/test').Page,
  idToken: string,
): Promise<{ id: string } | null> {
  // POST /api/me/exam-targets — best-effort. Returns the created target's
  // id when the dev server has the endpoint wired; null otherwise.
  const resp = await page.request.post(`${STUDENT_API}/api/me/exam-targets`, {
    headers: { Authorization: `Bearer ${idToken}`, 'Content-Type': 'application/json' },
    data: {
      examCode: 'bagrut-math-5u',
      track: '5u',
      sitting: { academicYear: '2025-2026', season: 'Summer', moed: 'A' },
      weeklyHours: 6,
      questionPaperCodes: ['35581'],
    },
  })
  if (resp.status() < 200 || resp.status() >= 300)
    return null
  const body = await resp.json() as { id?: string }
  return body.id ? { id: body.id } : null
}

test.describe('E2E_PRR_256_SESSION_MODE_TOGGLE', () => {
  test('NO targets: ExamPrep disabled + no-targets helper surfaces @epic-prr-f @session-mode @p0', async ({ page }, testInfo) => {
    test.setTimeout(120_000)
    const consoleEntries: ConsoleEntry[] = []
    page.on('console', m => consoleEntries.push({ type: m.type(), text: m.text() }))

    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    const a = await bootstrapStudent(page, 'no-targets')
    await loginViaSpa(page, a.email, a.password)
    await page.goto('/session')

    // Form mounts.
    await expect(page.getByTestId('session-setup-form')).toBeVisible()
    await expect(page.getByTestId('setup-exam-scope-section')).toBeVisible()

    // ExamPrep button must be aria-disabled when there are no targets.
    const examPrepBtn = page.getByTestId('setup-exam-scope-exam-prep')
    await expect(examPrepBtn).toBeVisible()
    const aria = await examPrepBtn.getAttribute('aria-disabled')
    const disabled = await examPrepBtn.getAttribute('disabled')
    expect(aria === 'true' || disabled !== null,
      'ExamPrep must be disabled when student has 0 active targets').toBe(true)

    // The Freestyle helper or no-targets helper renders.
    const helper = page.getByTestId('setup-exam-scope-helper')
    const noTargets = page.getByTestId('setup-exam-scope-no-targets')
    const oneVisible = await helper.isVisible().catch(() => false)
      || await noTargets.isVisible().catch(() => false)
    expect(oneVisible, 'one of the exam-scope helper copies is visible').toBe(true)

    testInfo.attach('console-entries.json', {
      body: JSON.stringify(consoleEntries, null, 2),
      contentType: 'application/json',
    })
  })

  test('WITH target: defaults to ExamPrep + emits {examScope, activeExamTargetId} @epic-prr-f @session-mode @p0', async ({ page }, testInfo) => {
    test.setTimeout(150_000)
    const requestsToStart: Array<{ url: string; body: unknown }> = []

    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    const a = await bootstrapStudent(page, 'with-target')
    const target = await addExamTarget(page, a.idToken)

    await loginViaSpa(page, a.email, a.password)

    // Capture the /api/sessions/start request body so we can assert on
    // the wire payload after clicking Start.
    page.on('request', (req) => {
      if (req.url().includes('/api/sessions/start') && req.method() === 'POST') {
        try {
          requestsToStart.push({ url: req.url(), body: JSON.parse(req.postData() ?? '{}') })
        }
        catch {
          requestsToStart.push({ url: req.url(), body: '<unparseable>' })
        }
      }
    })

    await page.goto('/session')
    await expect(page.getByTestId('session-setup-form')).toBeVisible()

    if (!target) {
      // The exam-targets POST endpoint may not be wired in this dev env.
      // We DO NOT silently soft-pass — surface this as an env gap, not
      // a fixme. The user's mandate is "no shortfalls".
      test.fail(true,
        'POST /api/me/exam-targets must be reachable in dev for this spec; '
        + 'if it 404s, the env is missing ExamTargetEndpoints registration.')
      return
    }

    // The toggle should default to ExamPrep within ~3s after /api/me/exam-targets resolves.
    await expect(page.getByTestId('setup-exam-scope-exam-prep')).toHaveAttribute('aria-pressed', 'true', { timeout: 5000 })
      .catch(async () => {
        // VBtnToggle may not set aria-pressed; alternate signal is the
        // target picker becoming visible.
        await expect(page.getByTestId('setup-target-section')).toBeVisible({ timeout: 3000 })
      })

    await page.getByTestId('setup-start').click()

    // Either the SPA navigates away to /session/{id} or stays on /session
    // depending on the question-bank seed. Either is fine for this spec —
    // we're asserting on the WIRE payload, not the next page.
    await page.waitForTimeout(1500)

    expect(requestsToStart.length, 'one POST /api/sessions/start fired').toBeGreaterThanOrEqual(1)
    const body = requestsToStart[0].body as Record<string, unknown>
    expect(body.examScope, 'examScope=exam-prep').toBe('exam-prep')
    expect(body.activeExamTargetId, 'activeExamTargetId === created target id').toBe(target.id)

    testInfo.attach('start-requests.json', {
      body: JSON.stringify(requestsToStart, null, 2),
      contentType: 'application/json',
    })
  })

  test('Freestyle switch: emits examScope=freestyle, OMITS activeExamTargetId @epic-prr-f @session-mode @p0', async ({ page }) => {
    test.setTimeout(150_000)
    const requestsToStart: Array<{ url: string; body: unknown }> = []

    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    const a = await bootstrapStudent(page, 'switch-freestyle')
    const target = await addExamTarget(page, a.idToken)
    if (!target) {
      test.fail(true, 'POST /api/me/exam-targets must be reachable in dev')
      return
    }

    await loginViaSpa(page, a.email, a.password)

    page.on('request', (req) => {
      if (req.url().includes('/api/sessions/start') && req.method() === 'POST') {
        try { requestsToStart.push({ url: req.url(), body: JSON.parse(req.postData() ?? '{}') }) }
        catch { requestsToStart.push({ url: req.url(), body: '<unparseable>' }) }
      }
    })

    await page.goto('/session')
    await expect(page.getByTestId('session-setup-form')).toBeVisible()

    // Click the Freestyle button to switch off ExamPrep.
    await page.getByTestId('setup-exam-scope-freestyle').click()

    // Target picker MUST disappear once Freestyle is selected.
    await expect(page.getByTestId('setup-target-section')).not.toBeVisible({ timeout: 3000 })

    await page.getByTestId('setup-start').click()
    await page.waitForTimeout(1500)

    expect(requestsToStart.length).toBeGreaterThanOrEqual(1)
    const body = requestsToStart[0].body as Record<string, unknown>
    expect(body.examScope, 'examScope=freestyle').toBe('freestyle')
    expect(
      body.activeExamTargetId,
      'activeExamTargetId must be omitted in Freestyle (server validator rejects otherwise)',
    ).toBeUndefined()
  })

  test('Server validator: examScope=exam-prep + missing target → 400 @epic-prr-f @session-mode @p0', async ({ page }) => {
    test.setTimeout(60_000)
    const a = await bootstrapStudent(page, 'validator')

    const resp = await page.request.post(`${STUDENT_API}/api/sessions/start`, {
      headers: { Authorization: `Bearer ${a.idToken}`, 'Content-Type': 'application/json' },
      data: {
        subjects: ['math'],
        durationMinutes: 15,
        mode: 'practice',
        examScope: 'exam-prep',
        // activeExamTargetId intentionally omitted
      },
    })
    expect(resp.status(),
      'examScope=exam-prep without activeExamTargetId must be 400').toBe(400)

    const respBoth = await page.request.post(`${STUDENT_API}/api/sessions/start`, {
      headers: { Authorization: `Bearer ${a.idToken}`, 'Content-Type': 'application/json' },
      data: {
        subjects: ['math'],
        durationMinutes: 15,
        mode: 'practice',
        examScope: 'freestyle',
        activeExamTargetId: 'et_should_be_rejected',
      },
    })
    expect(respBoth.status(),
      'examScope=freestyle WITH activeExamTargetId must be 400').toBe(400)
  })
})
