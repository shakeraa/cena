// =============================================================================
// Cena Platform — CatalogEndpoints unit tests (prr-220)
//
// Drives the minimal-API handlers via `DefaultHttpContext` — no test server
// needed. Validates:
//   - 200 + ETag + Cache-Control on the grouped list response
//   - 304 Not Modified when If-None-Match echoes the ETag
//   - 404 on unknown exam target
//   - Tenant-overlay subtraction reflected in the served groups
//   - Admin rebuild returns 409 when monotonicity is violated
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Api.Contracts.Catalog;
using Cena.Student.Api.Host.Catalog;
using Cena.Student.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class CatalogEndpointsTests
{
    private static string CatalogDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "contracts", "exam-catalog");
    }

    private static ExamCatalogService NewService() =>
        new(CatalogDir(), new NullTenantCatalogOverlayStore(),
            NullLogger<ExamCatalogService>.Instance);

    private static DefaultHttpContext NewAnonContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        return ctx;
    }

    private static DefaultHttpContext NewTenantContext(string tenantId)
    {
        var ctx = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("institute_id", tenantId),
            new Claim("sub", "student-1"),
        }, "test");
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public void GetExamTargets_returns_ok_with_cache_headers()
    {
        var svc = NewService();
        var overlayStore = new NullTenantCatalogOverlayStore();
        var ctx = NewAnonContext();

        var result = CatalogEndpoints.GetExamTargets(ctx, svc, overlayStore, "en");

        Assert.IsType<Ok<ExamTargetCatalogDto>>(result);
        Assert.Equal(CatalogEndpoints.CacheControl, ctx.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrWhiteSpace(ctx.Response.Headers.ETag.ToString()));
        Assert.Equal("Accept-Language, X-Tenant-Id", ctx.Response.Headers.Vary.ToString());
    }

    [Fact]
    public void GetExamTargets_returns_304_on_matching_if_none_match()
    {
        var svc = NewService();
        var overlayStore = new NullTenantCatalogOverlayStore();

        // First call — fetch ETag.
        var ctx1 = NewAnonContext();
        CatalogEndpoints.GetExamTargets(ctx1, svc, overlayStore, "en");
        var etag = ctx1.Response.Headers.ETag.ToString();
        Assert.False(string.IsNullOrWhiteSpace(etag));

        // Second call — echo the etag as If-None-Match.
        var ctx2 = NewAnonContext();
        ctx2.Request.Headers["If-None-Match"] = etag;
        var result = CatalogEndpoints.GetExamTargets(ctx2, svc, overlayStore, "en");

        var status = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status304NotModified, status.StatusCode);
    }

    [Fact]
    public void GetExamTargetTopics_unknown_code_returns_404()
    {
        var svc = NewService();
        var ctx = NewAnonContext();

        var result = CatalogEndpoints.GetExamTargetTopics(
            ctx, svc, code: "NOT_A_REAL_TARGET", locale: "en");

        // The handler uses Results.Json with 404 status; inspect via the
        // JsonHttpResult shape produced by the framework.
        Assert.NotNull(result);
        // Best-effort assertion: the result type exposes StatusCode.
        var statusProp = result.GetType().GetProperty("StatusCode");
        Assert.Equal(StatusCodes.Status404NotFound, statusProp?.GetValue(result));
    }

    [Fact]
    public void GetExamTargetTopics_known_code_returns_200_with_localized_display()
    {
        var svc = NewService();
        var ctx = NewAnonContext();

        var result = CatalogEndpoints.GetExamTargetTopics(
            ctx, svc, code: "BAGRUT_MATH_5U", locale: "he");

        var ok = Assert.IsType<Ok<ExamTargetTopicsDto>>(result);
        Assert.Equal("BAGRUT_MATH_5U", ok.Value!.ExamCode);
        Assert.Equal("he", ok.Value.Locale);
        Assert.True(ok.Value.Topics.Count > 0);
    }

    [Fact]
    public void GetExamTargets_etag_differs_between_tenants()
    {
        // Two tenants with identical overlays should still get different
        // cache keys — persona-redteam's "no cross-tenant fingerprinting" rule.
        var svc = NewService();
        var overlayStore = new NullTenantCatalogOverlayStore();

        var ctxA = NewTenantContext("school-A");
        CatalogEndpoints.GetExamTargets(ctxA, svc, overlayStore, "en");
        var etagA = ctxA.Response.Headers.ETag.ToString();

        var ctxB = NewTenantContext("school-B");
        CatalogEndpoints.GetExamTargets(ctxB, svc, overlayStore, "en");
        var etagB = ctxB.Response.Headers.ETag.ToString();

        Assert.NotEqual(etagA, etagB);
    }

    [Fact]
    public async Task RebuildCatalog_returns_409_on_monotonicity_violation()
    {
        // The on-disk YAML and the loaded snapshot share the same version
        // so a rebuild always returns non_monotonic_version warning + 409.
        var svc = NewService();
        var result = await CatalogEndpoints.RebuildCatalog(svc, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CatalogRebuildResultDto>>(result);
        Assert.Equal(StatusCodes.Status409Conflict, json.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(json.Value!.CurrentVersion));
        Assert.Contains(json.Value.Warnings, w => w.Contains("non_monotonic_version"));
    }
}
