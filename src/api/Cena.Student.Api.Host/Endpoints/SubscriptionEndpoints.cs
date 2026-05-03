// =============================================================================
// Cena Platform — Subscription endpoints (EPIC-PRR-I PRR-290/291, ADR-0057)
//
// Public pricing catalog. Anonymous-OK — pricing is advertised content.
// Authenticated tier-enforcement endpoints (activate, change, cancel) land
// in follow-up tasks; this file ships the catalog only.
//
// Cache discipline: strong ETag on the response body, Cache-Control
// public/max-age=300. Because the catalog is code-constant (ADR-0057 §6),
// the ETag is derived from the constant hash — stable across deploys unless
// prices change, which is exactly the cache-invalidation signal we want.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Minimal-API endpoint wiring for the pricing catalog.</summary>
public static class SubscriptionEndpoints
{
    /// <summary>Cache-Control header value for the public catalog response.</summary>
    public const string CacheControl = "public, max-age=300, stale-while-revalidate=86400";

    /// <summary>Register the <c>/api/v1/tiers</c> endpoint group.</summary>
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Subscriptions")
            .AllowAnonymous();

        group.MapGet("tiers", GetPricingCatalog)
            .WithName("GetPricingCatalog")
            .WithSummary("Retail pricing catalog (Basic, Plus, Premium) + sibling discount.");

        return app;
    }

    private static IResult GetPricingCatalog(HttpContext http)
    {
        var dto = BuildPricingCatalog();
        var payload = JsonSerializer.Serialize(dto, CatalogSerializerOptions);
        var etag = ComputeWeakEtag(payload);

        var requestEtag = http.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(requestEtag) && requestEtag == etag)
        {
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = CacheControl;
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = CacheControl;
        http.Response.ContentType = "application/json; charset=utf-8";
        return Results.Content(payload, "application/json");
    }

    /// <summary>
    /// Build the catalog DTO from the code-constant <see cref="TierCatalog"/>.
    /// Pure function; deterministic.
    /// </summary>
    public static PricingCatalogResponseDto BuildPricingCatalog()
    {
        var tiers = TierCatalog.RetailTiers
            .Select(MapTier)
            .ToArray();

        var siblingDiscount = new SiblingDiscountDto(
            FirstSecondSiblingMonthlyAgorot: TierCatalog.SiblingMonthlyPrice(1).Amount,
            ThirdPlusSiblingMonthlyAgorot: TierCatalog.SiblingMonthlyPrice(3).Amount);

        return new PricingCatalogResponseDto(
            Tiers: tiers,
            SiblingDiscount: siblingDiscount,
            VatBasisPoints: IsraeliVatCalculator.VatBasisPoints);
    }

    private static RetailTierDto MapTier(TierDefinition definition)
    {
        var monthlyVat = IsraeliVatCalculator.DecomposeGross(definition.MonthlyPrice);
        var annualVat = IsraeliVatCalculator.DecomposeGross(definition.AnnualPrice);

        return new RetailTierDto(
            TierId: definition.Tier.ToString(),
            MonthlyPriceAgorot: definition.MonthlyPrice.Amount,
            AnnualPriceAgorot: definition.AnnualPrice.Amount,
            MonthlyVatAgorot: monthlyVat.Vat.Amount,
            AnnualVatAgorot: annualVat.Vat.Amount,
            Caps: new UsageCapsDto(
                SonnetEscalationsPerWeek: UnlimitedToNull(definition.Caps.SonnetEscalationsPerWeek),
                PhotoDiagnosticsPerMonth: UnlimitedToNull(definition.Caps.PhotoDiagnosticsPerMonth),
                PhotoDiagnosticsHardCapPerMonth: UnlimitedToNull(definition.Caps.PhotoDiagnosticsHardCapPerMonth),
                HintRequestsPerMonth: UnlimitedToNull(definition.Caps.HintRequestsPerMonth)),
            Features: new TierFeatureFlagsDto(
                ParentDashboard: definition.Features.ParentDashboard,
                TutorHandoffPdf: definition.Features.TutorHandoffPdf,
                ArabicDashboard: definition.Features.ArabicDashboard,
                PrioritySupport: definition.Features.PrioritySupport));
    }

    private static int? UnlimitedToNull(int value) =>
        value == UsageCaps.Unlimited ? null : value;

    private static readonly JsonSerializerOptions CatalogSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string ComputeWeakEtag(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var hash = SHA256.HashData(bytes);
        var b64 = Convert.ToBase64String(hash, 0, 16);
        return $"W/\"{b64}\"";
    }
}
