// =============================================================================
// Cena Platform — ICheckoutSessionProvider (EPIC-PRR-I PRR-301, ADR-0053)
//
// Port for "start a subscription via a hosted-checkout flow". Stripe Checkout
// Session is the canonical production shape; the sandbox impl returns a fake
// URL for dev/test. Distinct from IPaymentGateway (synchronous authorize)
// because real gateways are async-by-webhook, not authorize-and-return.
//
// Flow:
//   1. Parent clicks "Subscribe" in UI
//   2. Frontend calls POST /api/me/subscription/checkout-session
//   3. Backend creates a session via ICheckoutSessionProvider
//   4. Frontend redirects parent to CheckoutUrl
//   5. Parent pays at gateway-hosted page
//   6. Gateway fires webhook → SubscriptionWebhookHandler → SubscriptionActivated_V1
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Input to <see cref="ICheckoutSessionProvider.CreateSessionAsync"/>.
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent id.</param>
/// <param name="PrimaryStudentSubjectIdEncrypted">
/// Encrypted primary student id; embedded in session metadata so the webhook
/// can reconstruct the activation command.
/// </param>
/// <param name="Tier">Target tier.</param>
/// <param name="Cycle">Billing cycle (Monthly or Annual).</param>
/// <param name="IdempotencyKey">
/// Gateway idempotency key — replaying with the same key returns the same
/// session (never double-creates a session).
/// </param>
/// <param name="SuccessUrl">Where the gateway redirects on payment success.</param>
/// <param name="CancelUrl">Where the gateway redirects on cancellation.</param>
public sealed record CheckoutSessionRequest(
    string ParentSubjectIdEncrypted,
    string PrimaryStudentSubjectIdEncrypted,
    SubscriptionTier Tier,
    BillingCycle Cycle,
    string IdempotencyKey,
    string SuccessUrl,
    string CancelUrl);

/// <summary>
/// Created checkout session. <see cref="CheckoutUrl"/> is the redirect target;
/// <see cref="SessionId"/> is the gateway-side session id (retained for audit).
/// </summary>
public sealed record CheckoutSessionResult(string CheckoutUrl, string SessionId);

/// <summary>Port for starting a subscription via hosted checkout.</summary>
public interface ICheckoutSessionProvider
{
    /// <summary>Adapter name for metrics/audit ("stripe" | "sandbox" | ...).</summary>
    string Name { get; }

    /// <summary>Create a checkout session and return its redirect URL.</summary>
    Task<CheckoutSessionResult> CreateSessionAsync(CheckoutSessionRequest request, CancellationToken ct);
}
