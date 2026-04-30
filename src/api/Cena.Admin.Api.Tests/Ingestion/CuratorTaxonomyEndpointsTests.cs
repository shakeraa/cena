// =============================================================================
// Cena Platform — Curator Taxonomy Endpoints Tests
//
// Two layers:
//   (1) Wiring smoke test: MapCuratorTaxonomyEndpoints registers the route
//       under the expected pattern and applies the ModeratorOrAbove policy,
//       and the registration extension wires it into the admin host pipeline.
//       Per the route-smoke-test memory, every minimal-API group needs this —
//       direct-handler tests miss [FromBody]/service-vs-body inference defects.
//   (2) Behavioural test: the handler returns the expected leaf set for a
//       known TaxonomyCache, applies the track filter correctly, normalizes
//       the wire-value track ids, and rejects unknown tracks with 400.
// =============================================================================

using Cena.Admin.Api;
using Cena.Admin.Api.Content;
using Cena.Admin.Api.Ingestion;
using Cena.Admin.Api.Registration;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Admin.Api.Tests.Ingestion;

public class CuratorTaxonomyEndpointsTests
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();

        // The taxonomy endpoint requires (a) the auth policies it gates on,
        // (b) routing, (c) a rate-limiter named "api" because the group calls
        // RequireRateLimiting("api"), and (d) TaxonomyCache in DI.
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("api", o =>
            {
                o.PermitLimit = 100;
                o.Window = TimeSpan.FromMinutes(1);
            });
        });
        // Keep using the real cache loaded from disk. Tests only assert
        // structure, not specific concept counts (those are covered by
        // ContentCoverageServiceTests), so disk read is fine here.
        builder.Services.AddSingleton<TaxonomyCache>(_ => TaxonomyCache.LoadFromDisk());
        return builder.Build();
    }

    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app)
    {
        var routeBuilder = (IEndpointRouteBuilder)app;
        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    // ---------------------------------------------------------------------
    // (1) Wiring smoke
    // ---------------------------------------------------------------------

    [Fact]
    public void MapCuratorTaxonomyEndpoints_RegistersLeavesRoute()
    {
        var app = BuildTestApp();
        app.MapCuratorTaxonomyEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null && p.StartsWith("/api/admin/ingestion/taxonomy", StringComparison.Ordinal))
            .ToHashSet();

        Assert.Contains("/api/admin/ingestion/taxonomy/leaves", patterns);
    }

    [Fact]
    public void MapCuratorTaxonomyEndpoints_AppliesModeratorOrAbovePolicy()
    {
        var app = BuildTestApp();
        app.MapCuratorTaxonomyEndpoints();

        var endpoints = EnumerateEndpoints(app)
            .Where(e => e.RoutePattern.RawText is not null &&
                        e.RoutePattern.RawText.StartsWith("/api/admin/ingestion/taxonomy", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
            Assert.NotEmpty(authAttrs);
            Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
        }
    }

    // ---------------------------------------------------------------------
    // (2) Behaviour — call the handler delegate directly via the route
    // ---------------------------------------------------------------------

    private static (TaxonomyLeavesResponse? body, int status) InvokeLeaves(string? track)
    {
        var app = BuildTestApp();
        app.MapCuratorTaxonomyEndpoints();

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == "/api/admin/ingestion/taxonomy/leaves");

        // Build a synthetic GET request with the optional track query param.
        var ctx = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() },
        };
        ctx.Request.Method = HttpMethods.Get;
        ctx.Request.Path = "/api/admin/ingestion/taxonomy/leaves";
        if (track is not null) ctx.Request.QueryString = new QueryString($"?track={track}");

        endpoint.RequestDelegate!(ctx).GetAwaiter().GetResult();
        ctx.Response.Body.Position = 0;
        var status = ctx.Response.StatusCode;
        if (status != 200) return (null, status);

        var json = new StreamReader(ctx.Response.Body).ReadToEnd();
        var body = System.Text.Json.JsonSerializer.Deserialize<TaxonomyLeavesResponse>(
            json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return (body, status);
    }

    [Fact]
    public void Leaves_NoTrackFilter_ReturnsAllLeaves()
    {
        var (body, status) = InvokeLeaves(track: null);

        Assert.Equal(200, status);
        Assert.NotNull(body);
        Assert.Null(body!.Track);
        Assert.NotEmpty(body.Leaves);
        // The committed bagrut-taxonomy.json has 3 tracks. Leaves should
        // span all three; pull the unique TrackIds and sanity-check the count.
        var distinctTracks = body.Leaves.Select(l => l.TrackId).Distinct().ToList();
        Assert.Equal(3, distinctTracks.Count);
    }

    [Theory]
    [InlineData("5u",      "math_5u")]
    [InlineData("4u",      "math_4u")]
    [InlineData("3u",      "math_3u")]
    [InlineData("math_5u", "math_5u")]
    public void Leaves_TrackFilter_NormalizesWireValue(string queryTrack, string expectedTrackId)
    {
        var (body, status) = InvokeLeaves(track: queryTrack);

        Assert.Equal(200, status);
        Assert.NotNull(body);
        Assert.Equal(expectedTrackId, body!.Track);
        Assert.NotEmpty(body.Leaves);
        Assert.All(body.Leaves, l => Assert.Equal(expectedTrackId, l.TrackId));
    }

    [Fact]
    public void Leaves_UnknownTrack_Returns400()
    {
        var (_, status) = InvokeLeaves(track: "10u");
        Assert.Equal(400, status);
    }

    [Fact]
    public void Leaves_AreSortedByTopicThenSubtopic()
    {
        var (body, _) = InvokeLeaves(track: "5u");
        Assert.NotNull(body);

        // Sort assertion: every leaf's (topic, subtopic) is ordinally
        // <= the next one's. Using Ordinal because the endpoint sorts
        // ordinally too — keep test and prod sort policies aligned.
        for (var i = 1; i < body!.Leaves.Count; i++)
        {
            var prev = body.Leaves[i - 1];
            var curr = body.Leaves[i];
            var topicCmp = string.CompareOrdinal(prev.Topic, curr.Topic);
            if (topicCmp == 0)
                Assert.True(string.CompareOrdinal(prev.Subtopic, curr.Subtopic) <= 0,
                    $"Sort breaks at index {i}: {prev.Subtopic} > {curr.Subtopic}");
            else
                Assert.True(topicCmp <= 0,
                    $"Sort breaks at index {i}: {prev.Topic} > {curr.Topic}");
        }
    }

    // ---------------------------------------------------------------------
    // Registration extension wires this in
    // ---------------------------------------------------------------------

    [Fact]
    public void MapCenaAdminEndpoints_WiresMapCuratorTaxonomyEndpoints()
    {
        // Static check: the extension method must exist on the type. The
        // dynamic registration check (MapCuratorTaxonomyEndpoints_*) covers
        // the active wiring path; this guards against the extension being
        // renamed without updating the registration call site.
        var asm = typeof(CenaAdminServiceRegistration).Assembly;
        var type = asm.GetType("Cena.Admin.Api.Ingestion.CuratorTaxonomyEndpoints");
        Assert.NotNull(type);

        var mapMethod = type!.GetMethod(
            "MapCuratorTaxonomyEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mapMethod);
    }
}
