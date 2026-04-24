// =============================================================================
// Cena Platform — Pricing catalog endpoint tests (EPIC-PRR-I PRR-291)
//
// Covers the PRR-291 DoD: GET /api/v1/tiers returns 3 retail tiers with
// the full shape (tierId, monthly/annual price + VAT split, caps,
// feature flags), sibling discount, and Israeli VAT basis points.
//
// We exercise BuildPricingCatalog() directly — the endpoint is a thin
// caching wrapper over this pure function, so coverage at the function
// level is where correctness lives. ETag + Cache-Control headers are
// locked by the endpoint's static CacheControl constant.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Cena.Student.Api.Host.Endpoints;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class PricingCatalogEndpointTests
{
    [Fact]
    public void Catalog_returns_exactly_three_retail_tiers()
    {
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();

        Assert.Equal(3, catalog.Tiers.Count);
        var ids = catalog.Tiers.Select(t => t.TierId).ToHashSet();
        Assert.Contains("Basic", ids);
        Assert.Contains("Plus", ids);
        Assert.Contains("Premium", ids);
    }

    [Fact]
    public void Each_tier_carries_vat_split_in_agorot()
    {
        // VAT is 17% inclusive on the catalog (ADR-0057 §5). Each tier's
        // VAT field must be strictly positive and match the Israeli VAT
        // calculator's decomposition of gross → net + vat.
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();

        foreach (var tier in catalog.Tiers)
        {
            Assert.True(tier.MonthlyPriceAgorot > 0, $"{tier.TierId} missing monthly price");
            Assert.True(tier.AnnualPriceAgorot > 0, $"{tier.TierId} missing annual price");
            Assert.True(tier.MonthlyVatAgorot > 0, $"{tier.TierId} VAT not decomposed from monthly");
            Assert.True(tier.AnnualVatAgorot > 0, $"{tier.TierId} VAT not decomposed from annual");
            // VAT + Net = gross: we don't re-derive net here, but VAT
            // must be less than gross (sanity on the decomposition sign).
            Assert.True(tier.MonthlyVatAgorot < tier.MonthlyPriceAgorot,
                $"{tier.TierId} monthly VAT larger than gross");
            Assert.True(tier.AnnualVatAgorot < tier.AnnualPriceAgorot,
                $"{tier.TierId} annual VAT larger than gross");
        }
    }

    [Fact]
    public void Premium_carries_full_feature_flags()
    {
        // Premium is the marketing "target" tier — all parent-side
        // features flip on here so pricing marketing is honest.
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();
        var premium = catalog.Tiers.Single(t => t.TierId == "Premium");

        Assert.True(premium.Features.ParentDashboard);
        Assert.True(premium.Features.TutorHandoffPdf);
        Assert.True(premium.Features.ArabicDashboard);
        Assert.True(premium.Features.PrioritySupport);
    }

    [Fact]
    public void Basic_has_zero_photo_diagnostics_cap()
    {
        // PRR-312 + ADR-0057: Basic excludes photo diagnostic as the
        // tier differentiator. If the wire contract leaks a non-zero
        // cap the UI would promise a feature Basic parents do not have.
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();
        var basic = catalog.Tiers.Single(t => t.TierId == "Basic");

        // 0 on the DTO surfaces as the literal 0 (not null — Unlimited→null).
        Assert.Equal(0, basic.Caps.PhotoDiagnosticsPerMonth);
        Assert.Equal(0, basic.Caps.PhotoDiagnosticsHardCapPerMonth);
    }

    [Fact]
    public void Plus_and_Premium_sonnet_cap_is_unlimited()
    {
        // Plus/Premium tiers carry Unlimited sonnet escalations — the
        // DTO encodes Unlimited as null so the UI knows "no cap" vs "0".
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();

        foreach (var id in new[] { "Plus", "Premium" })
        {
            var tier = catalog.Tiers.Single(t => t.TierId == id);
            Assert.Null(tier.Caps.SonnetEscalationsPerWeek);
        }
    }

    [Fact]
    public void Sibling_discount_and_vat_basis_points_are_populated()
    {
        var catalog = SubscriptionEndpoints.BuildPricingCatalog();

        Assert.True(catalog.SiblingDiscount.FirstSecondSiblingMonthlyAgorot > 0);
        Assert.True(catalog.SiblingDiscount.ThirdPlusSiblingMonthlyAgorot > 0);
        Assert.True(catalog.SiblingDiscount.ThirdPlusSiblingMonthlyAgorot
            < catalog.SiblingDiscount.FirstSecondSiblingMonthlyAgorot,
            "3+ sibling price must be cheaper than 1-2 sibling price");

        // Israel 17% VAT as basis points = 1700.
        Assert.Equal(1700, catalog.VatBasisPoints);
    }

    [Fact]
    public void Cache_control_constant_is_explicit_public_max_age_300()
    {
        // Lock the cache-control string so a future refactor cannot
        // accidentally drop the stale-while-revalidate hint — the
        // pricing page's perceived-latency story depends on it.
        Assert.Equal(
            "public, max-age=300, stale-while-revalidate=86400",
            SubscriptionEndpoints.CacheControl);
    }
}
