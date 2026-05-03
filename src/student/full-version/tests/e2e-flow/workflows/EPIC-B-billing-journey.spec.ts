// =============================================================================
// EPIC-E2E-B — Subscription & billing journey extensions
//
// The flagship happy path lives in `subscription-happy-path.spec.ts`
// (Plus annual: pricing → checkout-session → activate → confirm-active).
// This file extends with the post-activation flows the EPIC-E2E-B task
// list calls out: B-03 cancel-back from checkout, B-04 tier upgrade,
// B-06 cancel-at-period-end, B-07 sibling discount, B-08 bank transfer.
//
// Diagnostic-collection pattern matches student-full-journey.spec.ts:
// every test attaches console + page-error + 4xx/5xx network arrays so
// we surface what the page actually shouts about — even when the
// boundary asserts succeed. Real-browser clicks where the SPA exposes
// the affordance; pure API drive only when the UI is genuinely absent
// (called out per-test below — no silent stubbing).
//
// Tests honour the scope split with claude-code: this file owns
// EPIC-B; EPIC-C learning + EPIC-D tutor + EPIC-H tenant isolation
// are claude-code's. Nothing in here touches those domains.
// =============================================================================

import { test, expect, type Page } from '@playwright/test'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'
const EMU_HOST = process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'

interface ConsoleEntry {
  type: string
  text: string
  location?: string
}
interface NetworkFailure {
  method: string
  url: string
  status: number
  body?: string
}

/** Attaches console / page-error / 4xx-5xx listeners and returns the live arrays. */
function attachDiagnostics(page: Page) {
  const consoleEntries: ConsoleEntry[] = []
  const pageErrors: { message: string; stack?: string }[] = []
  const failedRequests: NetworkFailure[] = []

  page.on('console', msg => {
    consoleEntries.push({
      type: msg.type(),
      text: msg.text(),
      location: msg.location()?.url
        ? `${msg.location().url}:${msg.location().lineNumber}`
        : undefined,
    })
  })
  page.on('pageerror', err => {
    pageErrors.push({ message: err.message, stack: err.stack })
  })
  page.on('response', async resp => {
    if (resp.status() >= 400) {
      let body: string | undefined
      try {
        const text = await resp.text()
        body = text.length > 800 ? `${text.slice(0, 800)}…(truncated)` : text
      }
      catch {
        body = '<body unreadable>'
      }
      failedRequests.push({
        method: resp.request().method(),
        url: resp.url(),
        status: resp.status(),
        body,
      })
    }
  })

  return { consoleEntries, pageErrors, failedRequests }
}

interface ProvisionedAccount {
  email: string
  password: string
  idToken: string
  studentId: string
}

/**
 * Common setup: provision a fresh student via Firebase emu, bootstrap
 * Marten via /api/auth/on-first-sign-in, drive the SPA's /login form
 * so Firebase IndexedDB persists the session, and sign-in via emu REST
 * to grab a fresh idToken with the customClaims the bootstrap pushed.
 *
 * Returns the email/password (for re-login if needed), the idToken
 * (for direct API calls), and the studentId (required field on
 * subscription requests).
 */
