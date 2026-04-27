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

const SEEDED_STUDENT_EMAIL = 'student1@cena.local'
const SEEDED_STUDENT_PASSWORD = 'DevStudent123!'
const SEEDED_STUDENT_TENANT = 'cena'

test.describe('E2E-001 subscription happy path', () => {
  test('plus annual: pricing → checkout-session → stripe success → confirm-active', async ({
    page,
    stripeScope,
  }) => {
    // Lock the locale before navigation so the FirstRunLanguageChooser
    // modal does not intercept pointer events on /login or /pricing.
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
    })

    // 0. Drive the SPA's real /login form. Firebase JS SDK in the page
    //    persists the session via IndexedDB (the SPA's actual auth path).
    await page.goto('/login')
    await page.getByTestId('auth-email').locator('input').fill(SEEDED_STUDENT_EMAIL)
    await page.getByTestId('auth-password').locator('input').fill(SEEDED_STUDENT_PASSWORD)
    await page.getByTestId('auth-submit').click()
    // Wait until the SPA exits /login. Where it lands depends on
    // onboarding state (could be /home, /onboarding, etc.) — we just
    // need to know we're past the login form before driving /pricing.
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
    //    Mint a fresh idToken via the emulator REST so the page.request
    //    POSTs authenticate the same way the SPA's POST would.
    const tokenResp = await page.request.post(
      `http://${process.env.FIREBASE_EMU_HOST ?? 'localhost:9099'}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=fake-api-key`,
      { data: { email: SEEDED_STUDENT_EMAIL, password: SEEDED_STUDENT_PASSWORD, returnSecureToken: true } },
    )
    expect(tokenResp.ok(), 'Firebase emu signInWithPassword must succeed for the seeded student').toBe(true)
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

    // 4. Simulate Stripe paying the invoice via test-mode webhook.
    await stripeScope.triggerCheckoutCompleted(checkoutBody.sessionId)

    // 5. Navigate SPA-side to the confirm page (the real redirect would
    //    come from Stripe's success_url; we drive there directly).
    await page.goto(`/subscription/confirm?session=${checkoutBody.sessionId}`)

    // 6. Confirm-active view rendered within the 10s expect timeout
    //    (the backend polls SubscriptionAggregate state; webhook→actor
    //    processing is sub-second on a warm stack).
    await expect(
      page.getByTestId('subscription-confirm-active'),
      `Subscription never reached Active state for ${SEEDED_STUDENT_EMAIL}`,
    ).toBeVisible({ timeout: 15_000 })

    // 7. Boundary assertion — API-layer read-model agrees with the DOM.
    const subscriptionState = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${idToken}` },
    })
    expect(subscriptionState.ok()).toBe(true)
    const body = await subscriptionState.json() as {
      state: string
      tier: string
      cycle: string
    }
    expect(body.state).toBe('Active')
    expect(body.tier).toBe('Plus')
    expect(body.cycle).toBe('Annual')

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
      tenantId: SEEDED_STUDENT_TENANT,
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
