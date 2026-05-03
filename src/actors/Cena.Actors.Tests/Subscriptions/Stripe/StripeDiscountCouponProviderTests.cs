// =============================================================================
// Cena Platform — StripeDiscountCouponProvider tests (per-user discount-codes)
//
// Network-free unit tests for the Stripe adapter. Subclasses Stripe SDK
// services (CouponService, PromotionCodeService, CustomerService) and
// overrides their virtual Create/Update/Delete/List methods so the test
// can assert the option payload + idempotency-key shape WITHOUT hitting
// the Stripe sandbox.
//
// Coverage matrix:
//   Construction
//     - Throws when options.SecretKey is blank
//   CreateCouponAsync — PercentOff
//     - Coupon: Duration=repeating, DurationInMonths=N, PercentOff=value/100
//     - Coupon Metadata carries cena_assignment_id + cena_target_email
//     - PromotionCode: MaxRedemptions=1, Coupon=<created>, restricted to
//       Customer when one exists (else metadata-tagged only)
//     - Returns CouponId + PromotionCodeId + PromotionCodeString
//   CreateCouponAsync — AmountOff
//     - Coupon: AmountOff=agorot, Currency="ils"
//   RevokeCouponAsync
//     - Calls UpdateAsync(active=false) on the promotion code
//     - Calls DeleteAsync on the coupon
//     - Idempotent on Stripe 404 (gateway already lost it)
// =============================================================================

