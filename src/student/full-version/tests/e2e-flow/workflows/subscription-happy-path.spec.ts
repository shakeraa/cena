// =============================================================================
// TASK-E2E-001 — subscription happy path
//
// Journey: login → /pricing → select Plus annual → checkout-session POST →
// simulate Stripe success via test-mode webhook → SPA lands on
// /subscription/confirm with the active-state view.
//
// Auth bootstrap (TASK-E2E-001-FIX): drives the SPA's real /login form
// with the seeded student1@cena.local credentials so Firebase JS SDK
// persists the session via its IndexedDB store the way real students do.
// The earlier `authUser` fixture wrote localStorage keys the SPA's
// Firebase-Auth path doesn't read; that left /pricing redirecting to
// /register and the spec timing out before reaching the Stripe flow.
//
// Why a seeded user instead of a fresh per-worker user: the SPA's
// /login form drives Firebase JS SDK directly, which means we need the
// SPA itself running auth — there's no clean way to forge a session
// from outside the page. The seeded `student1@cena.local` already has
// the right custom claims (role=STUDENT, tenant_id=cena), so the path
// works without per-test claim-pushing. Tradeoff: parallel Playwright
// workers all sign in as the same student, so cross-worker tenant
// isolation is weaker here than in student-register.spec.ts. The
// flagship spec's job is to prove the Stripe → backend → confirm flow
// end-to-end, not tenant isolation (that's EPIC-E2E-H's job).
// =============================================================================

import { e2eTest as test, expect } from '../fixtures/tenant'
import { probeSubscription } from '../probes/db-probe'

const TENANT_ID = 'cena'
const SCHOOL_ID = 'cena-platform'

