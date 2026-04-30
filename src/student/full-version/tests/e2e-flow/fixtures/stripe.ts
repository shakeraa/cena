// =============================================================================
// Cena E2E flow — Stripe test-mode helpers
//
// Drives Stripe's test-mode without navigating the hosted checkout UI
// (which is slow + external-dependent). Instead:
//   * Intercept the POST /api/me/subscription/checkout-session response
//   * Instead of redirecting the browser to checkout.stripe.com, the spec
//     calls triggerCheckoutCompleted() or triggerPaymentFailed() which
//     posts a test-mode webhook payload to the student-api webhook endpoint.
//
// Reference: scripts/stripe/trigger-events.sh for the CLI-based equivalent.
// =============================================================================

import { spawn } from 'node:child_process'

export interface StripeScope {
  readonly tenantId: string
  /**
   * Trigger a `checkout.session.completed` webhook bound to the given
   * checkoutSessionId (from the POST /checkout-session response) and this
   * scope's tenant metadata. Backend processes it, emits
   * SubscriptionActivated_V1.
   */
  triggerCheckoutCompleted(checkoutSessionId: string): Promise<void>
  /**
   * Trigger a `payment_intent.payment_failed` webhook. Backend moves the
   * CheckoutSession to Abandoned state.
   */
  triggerPaymentFailed(checkoutSessionId: string): Promise<void>
  /**
   * Trigger a `checkout.session.expired` — user dismissed the modal.
   */
  triggerCheckoutExpired(checkoutSessionId: string): Promise<void>
  /**
   * Trigger an `invoice.paid` webhook — drives trial → paid conversion
   * (Phase 3, d9c663e2). Stripe's SetupIntent flow fires this NOT
   * checkout.session.completed at conversion, so a Trialing-state
   * invoice.paid IS the conversion event.
   *
   * Backend reaction (StripeWebhookHandler.HandleInvoicePaidAsync):
   *   - if aggregate.Status == Trialing →
   *     emits TrialConverted_V1 + SubscriptionActivated_V1 atomically
   *   - if aggregate.Status == Active|PastDue →
   *     emits RenewalProcessed_V1
   *   - otherwise no-op
   *
   * Required overrides (passed via `extraMetadata`): cena_parent_id,
   * cena_tier (Basic|Plus|Premium), cena_cycle (Monthly|Annual). The
   * fixture seeds them under subscription_details.metadata since that's
   * the canonical location HandleInvoicePaidAsync reads.
   */
  triggerInvoicePaid(
    checkoutSessionId: string,
    extraMetadata: { parentId: string; tier: string; cycle: string },
  ): Promise<void>
}

const STRIPE_CLI = process.env.STRIPE_CLI ?? 'stripe'

/**
 * Build a StripeScope bound to a specific tenant id. All test-mode events
 * it fires carry `metadata.tenant_id` matching this scope, so backend
 * webhook routing resolves to the correct subscription aggregate.
 */
export function createStripeScope(tenantId: string): StripeScope {
  function trigger(event: string, sessionId: string, extraOverrides: string[] = []): Promise<void> {
    const overrides = [
      `--override=metadata.tenant_id=${tenantId}`,
      `--override=client_reference_id=${sessionId}`,
      ...extraOverrides,
    ]
    return new Promise<void>((resolve, reject) => {
      const proc = spawn(STRIPE_CLI, ['trigger', event, ...overrides], {
        env: { ...process.env, STRIPE_CLI_TELEMETRY_OPTOUT: '1' },
        stdio: ['ignore', 'pipe', 'pipe'],
      })
      const stderr: Buffer[] = []
      proc.stderr?.on('data', (chunk: Buffer) => stderr.push(chunk))
      proc.on('error', err => {
        reject(new Error(
          `Stripe CLI 'trigger' failed to spawn (event=${event}, tenant=${tenantId}): ${err.message}\n`
          + 'Ensure `stripe login` has been run and STRIPE_CLI is on $PATH. '
          + 'See scripts/stripe/bootstrap-sandbox.sh.',
        ))
      })
      proc.on('exit', code => {
        if (code === 0) {
          resolve()
          return
        }
        reject(new Error(
          `Stripe CLI 'trigger' exited ${code} (event=${event}, tenant=${tenantId}): `
          + Buffer.concat(stderr).toString().trim(),
        ))
      })
    })
  }

  return {
    tenantId,
    async triggerCheckoutCompleted(checkoutSessionId: string) {
      await trigger('checkout.session.completed', checkoutSessionId, [
        '--override=mode=subscription',
        '--override=payment_status=paid',
      ])
    },
    async triggerPaymentFailed(checkoutSessionId: string) {
      await trigger('payment_intent.payment_failed', checkoutSessionId, [
        '--override=last_payment_error.code=card_declined',
      ])
    },
    async triggerCheckoutExpired(checkoutSessionId: string) {
      await trigger('checkout.session.expired', checkoutSessionId)
    },
    async triggerInvoicePaid(
      checkoutSessionId: string,
      extraMetadata: { parentId: string; tier: string; cycle: string },
    ) {
      // HandleInvoicePaidAsync looks at subscription_details.metadata
      // first, then invoice.metadata as fallback. Stripe CLI overrides
      // with dotted keys hit the top-level invoice.metadata; setting
      // both via cena_* keys covers either branch.
      await trigger('invoice.paid', checkoutSessionId, [
        `--override=metadata.cena_parent_id=${extraMetadata.parentId}`,
        `--override=metadata.cena_tier=${extraMetadata.tier}`,
        `--override=metadata.cena_cycle=${extraMetadata.cycle}`,
        `--override=subscription_details.metadata.cena_parent_id=${extraMetadata.parentId}`,
        `--override=subscription_details.metadata.cena_tier=${extraMetadata.tier}`,
        `--override=subscription_details.metadata.cena_cycle=${extraMetadata.cycle}`,
      ])
    },
  }
}
