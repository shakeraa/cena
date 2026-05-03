// =============================================================================
// Cena Platform — useCheckoutSession (EPIC-PRR-I PRR-292)
//
// Client for POST /api/me/subscription/checkout-session. Posts the
// (tier, cycle) selection + an idempotency key; receives a Stripe hosted
// Checkout URL; redirects the browser to it. Redirection is the terminal
// step — state from here on lives on Stripe's side until the webhook fires
// SubscriptionActivated_V1 which the user sees on the /subscription/confirm
// page after completing payment.
//
// Idempotency key is generated per request via crypto.randomUUID() and
// cached in sessionStorage for ~5 min so a pricing-page double-click doesn't
// create two Stripe sessions.
// =============================================================================

import { ref } from 'vue'
import { useApi } from './useApi'

/** Input shape for a new checkout session. */
export interface CheckoutSessionInput {
  primaryStudentId: string
  tier: 'Basic' | 'Plus' | 'Premium'
  billingCycle: 'Monthly' | 'Annual'
}

/** Response from the API. */
export interface CheckoutSessionResponse {
  checkoutUrl: string
  sessionId: string
  providerName: string
}

/** How long to keep an idempotency key alive in session storage. */
const IDEMPOTENCY_TTL_MS = 5 * 60 * 1000

/**
 * Build (or retrieve from sessionStorage) an idempotency key for this
 * (tier, cycle) selection. Double-clicks within the TTL reuse the same key,
 * so Stripe returns the same session URL instead of a duplicate.
 */
function idempotencyKeyFor(input: CheckoutSessionInput): string {
  const storageKey = `cena:checkout:idem:${input.tier}:${input.billingCycle}`
  const stored = sessionStorage.getItem(storageKey)
  if (stored) {
    const { key, ts } = JSON.parse(stored) as { key: string; ts: number }
    if (Date.now() - ts < IDEMPOTENCY_TTL_MS) {
      return key
    }
  }
  const key = crypto.randomUUID?.() ?? `fallback-${Date.now()}-${Math.random()}`
  sessionStorage.setItem(storageKey, JSON.stringify({ key, ts: Date.now() }))
  return key
}

/** Composable wrapping the checkout-session POST + redirect. */
export function useCheckoutSession() {
  const submitting = ref(false)
  const error = ref<Error | null>(null)

  /**
   * POST to /api/me/subscription/checkout-session and redirect the browser
   * to the returned URL. This function never returns on the happy path — the
   * page navigates away to Stripe's hosted checkout.
   */
  const startCheckout = async (input: CheckoutSessionInput): Promise<void> => {
    submitting.value = true
    error.value = null
    try {
      const body = {
        primaryStudentId: input.primaryStudentId,
        tier: input.tier,
        billingCycle: input.billingCycle,
        idempotencyKey: idempotencyKeyFor(input),
      }
      const { data, error: fetchError } = await useApi('/me/subscription/checkout-session')
        .post(body)
        .json<CheckoutSessionResponse>()

      if (fetchError.value || !data.value) {
        throw new Error(fetchError.value?.message ?? 'checkout_session_failed')
      }
      if (!data.value.checkoutUrl) {
        throw new Error('empty_checkout_url')
      }
      // Redirect — page unload happens here.
      window.location.assign(data.value.checkoutUrl)
    } catch (e) {
      error.value = e instanceof Error ? e : new Error(String(e))
      submitting.value = false
      throw error.value
    }
  }

  return { startCheckout, submitting, error }
}