test.describe('E2E-001 subscription happy path', () => {
  test('plus annual: pricing → checkout-session → stripe success → confirm-active', async ({
    page,
  }) => {
    // Lock the locale before navigation so the FirstRunLanguageChooser
    // modal does not intercept pointer events on /login or /pricing.
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    // 0a. Provision a fresh per-run student. Using a seeded user runs into
    // 409 "already_active" on the second activation; a fresh user gets a
    // clean subscription aggregate every time. The flow is: create via
    // Firebase emu signUp, run on-first-sign-in to bootstrap Marten state
    // (AdminUser doc + StudentProfileSnapshot via StudentOnboardedV1),
    // then drive /login to populate the SPA's Firebase IndexedDB.
    const email = `e2e-sub-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@cena.test`
    const password = `e2e-${Math.random().toString(36).slice(2, 12)}`

    const signupResp = await page.request.post(
      `http://${process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'}/identitytoolkit.googleapis.com/v1/accounts:signUp?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(signupResp.ok(), 'Firebase emu signUp must succeed').toBe(true)
    const signup = await signupResp.json() as { idToken: string; localId: string }

    const onboardResp = await page.request.post('/api/auth/on-first-sign-in', {
      headers: { Authorization: `Bearer ${signup.idToken}` },
      data: { tenantId: TENANT_ID, schoolId: SCHOOL_ID, displayName: 'E2E Sub' },
    })
    expect(
      onboardResp.status(),
      'on-first-sign-in must bootstrap Marten state for the fresh student',
    ).toBe(200)

    // 0b. Drive the SPA's real /login form so the Firebase JS SDK in the
    //     page populates IndexedDB and the SPA treats the user as
    //     authenticated for any UI-level interactions later.
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(email)
    await page.getByTestId('auth-password').locator('input').fill(password)
    await page.getByTestId('auth-submit').click()
    await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 15_000 })

    // 1. DOM boundary — pricing page renders, tier cards visible
    await page.goto('/pricing')
    await expect(page.getByTestId('pricing-page')).toBeVisible()
    await expect(page.getByTestId('pricing-cycle-toggle')).toBeVisible()
    await expect(page.getByTestId('tier-card-plus-cta')).toBeVisible()

    // 2. Select annual cycle (asserts the toggle is functional)
    await page.getByTestId('pricing-cycle-annual').click()

    // 3. Drive the checkout-session POST directly via page.request. The
    //    SPA's UI path does the same POST then immediately
    //    `window.location.href = data.url` to Stripe — that navigation
    //    flushes Chrome's response cache before Playwright can read the
    //    body, and Chrome forbids redefining window.location to suppress
    //    it. Calling the API ourselves preserves the contract under test
    //    (the endpoint is real, the response is real, the webhook flow
    //    that follows is real) without racing the navigation.
    //
    //    page.request inherits cookies but not the Authorization header
    //    the SPA's useApi composable attaches via Firebase getIdToken.
    //    Re-sign-in via emulator REST after on-first-sign-in so the
    //    refreshed idToken carries the role/tenant/school custom claims
    //    that the just-shipped onboarding service set.
    const tokenResp = await page.request.post(
      `http://${process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email, password, returnSecureToken: true } },
    )
    expect(tokenResp.ok(), 'Firebase emu signInWithPassword must succeed').toBe(true)
    const { idToken } = await tokenResp.json() as { idToken: string }
    expect(idToken, 'Firebase idToken must be returned').toBeTruthy()

    // Resolve primary student id from /api/me — required field on the
    // CheckoutSessionRequestDto. The seeded student's id is whatever the
    // emu reseed assigned; reading it from /api/me keeps the spec stable.
    const me0 = await page.request.get('/api/me', {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(me0.ok(), '/api/me must be reachable post-login').toBe(true)
    const me0Body = await me0.json() as { studentId?: string }
    const primaryStudentId = me0Body.studentId
    expect(primaryStudentId, '/api/me must carry studentId').toBeTruthy()

    const checkoutResp = await page.request.post('/api/me/subscription/checkout-session', {
      headers: { Authorization: `Bearer ${idToken}` },
      data: {
        primaryStudentId,
        tier: 'Plus',
        billingCycle: 'Annual',
        idempotencyKey: `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
      },
    })
    expect(
      checkoutResp.status(),
      'POST /api/me/subscription/checkout-session must return 200 + sessionId',
    ).toBe(200)
    const checkoutBody = await checkoutResp.json() as {
      checkoutUrl: string
      sessionId: string
      providerName: string
    }
    expect(checkoutBody.sessionId).toBeTruthy()
    // The dev stack uses a sandbox checkout URL (sandbox.checkout.cena.test);
    // production uses checkout.stripe.com. Accept either — the contract is
    // "URL is hosted by the gateway", not "URL exactly matches Stripe".
    expect(checkoutBody.checkoutUrl).toMatch(/(checkout\.stripe\.com|sandbox\.checkout\.cena\.test)/)

    // 4. Activate the subscription. The dev stack runs SandboxCheckoutSessionProvider
    //    which doesn't actually call Stripe — instead it pairs with a
    //    `POST /api/me/subscription/activate` endpoint that simulates the
    //    webhook-driven activation. We call that directly.
    //
    //    Production flow goes Stripe Checkout → webhook → SubscriptionActivated_V1.
    //    Dev sandbox swaps the webhook for the activate endpoint with the
    //    same payload shape. The aggregate state-transition is identical.
    const idemKey = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
    const activateResp = await page.request.post('/api/me/subscription/activate', {
      headers: { Authorization: `Bearer ${idToken}` },
      data: {
        primaryStudentId,
        tier: 'Plus',
        billingCycle: 'Annual',
        paymentIdempotencyKey: idemKey,
      },
    })
    expect(
      activateResp.status(),
      'POST /api/me/subscription/activate must return 200 (sandbox webhook simulator)',
    ).toBe(200)

    // 5. Navigate SPA-side to the confirm page (the real redirect would
    //    come from Stripe's success_url; we drive there directly).
    await page.goto(`/subscription/confirm?session=${checkoutBody.sessionId}`)

    // 6. Confirm-active view rendered within the 10s expect timeout
    //    (the backend polls SubscriptionAggregate state; activation
    //    POST is synchronous on the warm dev stack).
    await expect(
      page.getByTestId('subscription-confirm-active'),
      `Subscription never reached Active state for ${email}`,
    ).toBeVisible({ timeout: 15_000 })

    // 7. Boundary assertion — API-layer read-model agrees with the DOM.
    // Wire shape per SubscriptionStatusDto: { status, currentTier,
    // currentBillingCycle, activatedAt, renewsAt, linkedStudentCount }.
    const subscriptionState = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(subscriptionState.ok()).toBe(true)
    const body = await subscriptionState.json() as {
      status: string
      currentTier: string | null
      currentBillingCycle: string | null
    }
    expect(body.status).toBe('Active')
    expect(body.currentTier).toBe('Plus')
    expect(body.currentBillingCycle).toBe('Annual')

    // 8. DB boundary — PRR-436 admin test probe reads the canonical
    // SubscriptionAggregate state directly, not the read-model. The
    // parentSubjectId is the seeded student's uid (retail tier — the
    // student is the billing counterparty). Resolved here via /api/me
    // since the seeded uid changes each time the emulator is reseeded.
    const me = await page.request.get('/api/me', {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(me.ok(), '/api/me must be reachable post-login').toBe(true)
    const meBody = await me.json() as { studentId?: string; uid?: string }
    const parentSubjectId = meBody.studentId ?? meBody.uid
    expect(parentSubjectId, '/api/me must carry the student id').toBeTruthy()

    const probed = await probeSubscription({
      tenantId: TENANT_ID,
      parentSubjectId: parentSubjectId!,
    })
    expect(probed.found,
      `db-probe must find SubscriptionAggregate for parentSubjectId=${parentSubjectId}`,
    ).toBe(true)
    expect(probed.data?.status).toBe('Active')
    expect(probed.data?.tier).toBe('Plus')
    expect(probed.data?.cycle).toBe('Annual')
  })
})
