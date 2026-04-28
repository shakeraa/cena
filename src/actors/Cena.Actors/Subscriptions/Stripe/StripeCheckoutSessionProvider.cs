// =============================================================================
// Cena Platform — StripeCheckoutSessionProvider (EPIC-PRR-I PRR-301, ADR-0053)
//
// Real Stripe Checkout Session creation. Session metadata embeds the Cena
// parent + student ids so the webhook handler can reconstruct the activation
// command without a secondary lookup.
//
// Idempotency: Stripe SDK supports Idempotency-Key headers — we pass the
// caller's IdempotencyKey through so replays return the same session.
//
// Session lifetime: default 24h expiry (Stripe default); we don't override
// because the Cena UI should redirect immediately after creation.
// =============================================================================

using Cena.Actors.Subscriptions;
using Stripe;
using Stripe.Checkout;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Stripe-backed <see cref="ICheckoutSessionProvider"/>. Constructs a Checkout
/// Session with subscription mode + the correct price id for the requested
/// (tier, cycle).
/// </summary>
public sealed class StripeCheckoutSessionProvider : ICheckoutSessionProvider
{
    private readonly StripeOptions _options;
    private readonly StripePriceResolver _priceResolver;
    private readonly SessionService _sessionService;

    public StripeCheckoutSessionProvider(
        StripeOptions options,
        StripePriceResolver priceResolver,
        SessionService? sessionService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _priceResolver = priceResolver ?? throw new ArgumentNullException(nameof(priceResolver));

        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Stripe is not fully configured — SecretKey, WebhookSigningSecret, and all six retail PriceIds are required.");
        }

        // Allow injection for tests; default uses the configured secret key.
        _sessionService = sessionService ?? new SessionService(new StripeClient(_options.SecretKey));
    }

    /// <inheritdoc/>
    public string Name => "stripe";

    /// <inheritdoc/>
    public async Task<CheckoutSessionResult> CreateSessionAsync(
        CheckoutSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey is required.", nameof(request));
        }

        var priceId = _priceResolver.Resolve(request.Tier, request.Cycle);

        var sessionMetadata = new Dictionary<string, string>
        {
            ["cena_parent_id"] = request.ParentSubjectIdEncrypted,
            ["cena_primary_student_id"] = request.PrimaryStudentSubjectIdEncrypted,
            ["cena_tier"] = request.Tier.ToString(),
            ["cena_cycle"] = request.Cycle.ToString(),
        };
        var subscriptionMetadata = new Dictionary<string, string>
        {
            ["cena_parent_id"] = request.ParentSubjectIdEncrypted,
            ["cena_primary_student_id"] = request.PrimaryStudentSubjectIdEncrypted,
            ["cena_tier"] = request.Tier.ToString(),
            ["cena_cycle"] = request.Cycle.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(request.DiscountAssignmentId))
        {
            // Carry the discount-assignment id via both session-level and
            // subscription-level metadata so the webhook handler can
            // redeem the correct assignment when Stripe carries the
            // metadata back on checkout.session.completed and on
            // customer.subscription.created.
            sessionMetadata["cena_discount_assignment_id"] = request.DiscountAssignmentId;
            subscriptionMetadata["cena_discount_assignment_id"] = request.DiscountAssignmentId;
        }

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 },
            },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ClientReferenceId = request.ParentSubjectIdEncrypted,
            Metadata = sessionMetadata,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = subscriptionMetadata,
            },
        };

        // Discount attachment: Stripe accepts ONE coupon per subscription;
        // per the LOCKED stacking decision the per-user discount overrides
        // any sibling-discount the user might also be entitled to. Sibling
        // discount is computed at price-resolution time (line-item level),
        // not via Stripe Coupon, so the two paths don't fight here.
        if (!string.IsNullOrWhiteSpace(request.PromotionCodeId))
        {
            options.Discounts = new List<SessionDiscountOptions>
            {
                new() { PromotionCode = request.PromotionCodeId },
            };
        }

        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };
        var session = await _sessionService.CreateAsync(options, requestOptions, ct);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException(
                "Stripe returned a session with no redirect URL — investigate SDK version.");
        }
        return new CheckoutSessionResult(CheckoutUrl: session.Url, SessionId: session.Id);
    }
}
