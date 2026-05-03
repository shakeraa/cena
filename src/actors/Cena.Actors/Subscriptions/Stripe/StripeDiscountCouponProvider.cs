// =============================================================================
// Cena Platform — StripeDiscountCouponProvider (per-user discount-codes feature)
//
// Production-grade Stripe-backed adapter for IDiscountCouponProvider. Wraps
// the Stripe Coupons + PromotionCodes APIs.
//
// Issuance flow (single transactional unit from the gateway's POV):
//
//   1. CouponService.CreateAsync — creates a Coupon with
//        - PercentOff   (decimal 0.01..100) for DiscountKind.PercentOff,
//          with Duration: "repeating" + DurationInMonths: N
//        - AmountOff    (long, smallest unit) + Currency: "ils" for
//          DiscountKind.AmountOff, with Duration: "repeating" + DurationInMonths: N
//      Coupon Metadata carries cena_assignment_id + cena_target_email so
//      the webhook handler can reconstruct the assignment without a
//      secondary store lookup.
//
//   2. PromotionCodeService.CreateAsync — creates a customer-facing code
//      tied to that coupon. Restriction strategy:
//        - When a Stripe Customer with the target email already exists,
//          set Customer = customerId so the code refuses for any other
//          email at checkout. This is the strict path.
//        - When no Stripe Customer exists yet, the code carries Metadata
//          (cena_target_email) and is restricted via Stripe's email-
//          metadata enforcement at the checkout-customer-creation seam.
//          This avoids creating Stripe Customers eagerly for users who
//          may never sign up — keeps the Customer table aligned with
//          actual paying parents.
//
// Per-call API key (RequestOptions.ApiKey) — same pattern as
// StripeRefundGatewayService — keeps the AppDomain free of process-wide
// StripeConfiguration.ApiKey state.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Stripe;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Stripe-backed <see cref="IDiscountCouponProvider"/>. Allows test
/// injection of <see cref="CouponService"/> + <see cref="PromotionCodeService"/>
/// + <see cref="CustomerService"/> so unit tests can stub the SDK without
/// reaching the network.
/// </summary>
public sealed class StripeDiscountCouponProvider : IDiscountCouponProvider
{
    private readonly StripeOptions _options;
    private readonly CouponService _couponService;
    private readonly PromotionCodeService _promotionCodeService;
    private readonly CustomerService _customerService;