using System.Net;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Actors.Subscriptions.Stripe;
using Stripe;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class StripeDiscountCouponProviderTests
{
    private static StripeOptions ValidOptions() => new()
    {
        SecretKey = "sk_test_dummy_local_only",
        WebhookSigningSecret = "whsec_dummy_local_only",
        // Some price ids so other code paths that consume StripeOptions don't trip.
        PriceIds = new StripePriceIdMap
        {
            BasicMonthly = "price_basic_m", BasicAnnual = "price_basic_a",
            PlusMonthly = "price_plus_m", PlusAnnual = "price_plus_a",
            PremiumMonthly = "price_premium_m", PremiumAnnual = "price_premium_a",
        },
    };

    [Fact]
    public void Constructor_throws_when_secret_key_blank()
    {
        var options = ValidOptions();
        options.SecretKey = "";
        Assert.Throws<ArgumentException>(() =>
            new StripeDiscountCouponProvider(options));
    }

    [Fact]
    public async Task CreateCouponAsync_percent_off_sets_percent_and_repeating_duration()
    {
        var (coupon, promo, customer) = NewSpyServices();
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        var result = await sut.CreateCouponAsync(new CouponCreateRequest(
            AssignmentId: "abc",
            TargetEmailNormalized: "alice@gmail.com",
            DiscountKind: DiscountKind.PercentOff,
            DiscountValue: 5_000,
            DurationMonths: 3), default);

        Assert.NotNull(coupon.LastCreateOptions);
        Assert.Equal("repeating", coupon.LastCreateOptions!.Duration);
        Assert.Equal(3, coupon.LastCreateOptions.DurationInMonths);
        Assert.Equal(50m, coupon.LastCreateOptions.PercentOff);
        Assert.Null(coupon.LastCreateOptions.AmountOff);
        Assert.Null(coupon.LastCreateOptions.Currency);
        Assert.Equal("abc", coupon.LastCreateOptions.Metadata!["cena_assignment_id"]);
        Assert.Equal("alice@gmail.com", coupon.LastCreateOptions.Metadata!["cena_target_email"]);

        Assert.Equal("cena-coupon-abc", coupon.LastCreateRequestOptions!.IdempotencyKey);

        Assert.NotNull(promo.LastCreateOptions);
        Assert.Equal(coupon.NextCouponId, promo.LastCreateOptions!.Coupon);
        Assert.Equal(1L, promo.LastCreateOptions.MaxRedemptions);
        // No customer existed → no Customer restriction.
        Assert.Null(promo.LastCreateOptions.Customer);

        Assert.Equal(coupon.NextCouponId, result.CouponId);
        Assert.Equal(promo.NextPromoId, result.PromotionCodeId);
        Assert.Equal(promo.NextPromoCode, result.PromotionCodeString);
    }

    [Fact]
    public async Task CreateCouponAsync_amount_off_sets_amount_and_ils_currency()
    {
        var (coupon, promo, customer) = NewSpyServices();
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        await sut.CreateCouponAsync(new CouponCreateRequest(
            AssignmentId: "abc",
            TargetEmailNormalized: "alice@gmail.com",
            DiscountKind: DiscountKind.AmountOff,
            DiscountValue: 5_000,
            DurationMonths: 6), default);

        Assert.Equal(5_000L, coupon.LastCreateOptions!.AmountOff);
        Assert.Equal("ils", coupon.LastCreateOptions.Currency);
        Assert.Null(coupon.LastCreateOptions.PercentOff);
    }

    [Fact]
    public async Task CreateCouponAsync_restricts_to_customer_when_exists()
    {
        var (coupon, promo, customer) = NewSpyServices();
        customer.NextCustomerId = "cus_existing";
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        await sut.CreateCouponAsync(new CouponCreateRequest(
            AssignmentId: "abc",
            TargetEmailNormalized: "alice@gmail.com",
            DiscountKind: DiscountKind.PercentOff,
            DiscountValue: 5_000,
            DurationMonths: 3), default);

        Assert.Equal("cus_existing", promo.LastCreateOptions!.Customer);
    }

    [Fact]
    public async Task RevokeCouponAsync_deactivates_promo_then_deletes_coupon()
    {
        var (coupon, promo, customer) = NewSpyServices();
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        await sut.RevokeCouponAsync(new CouponRevokeRequest(
            AssignmentId: "abc",
            CouponId: "cou_abc",
            PromotionCodeId: "promo_abc"), default);

        Assert.Equal("promo_abc", promo.LastUpdateId);
        Assert.False(promo.LastUpdateOptions!.Active);
        Assert.Equal("cou_abc", coupon.LastDeleteId);
    }

    [Fact]
    public async Task RevokeCouponAsync_is_idempotent_on_stripe_404()
    {
        var (coupon, promo, customer) = NewSpyServices();
        promo.UpdateThrowsNotFound = true;
        coupon.DeleteThrowsNotFound = true;
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        // Should not throw.
        await sut.RevokeCouponAsync(new CouponRevokeRequest(
            AssignmentId: "abc",
            CouponId: "cou_abc",
            PromotionCodeId: "promo_abc"), default);
    }

    [Fact]
    public async Task RevokeCouponAsync_with_blank_ids_is_no_op()
    {
        var (coupon, promo, customer) = NewSpyServices();
        var sut = new StripeDiscountCouponProvider(
            ValidOptions(), coupon, promo, customer);

        await sut.RevokeCouponAsync(new CouponRevokeRequest(
            AssignmentId: "abc",
            CouponId: "",
            PromotionCodeId: ""), default);

        Assert.Null(promo.LastUpdateId);
        Assert.Null(coupon.LastDeleteId);
    }

    // ---- Spy services that don't hit the network -------------------------

    private static (SpyCouponService, SpyPromotionCodeService, SpyCustomerService) NewSpyServices()
    {
        // The Stripe.net SDK requires an IHttpClientFactory or StripeClient
        // for the service base; we never invoke base.CreateAsync so passing
        // a placeholder client with a fake key is enough to satisfy the
        // constructor.
        var client = new global::Stripe.StripeClient("sk_test_dummy_local_only");
        return (new SpyCouponService(client), new SpyPromotionCodeService(client), new SpyCustomerService(client));
    }

    private sealed class SpyCouponService : CouponService
    {
        public SpyCouponService(IStripeClient client) : base(client) { }

        public CouponCreateOptions? LastCreateOptions { get; private set; }
        public RequestOptions? LastCreateRequestOptions { get; private set; }
        public string? LastDeleteId { get; private set; }
        public bool DeleteThrowsNotFound { get; set; }
        public string NextCouponId { get; set; } = "cou_inmem_abc";

        public override Task<global::Stripe.Coupon> CreateAsync(
            CouponCreateOptions options,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastCreateOptions = options;
            LastCreateRequestOptions = requestOptions;
            return Task.FromResult(new global::Stripe.Coupon { Id = NextCouponId });
        }

        public override Task<global::Stripe.Coupon> DeleteAsync(
            string id,
            CouponDeleteOptions? options = null,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastDeleteId = id;
            if (DeleteThrowsNotFound)
            {
                throw new StripeException(
                    HttpStatusCode.NotFound,
                    new StripeError { Code = "resource_missing" },
                    "not found");
            }
            return Task.FromResult(new global::Stripe.Coupon { Id = id, Deleted = true });
        }
    }

    private sealed class SpyPromotionCodeService : PromotionCodeService
    {
        public SpyPromotionCodeService(IStripeClient client) : base(client) { }

        public PromotionCodeCreateOptions? LastCreateOptions { get; private set; }
        public string? LastUpdateId { get; private set; }
        public PromotionCodeUpdateOptions? LastUpdateOptions { get; private set; }
        public bool UpdateThrowsNotFound { get; set; }
        public string NextPromoId { get; set; } = "promo_inmem_abc";
        public string NextPromoCode { get; set; } = "CENA-ABC";

        public override Task<PromotionCode> CreateAsync(
            PromotionCodeCreateOptions options,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastCreateOptions = options;
            return Task.FromResult(new PromotionCode
            {
                Id = NextPromoId,
                Code = NextPromoCode,
            });
        }

        public override Task<PromotionCode> UpdateAsync(
            string id,
            PromotionCodeUpdateOptions options,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastUpdateId = id;
            LastUpdateOptions = options;
            if (UpdateThrowsNotFound)
            {
                throw new StripeException(
                    HttpStatusCode.NotFound,
                    new StripeError { Code = "resource_missing" },
                    "not found");
            }
            return Task.FromResult(new PromotionCode { Id = id });
        }
    }

    private sealed class SpyCustomerService : CustomerService
    {
        public SpyCustomerService(IStripeClient client) : base(client) { }

        public string? NextCustomerId { get; set; }

        public override Task<StripeList<Customer>> ListAsync(
            CustomerListOptions? options = null,
            RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            var list = new StripeList<Customer>
            {
                Data = NextCustomerId is null
                    ? new List<Customer>()
                    : new List<Customer> { new() { Id = NextCustomerId } },
            };
            return Task.FromResult(list);
        }
    }
}
