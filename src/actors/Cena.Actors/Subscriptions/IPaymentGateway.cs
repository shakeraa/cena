// =============================================================================
// Cena Platform — IPaymentGateway (EPIC-PRR-I PRR-301/302/303, ADR-0053)
//
// Payment-provider-agnostic adapter port. Concrete adapters:
//   - StripePaymentGateway (retail CC, retail annual)
//   - BitPaymentGateway (Israeli P2P-dominant)
//   - PayBoxPaymentGateway (Israeli CC+wallet)
//   - SandboxPaymentGateway (deterministic, in-process, dev/test only)
//
// Adapters translate gateway-specific webhooks and SDK calls into the
// simple command shape that SubscriptionCommands expects. Gateway secrets
// live in configuration (never committed) per CLAUDE.md security rules.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Intent of a payment charge.</summary>
public enum PaymentIntentKind
{
    /// <summary>First payment at subscription activation.</summary>
    Activation = 0,

    /// <summary>Renewal charge at the next cycle boundary.</summary>
    Renewal = 1,

    /// <summary>Sibling add-on prorated charge.</summary>
    SiblingAddon = 2,
}

/// <summary>
/// Input to <see cref="IPaymentGateway.AuthorizeAsync"/>. Amount is gross
/// VAT-inclusive agorot; the gateway is expected to display and charge that
/// exact amount (no client-side math).
/// </summary>
/// <param name="ParentSubjectIdEncrypted">Encrypted parent id.</param>
/// <param name="GrossAmount">Gross amount to charge.</param>
/// <param name="Kind">What the charge is for.</param>
/// <param name="IdempotencyKey">
/// Gateway-idempotency-key; on retry with the same key the gateway returns
/// the same result without double-charging. Required.
/// </param>
public sealed record PaymentIntent(
    string ParentSubjectIdEncrypted,
    Money GrossAmount,
    PaymentIntentKind Kind,
    string IdempotencyKey);

/// <summary>Gateway result shape. Successful results carry a transaction id; failures carry a reason.</summary>
public sealed record PaymentResult(
    bool Succeeded,
    string? TransactionId,
    string? FailureReason)
{
    public static PaymentResult Success(string txn) => new(true, txn, null);
    public static PaymentResult Failure(string reason) => new(false, null, reason);
}

/// <summary>
/// Payment-provider adapter. Every method must be idempotent by
/// <see cref="PaymentIntent.IdempotencyKey"/>: submitting the same intent
/// twice MUST produce the same result, not charge twice.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Gateway name for metrics and audit logs (e.g., "stripe", "bit").</summary>
    string Name { get; }

    /// <summary>
    /// Authorize + capture (or SCA flow) for the given intent. Returns
    /// <see cref="PaymentResult"/> — success carries the gateway's
    /// transaction id; failure carries a reason string suitable for
    /// PaymentFailed_V1.Reason.
    /// </summary>
    Task<PaymentResult> AuthorizeAsync(PaymentIntent intent, CancellationToken ct);
}
