// =============================================================================
// Cena Platform — StripeRefundGatewayService (EPIC-PRR-I PRR-306 prod)
//
// Production refund adapter: translates a domain RefundIntent into a
// Stripe RefundService call. Stripe's native idempotency header is
// threaded through so provider retries with the same key return the
// same receipt. API secret lives in StripeOptions.SecretKey — NEVER
// hardcoded, NEVER committed.
//
// Why a thin wrapper: keeping Stripe SDK types out of the application
// layer preserves the port-and-adapter pattern — RefundService only
// knows about IRefundGatewayService. This matches the Stripe checkout
// provider pattern (StripeCheckoutSessionProvider implements
// ICheckoutSessionProvider without leaking Stripe.Session to callers).
// =============================================================================

using Stripe;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Stripe-backed refund adapter. Uses <see cref="RefundService"/> with
/// the <c>Charge</c> field set to the original charge id and
/// <c>Amount</c> in agorot (Stripe uses the smallest currency unit).
/// Idempotency key is threaded via <see cref="RequestOptions"/>.
/// </summary>
public sealed class StripeRefundGatewayService : IRefundGatewayService
{
    private readonly StripeOptions _options;

    /// <summary>Construct with the bound Stripe options (SecretKey required).</summary>
    public StripeRefundGatewayService(StripeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException(
                "StripeOptions.SecretKey is required to issue refunds.",
                nameof(options));
        }
    }

    /// <inheritdoc />
    public string Name => "stripe";

    /// <inheritdoc />
    public async Task<RefundGatewayResult> RefundAsync(
        RefundIntent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (string.IsNullOrWhiteSpace(intent.IdempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key required.", nameof(intent));
        }
        if (string.IsNullOrWhiteSpace(intent.OriginalPaymentTransactionIdEncrypted))
        {
            return RefundGatewayResult.Failure(
                "missing_original_txn_id");
        }

        // Stripe SDK client bound per-call with the secret key; this avoids
        // a process-wide `StripeConfiguration.ApiKey = ...` which would
        // couple the whole AppDomain to a single account. The SDK supports
        // per-call authentication via RequestOptions.ApiKey.
        var refundService = new global::Stripe.RefundService();
        var createOptions = new RefundCreateOptions
        {
            // OriginalPaymentTransactionIdEncrypted is the opaque Stripe
            // charge/session id we stored at activation. Stripe's SDK
            // accepts either a PaymentIntent id (pi_...) or a Charge id
            // (ch_...); we carry whichever our checkout flow stored.
            Amount = intent.Amount.Amount,
            Reason = MapReason(intent.Reason),
        };
        // Prefer PaymentIntent when the id looks like a PI, otherwise
        // fall back to Charge. Both branches are valid Stripe inputs.
        if (intent.OriginalPaymentTransactionIdEncrypted.StartsWith("pi_", StringComparison.Ordinal))
        {
            createOptions.PaymentIntent = intent.OriginalPaymentTransactionIdEncrypted;
        }
        else
        {
            createOptions.Charge = intent.OriginalPaymentTransactionIdEncrypted;
        }

        try
        {
            var requestOpts = new RequestOptions
            {
                ApiKey = _options.SecretKey,
                IdempotencyKey = intent.IdempotencyKey,
            };
            var refund = await refundService
                .CreateAsync(createOptions, requestOpts, ct)
                .ConfigureAwait(false);
            return RefundGatewayResult.Success(refund.Id);
        }
        catch (StripeException ex)
        {
            // Map Stripe's error code to a stable reason string for the
            // domain event. The full StripeException is already logged
            // by the surrounding request scope; we surface the code here
            // so the event reflects provider truth.
            var code = ex.StripeError?.Code ?? "stripe:refund_failed";
            return RefundGatewayResult.Failure(code);
        }
    }

    private static string MapReason(string domainReason) =>
        domainReason switch
        {
            "abuse_diagnostic_uploads" or "abuse_hint_requests"
                => "fraudulent",   // protects our chargeback ratio
            _ => "requested_by_customer",
        };
}