async function provisionFreshStudent(page: Page): Promise<ProvisionedAccount> {
  await page.addInitScript(() => {
    window.localStorage.setItem(
      'cena-student-locale',
      JSON.stringify({ code: 'en', locked: true, version: 1 }),
    )
  })

  const email = `e2e-billing-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
  const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

  const signupResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(signupResp.ok(), 'Firebase emu signUp must succeed').toBe(true)
  await signupResp.json() as { idToken: string; localId: string }

  const onboardResp = await page.request.post('/api/auth/on-first-sign-in', {
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Billing E2E' },
  })
  // The endpoint requires the bearer; replay it with the captured signup token.
  // We re-issue a fresh idToken below to read /api/me; re-call on-first-sign-in
  // with that fresh token to actually push the customClaims into the JWT.
  const tokenResp1 = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(tokenResp1.ok(), 'Firebase emu signIn must succeed').toBe(true)
  const { idToken: rawIdToken } = await tokenResp1.json() as { idToken: string }

  const onFirstSignIn = await page.request.post('/api/auth/on-first-sign-in', {
    headers: { Authorization: `Bearer ${rawIdToken}` },
    data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Billing E2E' },
  })
  expect(
    onFirstSignIn.status(),
    'on-first-sign-in must bootstrap Marten state for the fresh student',
  ).toBe(200)
  void onboardResp // earlier call without auth was discarded; this is the load-bearing one

  // Drive the SPA's real /login form so Firebase JS SDK persists the
  // session in IndexedDB. Without this, /pricing's launchCheckout
  // bounces to /register because authStore.isSignedIn is false.
  await page.goto('/login')
  await page.getByTestId('auth-email').locator('input').fill(email)
  await page.getByTestId('auth-password').locator('input').fill(password)
  await page.getByTestId('auth-submit').click()
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })

  // Re-fetch idToken AFTER on-first-sign-in pushed claims so subsequent
  // API calls carry role/tenant/school. The first idToken (line above)
  // was issued before the claims push.
  const tokenResp = await page.request.post(
    `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
    { data: { email, password, returnSecureToken: true } },
  )
  expect(tokenResp.ok(), 'Firebase emu signInWithPassword must succeed').toBe(true)
  const { idToken } = await tokenResp.json() as { idToken: string }

  const me = await page.request.get('/api/me', {
    headers: { Authorization: `Bearer ${idToken}` },
  })
  expect(me.ok(), '/api/me must be reachable post-login').toBe(true)
  const meBody = await me.json() as { studentId?: string }
  expect(meBody.studentId, '/api/me must carry studentId').toBeTruthy()

  return { email, password, idToken, studentId: meBody.studentId! }
}

/**
 * Drive checkout-session + sandbox activate to put the account into
 * Active state on the requested tier/cycle. Returns the activate
 * response status — caller asserts on the read-model after.
 */
async function activateSubscription(
  page: Page,
  acct: ProvisionedAccount,
  tier: 'Basic' | 'Plus' | 'Premium',
  cycle: 'Monthly' | 'Annual',
): Promise<void> {
  const checkoutResp = await page.request.post('/api/me/subscription/checkout-session', {
    headers: { Authorization: `Bearer ${acct.idToken}` },
    data: {
      primaryStudentId: acct.studentId,
      tier,
      billingCycle: cycle,
      idempotencyKey: `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    },
  })
  expect(checkoutResp.status(), 'checkout-session must return 200').toBe(200)

  const activateResp = await page.request.post('/api/me/subscription/activate', {
    headers: { Authorization: `Bearer ${acct.idToken}` },
    data: {
      primaryStudentId: acct.studentId,
      tier,
      billingCycle: cycle,
      paymentIdempotencyKey: `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    },
  })
  expect(activateResp.status(), 'sandbox activate must return 200').toBe(200)
}

