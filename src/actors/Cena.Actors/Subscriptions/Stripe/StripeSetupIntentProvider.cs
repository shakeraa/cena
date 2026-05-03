// =============================================================================
// Cena Platform — StripeSetupIntentProvider (Phase 1C, trial-then-paywall §4.0)
//
// Production adapter for IPaymentMethodSetupProvider. Wraps the Stripe.Net
// SDK's SetupIntentService.
//
// Wire-level mapping (§4.0.1 → IPaymentMethodSetupProvider port):
//
//   Create  : SetupIntents.Create({
//               usage: "off_session",
//               payment_method_types: ["card"],
//               metadata: { cena_parent_id }
//             })
//             with RequestOptions.IdempotencyKey
//
//   Verify  : SetupIntents.Get(id, expand=["payment_method"])
//             so card.fingerprint is hydrated server-side rather than
//             requiring a second PaymentMethods.Retrieve call.
//
// Why per-call ApiKey (RequestOptions.ApiKey) instead of process-wide
// StripeConfiguration.ApiKey: keeps the AppDomain free of single-account
// state — same pattern StripeRefundGatewayService and
// StripeDiscountCouponProvider already use.
//
// Why service injection: tests subclass SetupIntentService and override
// CreateAsync / GetAsync to assert the option payload + idempotency-key
// shape WITHOUT hitting the network — same pattern as
// StripeDiscountCouponProviderTests.
// =============================================================================

using Stripe;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Stripe-backed <see cref="IPaymentMethodSetupProvider"/>. Allows test
/// injection of <see cref="SetupIntentService"/> so unit tests can stub the
/// SDK calls without reaching the network.
/// </summary>
public sealed class StripeSetupIntentProvider : IPaymentMethodSetupProvider
{
    private readonly StripeOptions _options;
    private readonly SetupIntentService _setupIntentService;

    /// <summary>Construct with bound Stripe options (SecretKey required).</summary>
    /// <param name="options">Bound Stripe configuration.</param>
    /// <param name="setupIntentService">
    /// Optional service injection — defaults to a new
    /// <see cref="SetupIntentService"/> bound to the configured secret key.
    /// Tests pass a subclass that overrides Create / Get to avoid network I/O.
    /// </param>
    public StripeSetupIntentProvider(
        StripeOptions options,
        SetupIntentService? setupIntentService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException(
                "StripeOptions.SecretKey is required for the SetupIntent provider.",
                nameof(options));
        }
        _setupIntentService = setupIntentService
            ?? new SetupIntentService(new StripeClient(_options.SecretKey));
    }

    /// <inheritdoc/>
    public string Name => "stripe";

    /// <inheritdoc/>
    public async Task<SetupIntentInitResult> CreateSetupIntentAsync(
        SetupIntentInitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "IdempotencyKey is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ParentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "ParentSubjectIdEncrypted is required.", nameof(request));
        }

        var createOptions = new SetupIntentCreateOptions
        {
            // off_session per §4.0 — the saved payment method is reused at
            // conversion-time when the parent is no longer at the keyboard
            // (Stripe charges from the daemon-driven trial-converted flow).
            Usage = "off_session",
            // Card-only at v1. Other types (sepa_debit, link) can be added
            // later once we have evidence on Israeli market mix.
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = new Dictionary<string, string>
            {
                ["cena_parent_id"] = request.ParentSubjectIdEncrypted,
            },
        };

        var requestOptions = new RequestOptions
        {
            ApiKey = _options.SecretKey,
            IdempotencyKey = request.IdempotencyKey,
        };

        var setupIntent = await _setupIntentService
            .CreateAsync(createOptions, requestOptions, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(setupIntent.ClientSecret))
        {
            throw new InvalidOperationException(
                "Stripe returned a SetupIntent with no client_secret — investigate SDK version.");
        }

        return new SetupIntentInitResult(
            SetupIntentId: setupIntent.Id,
            ClientSecret: setupIntent.ClientSecret,
            Status: MapStatus(setupIntent.Status));
    }

    /// <inheritdoc/>
    public async Task<SetupIntentVerifyResult> VerifyAndExtractFingerprintAsync(
        string setupIntentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(setupIntentId))
        {
            throw new ArgumentException(
                "setupIntentId is required.", nameof(setupIntentId));
        }

        // Expand payment_method so card.fingerprint is in the same response
        // body — single round trip, no follow-up PaymentMethods.Retrieve.
        var getOptions = new SetupIntentGetOptions
        {
            Expand = new List<string> { "payment_method" },
        };
        var requestOptions = new RequestOptions { ApiKey = _options.SecretKey };

        var setupIntent = await _setupIntentService
            .GetAsync(setupIntentId, getOptions, requestOptions, ct)
            .ConfigureAwait(false);

        var status = MapStatus(setupIntent.Status);
        var declineCode = setupIntent.LastSetupError?.DeclineCode;

        if (status == SetupIntentStatus.Succeeded)
        {
            // Per Stripe spec, payment_method.card.fingerprint is stable
            // per-card across emails. This is the value the trial-fingerprint
            // ledger keys on (§5.7).
            var card = setupIntent.PaymentMethod?.Card;
            var fingerprint = card?.Fingerprint;
            var paymentMethodId = setupIntent.PaymentMethod?.Id
                ?? setupIntent.PaymentMethodId;

            // A succeeded SetupIntent without a card fingerprint should not
            // be possible per Stripe's contract; surface as a Failed terminal
            // rather than silently appending a trial event with no anti-abuse
            // signal.
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return new SetupIntentVerifyResult(
                    Status: SetupIntentStatus.Failed,
                    CardFingerprint: null,
                    PaymentMethodId: paymentMethodId,
                    DeclineCode: "fingerprint_missing");
            }

            return new SetupIntentVerifyResult(
                Status: SetupIntentStatus.Succeeded,
                CardFingerprint: fingerprint,
                PaymentMethodId: paymentMethodId,
                DeclineCode: null);
        }

        return new SetupIntentVerifyResult(
            Status: status,
            CardFingerprint: null,
            PaymentMethodId: null,
            DeclineCode: declineCode);
    }

    /// <summary>
    /// Map Stripe's wire-level <c>status</c> to the coarse caller-decidable
    /// <see cref="SetupIntentStatus"/> per the §4.0.1 failure-mode table.
    /// </summary>
    private static SetupIntentStatus MapStatus(string? stripeStatus) =>
        stripeStatus switch
        {
            "succeeded" => SetupIntentStatus.Succeeded,
            "requires_action" => SetupIntentStatus.RequiresAction,
            "requires_confirmation" => SetupIntentStatus.RequiresAction,
            "requires_payment_method" => SetupIntentStatus.RequiresPaymentMethod,
            "processing" => SetupIntentStatus.Pending,
            // canceled / null / anything else → terminal failure.
            _ => SetupIntentStatus.Failed,
        };
}
