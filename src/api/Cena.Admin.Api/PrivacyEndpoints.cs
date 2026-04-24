// =============================================================================
// Cena Platform — Privacy admin endpoints (prr-035)
//
// Read-only admin surface for the sub-processor registry. Reads from
// ISubProcessorRegistry (loaded at boot from contracts/privacy/sub-processors.yml).
//
// Routing:
//   GET  /api/admin/privacy/sub-processors          — all entries (admin)
//   GET  /api/admin/privacy/sub-processors/parent   — parent_visible only
//
// Auth:
//   AdminOnly for /sub-processors (includes compliance + content review)
//   ParentOrAbove reserved for the /parent variant (future parent portal
//   surface; today this endpoint is the admin "preview as parent" seam).
//
// No writes — registry edits go through the contracts/privacy/ YAML
// and a code-review PR. There is no runtime mutation surface by design.
// =============================================================================

using Cena.Actors.Infrastructure.Privacy;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/privacy")
            .WithTags("Privacy — Sub-Processor Registry")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        group.MapGet("/sub-processors", (ISubProcessorRegistry registry) =>
        {
            var snap = registry.Current;
            var list = snap.All.Select(ToDto).ToArray();
            return Results.Ok(new SubProcessorRegistryDto(
                RegistryVersion: snap.RegistryVersion,
                UpdatedAtUtc: snap.UpdatedAtUtc,
                DataCategoryTaxonomy: snap.DataCategoryTaxonomy,
                SubProcessors: list));
        })
        .WithName("GetSubProcessorRegistry")
        .Produces<SubProcessorRegistryDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        group.MapGet("/sub-processors/parent", (ISubProcessorRegistry registry) =>
        {
            var snap = registry.Current;
            var list = snap.ParentVisible.Select(ToDto).ToArray();
            return Results.Ok(new SubProcessorRegistryDto(
                RegistryVersion: snap.RegistryVersion,
                UpdatedAtUtc: snap.UpdatedAtUtc,
                DataCategoryTaxonomy: snap.DataCategoryTaxonomy,
                SubProcessors: list));
        })
        .WithName("GetParentVisibleSubProcessors")
        .Produces<SubProcessorRegistryDto>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
        .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static SubProcessorDto ToDto(SubProcessor p) => new(
        Id: p.Id,
        Vendor: p.Vendor,
        Category: p.Category,
        Purpose: p.Purpose,
        DataCategories: p.DataCategories,
        DataResidency: p.DataResidency,
        SsoMethod: p.SsoMethod,
        DpaEffectiveDate: p.DpaEffectiveDate,
        Status: p.Status,
        ParentVisible: p.ParentVisible,
        Hostnames: p.Hostnames,
        Notes: p.Notes);

    // Admin DTO — deliberately OMITS the dpa_link field. The raw
    // `legal://` URI resolves only via the compliance-admin workflow
    // (countersigned PDF storage); exposing it on a JSON surface leaks
    // storage paths. Admins needing the DPA go through the separate
    // compliance workflow surface.
    public sealed record SubProcessorRegistryDto(
        string RegistryVersion,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<string> DataCategoryTaxonomy,
        IReadOnlyList<SubProcessorDto> SubProcessors);

    public sealed record SubProcessorDto(
        string Id,
        string Vendor,
        string Category,
        string Purpose,
        IReadOnlyList<string> DataCategories,
        string DataResidency,
        string SsoMethod,
        DateTimeOffset DpaEffectiveDate,
        string Status,
        bool ParentVisible,
        IReadOnlyList<string> Hostnames,
        string? Notes);
}