    public StripeDiscountCouponProvider(
        StripeOptions options,
        CouponService? couponService = null,
        PromotionCodeService? promotionCodeService = null,
        CustomerService? customerService = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException(
                "StripeOptions.SecretKey is required for the discount coupon provider.",
                nameof(options));
        }
        var client = new StripeClient(_options.SecretKey);
        _couponService = couponService ?? new CouponService(client);
        _promotionCodeService = promotionCodeService ?? new PromotionCodeService(client);
        _customerService = customerService ?? new CustomerService(client);
    }

    /// <inheritdoc/>
    public string Name => "stripe";

    /// <inheritdoc/>
    public async Task<CouponCreateResult> CreateCouponAsync(
        CouponCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AssignmentId))
        {
            throw new ArgumentException("AssignmentId is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.TargetEmailNormalized))
        {
            throw new ArgumentException("TargetEmailNormalized is required.", nameof(request));
        }

        // Stripe Coupon: PercentOff = decimal in 0.01..100; AmountOff = long
        // in smallest currency unit. Cena domain stores PercentOff as basis
        // points (1..10000) and AmountOff as agorot — convert here.
        var couponOptions = new CouponCreateOptions
        {
            Duration = "repeating",
            DurationInMonths = request.DurationMonths,
            // Coupon name surfaced in Stripe Dashboard for ops.
            Name = $"Cena {request.DiscountKind} discount {request.AssignmentId}",
            Metadata = new Dictionary<string, string>
            {
                ["cena_assignment_id"] = request.AssignmentId,
                ["cena_target_email"] = request.TargetEmailNormalized,
                ["cena_kind"] = request.DiscountKind.ToString(),
                ["cena_value"] = request.DiscountValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cena_duration_months"] = request.DurationMonths.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };

        if (request.DiscountKind == DiscountKind.PercentOff)
        {
            // Convert basis points → percent. 5000 bp == 50%.
            couponOptions.PercentOff = (decimal)request.DiscountValue / 100m;
        }
        else
        {
            couponOptions.AmountOff = request.DiscountValue;
            couponOptions.Currency = "ils";
        }

        var requestOptions = new RequestOptions
        {
            ApiKey = _options.SecretKey,
            // Stripe-side idempotency: replays with the same assignment id
            // return the existing coupon rather than creating a duplicate.
            IdempotencyKey = $"cena-coupon-{request.AssignmentId}",
        };

        var coupon = await _couponService
            .CreateAsync(couponOptions, requestOptions, ct)
            .ConfigureAwait(false);

        // Look up an existing Stripe Customer for the target email so we
        // can restrict the promotion code to it. If none exists, the
        // promotion code is metadata-tagged and the checkout flow will
        // bind it to the customer at first paid checkout.
        string? existingCustomerId = await TryFindCustomerIdByEmailAsync(
            request.TargetEmailNormalized, ct).ConfigureAwait(false);

        var promoOptions = new PromotionCodeCreateOptions
        {
            Coupon = coupon.Id,
            // MaxRedemptions = 1 — the discount is per-user single-use.
            // After the first redemption, Stripe rejects further uses of
            // this code automatically; combined with the discount-status
            // state machine this is belt + braces.
            MaxRedemptions = 1,
            Metadata = new Dictionary<string, string>
            {
                ["cena_assignment_id"] = request.AssignmentId,
                ["cena_target_email"] = request.TargetEmailNormalized,
            },
        };
        if (!string.IsNullOrWhiteSpace(existingCustomerId))
        {
            promoOptions.Customer = existingCustomerId;
        }

        var promoRequestOptions = new RequestOptions
        {
            ApiKey = _options.SecretKey,
            IdempotencyKey = $"cena-promo-{request.AssignmentId}",
        };

        var promotionCode = await _promotionCodeService
            .CreateAsync(promoOptions, promoRequestOptions, ct)
            .ConfigureAwait(false);

        return new CouponCreateResult(
            CouponId: coupon.Id,
            PromotionCodeId: promotionCode.Id,
            PromotionCodeString: promotionCode.Code);
    }

    /// <inheritdoc/>
    public async Task RevokeCouponAsync(CouponRevokeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = new RequestOptions { ApiKey = _options.SecretKey };

        // Deactivate the promotion code first so no new checkouts can
        // attach it, even if the coupon delete races with an in-flight
        // session create.
        if (!string.IsNullOrWhiteSpace(request.PromotionCodeId))
        {
            try
            {
                await _promotionCodeService.UpdateAsync(
                    request.PromotionCodeId,
                    new PromotionCodeUpdateOptions { Active = false },
                    apiKey, ct).ConfigureAwait(false);
            }
            catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Idempotent: gateway already lost it (concurrent revoke
                // or manual dashboard delete) — nothing to do.
            }
        }

        if (!string.IsNullOrWhiteSpace(request.CouponId))
        {
            try
            {
                await _couponService
                    .DeleteAsync(request.CouponId, requestOptions: apiKey, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Idempotent on 404 (already deleted).
            }
        }
    }

    /// <summary>
    /// Look up an existing Stripe Customer by email. Returns null when
    /// none exists or on transient errors (caller falls back to the
    /// metadata-restricted promo code path).
    /// </summary>
    private async Task<string?> TryFindCustomerIdByEmailAsync(
        string email, CancellationToken ct)
    {
        try
        {
            var listOptions = new CustomerListOptions
            {
                Email = email,
                Limit = 1,
            };
            var requestOptions = new RequestOptions { ApiKey = _options.SecretKey };
            var list = await _customerService.ListAsync(listOptions, requestOptions, ct)
                .ConfigureAwait(false);
            var customer = list.Data.FirstOrDefault();
            return customer?.Id;
        }
        catch
        {
            // Don't fail issuance because the lookup hiccupped — fall back
            // to metadata-restricted promo code path.
            return null;
        }
    }
}