test.describe('EPIC-E2E-B — billing extensions', () => {
  // ─────────────────────────────────────────────────────────────────────
  // B-03 — Stripe redirected the user to /subscription/cancel
  //
  // Real journey: user clicks "Back" on the Stripe checkout page →
  // Stripe redirects to the configured cancel_url (/subscription/cancel)
  // with no state mutated server-side. The SPA must render the
  // positive-framing landing card with two affordances: "Back to
  // Pricing" and "Go Home". No subscription state should be created.
  // ─────────────────────────────────────────────────────────────────────
  test('B-03 cancel-back: /subscription/cancel renders, "back to pricing" routes correctly @billing @p1', async ({ page }, testInfo) => {
    const { consoleEntries, pageErrors, failedRequests } = attachDiagnostics(page)

    // Lock the locale before navigation so the FirstRunLanguageChooser
    // modal (data-testid="first-run-chooser") does not intercept pointer
    // events on the cancel landing card. Without this seed every fresh
    // session opens the chooser and "Back to Pricing" never receives
    // the click. Same pattern provisionFreshStudent() uses.
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    // Public route: no auth required — Stripe can redirect any user here.
    await page.goto('/subscription/cancel')
    await expect(page.getByTestId('subscription-cancel-page')).toBeVisible({ timeout: 10_000 })

    // The landing card carries two CTAs: "Back to Pricing" (primary)
    // and "Go Home" (secondary). The Vuetify VBtn renders as a real
    // <button> with the i18n-translated label as text. We select via
    // the locale-en text directly since this test pinned locale=en.
    const backToPricingBtn = page.getByRole('button', { name: /back to pricing/i })
    await expect(backToPricingBtn, 'Back-to-pricing CTA must be present on the cancel landing').toBeVisible({ timeout: 10_000 })
    await backToPricingBtn.click()
    await page.waitForURL(/\/pricing/, { timeout: 10_000 })
    expect(page.url(), 'cancel-back must route to /pricing').toContain('/pricing')

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    expect(pageErrors, `page errors on cancel-back: ${JSON.stringify(pageErrors)}`).toEqual([])
  })

  // ─────────────────────────────────────────────────────────────────────
  // B-04 — Tier upgrade (Basic → Plus mid-cycle)
  //
  // Production flow per the task body: existing Basic monthly
  // subscriber → /account/subscription → upgrade to Plus → Stripe
  // prorates → SubscriptionTierChangedV1 → LLM router sees new tier.
  //
  // Today's reality: PATCH /api/me/subscription/tier exists on the
  // backend (SubscriptionManagementEndpoints.cs `ChangeTier`) but the
  // SPA's /account/subscription page has NO upgrade UI — only refund,
  // cancel, and add-sibling. So this test exercises the API contract
  // directly and asserts the read-model + DOM (post-refresh) reflect
  // the new tier. The UI gap is documented in REPORT-EPIC-B.md as a
  // pending follow-up; this spec is the regression catcher for the
  // already-shipped backend.
  // ─────────────────────────────────────────────────────────────────────
  test('B-04 tier upgrade: PATCH /tier flips Basic → Plus, /account/subscription DOM updates @billing @p1', async ({ page }, testInfo) => {
    const { consoleEntries, pageErrors, failedRequests } = attachDiagnostics(page)
    const acct = await provisionFreshStudent(page)
    await activateSubscription(page, acct, 'Basic', 'Monthly')

    // PATCH /tier — Stripe-prorate semantics on the dev sandbox:
    // ChangeTier emits SubscriptionTierChangedV1; the read-model
    // updates synchronously on the warm dev stack.
    const upgradeResp = await page.request.patch('/api/me/subscription/tier', {
      headers: { Authorization: `Bearer ${acct.idToken}` },
      data: { newTier: 'Plus' },
    })
    expect(
      upgradeResp.status(),
      `PATCH /api/me/subscription/tier must succeed; got ${upgradeResp.status()}: ${await upgradeResp.text().catch(() => '<unreadable>')}`,
    ).toBe(200)

    // Read-model boundary: SubscriptionStatusDto.currentTier reflects Plus.
    const statusResp = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${acct.idToken}` },
    })
    expect(statusResp.ok()).toBe(true)
    const status = await statusResp.json() as { status: string; currentTier: string }
    expect(status.status, 'sub must remain Active after tier change').toBe('Active')
    expect(status.currentTier, 'currentTier must reflect upgrade').toBe('Plus')

    // DOM boundary: /account/subscription shows the new tier label.
    // The page renders tier as `${status.currentTier}` next to the
    // status header; we just check the literal text appears.
    await page.goto('/account/subscription')
    await expect(page.getByTestId('account-subscription-page')).toBeVisible({ timeout: 10_000 })
    await expect(page.getByText('Plus', { exact: false }).first(),
      '/account/subscription must surface the new tier'
    ).toBeVisible({ timeout: 10_000 })

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    expect(pageErrors).toEqual([])
  })

  // ─────────────────────────────────────────────────────────────────────
  // B-06 — Cancel from inside the app
  //
  // Drive the real cancel UI: /account/subscription → "Cancel" button →
  // PRR-331 churn-reason dialog → "Confirm cancel" → cancel-confirm
  // POST → SubscriptionStatus flips. The task title says "cancel at
  // period end" but the POST /cancel endpoint today is a terminal
  // cancel (Status=Cancelled). Document the gap in the report; the
  // regression-catcher value is unchanged.
  // ─────────────────────────────────────────────────────────────────────
  test('B-06 cancel: /account/subscription → cancel-confirm → SubscriptionStatus flips @billing @p0', async ({ page }, testInfo) => {
    const { consoleEntries, pageErrors, failedRequests } = attachDiagnostics(page)
    const acct = await provisionFreshStudent(page)
    await activateSubscription(page, acct, 'Plus', 'Monthly')

    await page.goto('/account/subscription')
    await expect(page.getByTestId('account-subscription-page')).toBeVisible({ timeout: 10_000 })
    await expect(page.getByTestId('account-cancel'),
      'cancel CTA must be visible for an active subscriber'
    ).toBeVisible({ timeout: 10_000 })

    // Open the dialog, fill optional churn reason, confirm.
    await page.getByTestId('account-cancel').click()
    await expect(page.getByTestId('cancel-dialog')).toBeVisible()

    const cancelResponse = page.waitForResponse(
      r => r.url().includes('/api/me/subscription/cancel')
        && r.request().method() === 'POST',
      { timeout: 15_000 },
    )
    await page.getByTestId('cancel-confirm').click()
    const cancelResp = await cancelResponse
    expect(
      cancelResp.status(),
      `POST /api/me/subscription/cancel must succeed; got ${cancelResp.status()}`,
    ).toBeGreaterThanOrEqual(200)
    expect(cancelResp.status()).toBeLessThan(300)

    // Read-model boundary: state must move out of Active.
    const statusResp = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${acct.idToken}` },
    })
    expect(statusResp.ok()).toBe(true)
    const status = await statusResp.json() as { status: string }
    expect(['Cancelled', 'CancelPending'], `unexpected post-cancel status: ${status.status}`)
      .toContain(status.status)

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    expect(pageErrors).toEqual([])
  })

  // ─────────────────────────────────────────────────────────────────────
  // B-07 — Sibling discount
  //
  // Drive the sibling-add dialog through the real UI: open dialog,
  // type a sibling student id, confirm → POST /api/me/subscription/siblings.
  // Read-model linkedStudentCount must increment.
  //
  // We use the same student's id as the "sibling" (the dev backend
  // doesn't validate the sibling's existence beyond a basic shape
  // check). This is acceptable for the API contract test — the goal
  // is to prove the dialog wires to the endpoint and the count moves.
  // The "discount" math itself is asserted in the unit-test layer.
  // ─────────────────────────────────────────────────────────────────────
  test('B-07 sibling: dialog → confirm → linkedStudentCount increments @billing @p1', async ({ page }, testInfo) => {
    const { consoleEntries, pageErrors, failedRequests } = attachDiagnostics(page)
    const acct = await provisionFreshStudent(page)
    await activateSubscription(page, acct, 'Plus', 'Monthly')

    await page.goto('/account/subscription')
    await expect(page.getByTestId('account-subscription-page')).toBeVisible({ timeout: 10_000 })

    // Open the sibling-add dialog. The trigger button isn't testId-tagged;
    // we click via the dialog's trigger (the only "+ user-plus" button on
    // this page).
    const addBtn = page.locator('button:has(.tabler-user-plus)').first()
    await expect(addBtn, 'sibling-add CTA must be visible for an active sub').toBeVisible({ timeout: 10_000 })
    await addBtn.click()
    await expect(page.getByTestId('sibling-dialog')).toBeVisible()

    // Provision a real second student so the backend's sibling-link
    // validation can resolve a Marten doc. We use the same email
    // strategy as provisionFreshStudent but skip the SPA login since
    // we only need the studentId. Important: on-first-sign-in pushes
    // role/tenant claims into the user's customAttributes, but the
    // pre-existing idToken does NOT carry them — so we re-issue the
    // token AFTER the bootstrap call before reading /api/me.
    const sibEmail = `e2e-sibling-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@cena.test`
    const sibPwd = `e2e-${Math.random().toString(36).slice(2, 10)}`
    await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email: sibEmail, password: sibPwd, returnSecureToken: true } },
    )
    const sibPreToken = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email: sibEmail, password: sibPwd, returnSecureToken: true } },
    )
    const { idToken: sibPreIdToken } = await sibPreToken.json() as { idToken: string }
    const sibBootstrap = await page.request.post('/api/auth/on-first-sign-in', {
      headers: { Authorization: `Bearer ${sibPreIdToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'Sibling E2E' },
    })
    expect(
      sibBootstrap.status(),
      'sibling on-first-sign-in must succeed',
    ).toBe(200)

    // Re-issue idToken so the JWT carries the freshly pushed customClaims
    // (role=STUDENT, tenant_id, school_id). Without this /api/me returns
    // 401 (no role claim) and the destructured studentId is undefined,
    // which then crashes the dialog .fill() below.
    const sibTokenResp = await page.request.post(
      `http://${EMU_HOST}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email: sibEmail, password: sibPwd, returnSecureToken: true } },
    )
    const { idToken: sibIdToken } = await sibTokenResp.json() as { idToken: string }

    const sibMe = await page.request.get('/api/me', {
      headers: { Authorization: `Bearer ${sibIdToken}` },
    })
    expect(
      sibMe.status(),
      `sibling /api/me must return 200 after fresh idToken; got ${sibMe.status()}`,
    ).toBe(200)
    const sibMeBody = await sibMe.json() as { studentId?: string }
    expect(sibMeBody.studentId, 'sibling /api/me must carry studentId').toBeTruthy()
    const siblingStudentId = sibMeBody.studentId!

    // Type the sibling's studentId into the dialog text field. The
    // VTextField wraps a real <input>; click+type on the inner input.
    await page.getByTestId('sibling-dialog').locator('input[type="text"]').first().fill(siblingStudentId)

    const linkResp = page.waitForResponse(
      r => r.url().includes('/api/me/subscription/siblings')
        && r.request().method() === 'POST',
      { timeout: 15_000 },
    )
    await page.getByTestId('sibling-confirm').click()
    const linkResolved = await linkResp
    expect(
      linkResolved.status(),
      `POST /api/me/subscription/siblings must succeed; got ${linkResolved.status()}: ${await linkResolved.text().catch(() => '<unreadable>')}`,
    ).toBeGreaterThanOrEqual(200)
    expect(linkResolved.status()).toBeLessThan(300)

    // Read-model boundary: linkedStudentCount goes 1 → 2.
    const statusResp = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${acct.idToken}` },
    })
    const status = await statusResp.json() as { linkedStudentCount: number }
    expect(
      status.linkedStudentCount,
      'linkedStudentCount must increment after sibling link',
    ).toBeGreaterThanOrEqual(2)

    testInfo.attach('console-entries.json', { body: JSON.stringify(consoleEntries, null, 2), contentType: 'application/json' })
    testInfo.attach('failed-requests.json', { body: JSON.stringify(failedRequests, null, 2), contentType: 'application/json' })
    expect(pageErrors).toEqual([])
  })

  // ─────────────────────────────────────────────────────────────────────
  // B-02 declined card and B-08 bank transfer are NOT in this file:
  //
  //   B-02 declined: the dev sandbox's POST /api/me/subscription/activate
  //   has no "decline" mode — it always succeeds. A real declined-path
  //   test needs either a sandbox flag or a real Stripe test-mode
  //   declined card token. Tracked as a backend gap; queue when ready.
  //
  //   B-08 bank transfer: /api/me/subscription/bank-transfer/reserve
  //   exists but the dev container does not configure
  //   `BankTransfer:PayeeDetails`. Per the endpoint's own fail-loud
  //   contract (no-stubs memory), it returns 503 in dev. A test would
  //   either need a dev payee-config seed or to mock the options
  //   service — both feel like wrong shortcuts. Tracked in
  //   REPORT-EPIC-B.md.
  // ─────────────────────────────────────────────────────────────────────
})
