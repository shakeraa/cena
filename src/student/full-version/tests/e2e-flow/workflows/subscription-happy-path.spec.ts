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
import { probeSubscription } from '../probes/db-probe'

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

    // Lock the locale before navigation so the FirstRunLanguageChooser
    // modal does not intercept pointer events on the pricing-cycle toggle
    // and tier cards. Same pattern student-register.spec.ts uses.
    await page.addInitScript(() => {
      window.localStorage.setItem(
        'cena-student-locale',
        JSON.stringify({ code: 'en', locked: true, version: 1 }),
      )
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
    // Click the CTA button inside the Plus card. The card's outer wrapper
    // is informational only — only `tier-card-plus-cta` fires the
    // `select` emit that triggers /api/me/subscription/checkout-session.
    await page.getByTestId('tier-card-plus-cta').click()

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

    // 7. Boundary assertion — API-layer read-model agrees with the DOM.
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

    // 8. DB boundary — PRR-436 admin test probe reads the canonical
    // SubscriptionAggregate state directly, not the read-model. Catches
    // bugs where the read-model lies about aggregate state (e.g. caches
    // stale projections during the webhook race window).
    const probed = await probeSubscription({
      tenantId: tenant.id,
      parentSubjectId: authUser.uid,
    })
    expect(probed.found,
      `db-probe must find SubscriptionAggregate for parentSubjectId=${authUser.uid}`,
    ).toBe(true)
    expect(probed.data?.status).toBe('Active')
    expect(probed.data?.tier).toBe('Plus')
    expect(probed.data?.cycle).toBe('Annual')
  })
})
