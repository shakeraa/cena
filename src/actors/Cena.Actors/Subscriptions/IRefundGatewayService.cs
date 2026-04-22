// =============================================================================
// Cena Platform — IRefundGatewayService (EPIC-PRR-I PRR-306)
//
// Payment-provider-agnostic refund abstraction. The existing
// IPaymentGateway covers authorize-and-capture (Activation, Renewal,
// SiblingAddon) but does not cover refunds because the retail checkout
// path is ICheckoutSessionProvider-based (Stripe hosts the session) and
// therefore the authorize half of a charge/refund pair never flows
// through IPaymentGateway. Refunds on the other hand always go through
// the provider's server-to-server API (Stripe.RefundService,
// Bit.RefundEndpoint, PayBox./v3/charges/refund). This file defines the
// shared shape so the RefundService is decoupled from any one provider
// and the test harness can bind a deterministic sandbox.
//
// Idempotency: callers pass a stable key built from the refund event id
// (the SubscriptionRefunded_V1 stream-level dedup); provider retries
// with the same key return the same receipt without double-refunding.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Input to <see cref="IRefundGatewayService.RefundAsync"/>.</summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent id.</param>
/// <param name="OriginalPaymentTransactionIdEncrypted">
/// The encrypted gateway txn id from the activation or most-recent
/// renewal — Stripe refunds a specific charge; our aggregate holds the
/// encrypted reference so we can forward it opaquely.
/// </param>
/// <param name="Amount">Gross VAT-inclusive refund amount.</param>
/// <param name="IdempotencyKey">Stable key. Required.</param>
/// <param name="Reason">Machine-readable reason (never free-text PII).</param>
public sealed record RefundIntent(
    string ParentSubjectIdEncrypted,
    string OriginalPaymentTransactionIdEncrypted,
    Money Amount,
    string IdempotencyKey,
    string Reason);

/// <summary>Result from <see cref="IRefundGatewayService.RefundAsync"/>.</summary>
public sealed record RefundGatewayResult(
    bool Succeeded,
    string? RefundTransactionId,
    string? FailureReason)
{
    /// <summary>Construct a success result with the provider's refund id.</summary>
    public static RefundGatewayResult Success(string refundTxnId) =>
        new(true, refundTxnId, null);

    /// <summary>Construct a failure result with a stable reason string.</summary>
    public static RefundGatewayResult Failure(string reason) =>
        new(false, null, reason);
}

/// <summary>
/// Payment-provider refund port. Implementations: StripeRefundGatewayService
/// (prod when Stripe is configured), SandboxRefundGatewayService (dev/test,
/// deterministic success/failure based on idempotency key prefix).
/// </summary>
public interface IRefundGatewayService
{
    /// <summary>Gateway name for metrics and audit logs (e.g., "stripe", "sandbox").</summary>
    string Name { get; }

    /// <summary>
    /// Issue a refund against an earlier charge. MUST be idempotent by
    /// <see cref="RefundIntent.IdempotencyKey"/>: the same key produces
    /// the same result without double-refunding.
    /// </summary>
    Task<RefundGatewayResult> RefundAsync(RefundIntent intent, CancellationToken ct);
}
