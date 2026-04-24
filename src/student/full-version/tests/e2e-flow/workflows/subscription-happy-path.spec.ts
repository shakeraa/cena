// =============================================================================
// TASK-E2E-001 — subscription happy path
//
// Journey: login → /pricing → select Plus annual → checkout-session POST →
// simulate Stripe success via test-mode webhook → SPA lands on
// /subscription/confirm with the active-state view.
//
// This spec is the flagship spike: it proves the isolation model
// (per-worker tenant, fresh Firebase user, Stripe-metadata-scoped webhooks)
// end-to-end. Boundary assertions (DB / bus) land in the follow-up pass
// once the admin test-probe endpoint (PRR-436) is wired. For now, assert
// DOM + HTTP response shape — the two surfaces that are CI-safe today.
// =============================================================================

import { e2eTest as test, expect, tenantDebugString } from '../fixtures/tenant'

test.describe('E2E-001 subscription happy path', () => {
  test('plus annual: pricing → checkout-session → stripe success → confirm-active', async ({
    page,
    tenant,
    authUser,
    stripeScope,
  }, testInfo) => {
    testInfo.annotations.push({
      type: 'isolation',
      description: tenantDebugString(tenant, page),
    })

    // 1. Pricing page renders + tier cards visible
    await page.goto('/pricing')
    await expect(page.getByTestId('pricing-page')).toBeVisible()
    await expect(page.getByTestId('pricing-cycle-toggle')).toBeVisible()

    // 2. Select annual cycle
    await page.getByTestId('pricing-cycle-annual').click()

    // 3. Intercept the checkout-session POST before the browser follows
    //    the Stripe redirect. We want the raw sessionId so we can drive
    //    the webhook ourselves instead of driving Stripe's hosted UI.
    const checkoutRequest = page.waitForResponse(
      resp => resp.url().includes('/api/me/subscription/checkout-session')
        && resp.request().method() === 'POST',
    )

    // Block navigation to Stripe — we'll simulate the outcome ourselves.
    await page.route('**://checkout.stripe.com/**', route => route.abort('aborted'))

    // Click Plus tier (tier ids match the CheckoutSessionInput contract).
    await page.getByTestId('tier-card-plus').click()

    const checkoutResp = await checkoutRequest
    expect(
      checkoutResp.status(),
      'POST /api/me/subscription/checkout-session must return 200 + checkoutSessionId',
    ).toBe(200)
    const checkoutBody = await checkoutResp.json() as {
      checkoutSessionId: string
      url: string
    }
    expect(checkoutBody.checkoutSessionId).toBeTruthy()
    expect(checkoutBody.url).toContain('checkout.stripe.com')

    // 4. Simulate Stripe paying the invoice via test-mode webhook.
    await stripeScope.triggerCheckoutCompleted(checkoutBody.checkoutSessionId)

    // 5. Navigate SPA-side to the confirm page (the real redirect would
    //    come from Stripe's success_url; we land there directly since
    //    we blocked the hosted page).
    await page.goto(`/subscription/confirm?session=${checkoutBody.checkoutSessionId}`)

    // 6. Confirm-active view rendered within the 10s expect timeout
    //    (the backend polls SubscriptionAggregate state; webhook→actor
    //    processing is sub-second on a warm stack).
    await expect(
      page.getByTestId('subscription-confirm-active'),
      `Subscription never reached Active state for ${authUser.email} / ${tenant.id}`,
    ).toBeVisible({ timeout: 15_000 })

    // 7. Boundary assertion — the SPA's /api/me/subscription read-model
    //    agrees with the DOM. (DB + bus assertions land in the PRR-436
    //    follow-up once /api/admin/test/probe exists.)
    const subscriptionState = await page.request.get('/api/me/subscription', {
      headers: { Authorization: `Bearer ${authUser.idToken}` },
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
  })
})
