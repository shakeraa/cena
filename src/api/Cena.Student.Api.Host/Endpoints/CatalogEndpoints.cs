// =============================================================================
// Cena Platform — Exam Catalog Endpoints (prr-220, ADR-0050)
//
// Routes:
//   GET  /api/v1/catalog/exam-targets?locale=en|he|ar
//   GET  /api/v1/catalog/exam-targets/{code}/topics?locale=…
//   POST /api/admin/catalog/rebuild                          (SuperAdmin)
//
// Tenant scoping (ADR-0001): overlay is resolved from the authenticated
// tenant claim. Unauthenticated callers get the global catalog with an
// empty overlay and ETag keyed by version only (no tenant fingerprinting
// per persona-redteam). Authenticated callers get their tenant's view
// with the tenant id baked into the ETag so browser caches partition
// correctly.
//
// PWA-friendly caching: Cache-Control public/max-age=300 +
// stale-while-revalidate=86400 so a 5-minute edge cache works, but
// browsers continue to serve stale up to 24h while revalidating.
// ETag + If-None-Match short-circuits to 304 for catalogs that haven't
// changed since the last fetch.
// =============================================================================

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cena.Api.Contracts.Catalog;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Cena.Student.Api.Host.Catalog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

public static class CatalogEndpoints
{
    public const string CacheControl = "public, max-age=300, stale-while-revalidate=86400";

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/v1/catalog")
            .WithTags("Catalog")
            // Auth is optional here. Anonymous callers still get a catalog,
            // but with an empty overlay and no tenant fingerprint in ETag
            // (persona-redteam guidance).
            .AllowAnonymous();

        pub.MapGet("exam-targets", GetExamTargets)
            .WithName("GetExamTargets")
            .Produces<ExamTargetCatalogDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status304NotModified);

        pub.MapGet("exam-targets/{code}/topics", GetExamTargetTopics)
            .WithName("GetExamTargetTopics")
            .Produces<ExamTargetTopicsDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        var admin = app.MapGroup("/api/admin/catalog")
            .WithTags("Catalog Admin")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);

        admin.MapPost("rebuild", RebuildCatalog)
            .WithName("RebuildCatalog")
            .Produces<CatalogRebuildResultDto>(StatusCodes.Status200OK)
            .Produces<CatalogRebuildResultDto>(StatusCodes.Status409Conflict)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    internal static IResult GetExamTargets(
        HttpContext ctx,
        IExamCatalogService catalog,
        ITenantCatalogOverlayStore overlayStore,
        [FromQuery] string? locale = null)
    {
        var tenantId = ResolveTenantId(ctx.User);
        var overlay = overlayStore.Resolve(tenantId);

        var dto = catalog.GetForTenant(tenantId, locale ?? ExamCatalogService.DefaultLocale, overlay);

        var etag = ComputeEtag(dto.CatalogVersion, tenantId, dto.Locale, overlay);
        if (IfNoneMatchEquals(ctx.Request, etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        ctx.Response.Headers.CacheControl = CacheControl;
        ctx.Response.Headers.ETag = etag;
        ctx.Response.Headers.Vary = "Accept-Language, X-Tenant-Id";

        return Results.Ok(dto);
    }

    internal static IResult GetExamTargetTopics(
        HttpContext ctx,
        IExamCatalogService catalog,
        [FromRoute] string code,
        [FromQuery] string? locale = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Results.Json(
                new CenaError(
                    "invalid_code",
                    "Exam code required",
                    ErrorCategory.Validation, null, null),
                statusCode: StatusCodes.Status400BadRequest);

        var dto = catalog.GetTopics(code, locale ?? ExamCatalogService.DefaultLocale);
        if (dto is null)
            return Results.Json(
                new CenaError(
                    "exam_target_unknown",
                    $"Unknown exam code: {code}",
                    ErrorCategory.NotFound, null, null),
                statusCode: StatusCodes.Status404NotFound);

        ctx.Response.Headers.CacheControl = CacheControl;
        ctx.Response.Headers.ETag = ComputeEtag(
            dto.CatalogVersion, tenantId: null, dto.Locale, CatalogTenantOverlay.Empty);
        return Results.Ok(dto);
    }

    internal static async Task<IResult> RebuildCatalog(
        IExamCatalogService catalog,
        CancellationToken ct)
    {
        var outcome = await catalog.RebuildAsync(ct);
        var payload = new CatalogRebuildResultDto(
            PreviousVersion: outcome.PreviousVersion,
            CurrentVersion: outcome.CurrentVersion,
            TargetsLoaded: outcome.TargetsLoaded,
            Warnings: outcome.Warnings);

        return outcome.Accepted
            ? Results.Ok(payload)
            : Results.Json(payload, statusCode: StatusCodes.Status409Conflict);
    }

    // ---------- helpers ----------

    internal static string? ResolveTenantId(ClaimsPrincipal user)
        => user.FindFirstValue("institute_id")
           ?? user.FindFirstValue("tenant_id")
           ?? user.FindFirstValue("school_id");

    internal static string ComputeEtag(
        string catalogVersion,
        string? tenantId,
        string locale,
        CatalogTenantOverlay overlay)
    {
        // Stable, tenant-aware, overlay-sensitive ETag. SHA-256-truncated-to-16
        // is plenty for cache key uniqueness and keeps the header small.
        var sb = new StringBuilder(128);
        sb.Append(catalogVersion).Append('|');
        sb.Append(locale).Append('|');
        sb.Append(tenantId ?? "anon").Append('|');
        if (overlay.EnabledExamCodes is { } enabled)
            sb.Append("ena:").Append(string.Join(",", enabled.OrderBy(x => x, StringComparer.Ordinal)));
        sb.Append('|');
        if (overlay.DisabledExamCodes.Count > 0)
            sb.Append("dis:").Append(string.Join(",", overlay.DisabledExamCodes.OrderBy(x => x, StringComparer.Ordinal)));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "\"" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + "\"";
    }

    private static bool IfNoneMatchEquals(HttpRequest request, string etag)
    {
        if (!request.Headers.TryGetValue("If-None-Match", out var values))
            return false;
        foreach (var v in values)
            if (string.Equals(v, etag, StringComparison.Ordinal))
                return true;
        return false;
    }
}
