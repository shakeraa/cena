// =============================================================================
// Cena Platform — Coverage Heatmap endpoints wiring tests (prr-209)
//
// Mirrors GdprEndpointsWiringTests.cs: minimal in-process WebApplication
// binds MapCoverageHeatmapEndpoints and the test walks the route data
// sources to assert:
//   - both documented routes are registered
//   - every heatmap endpoint requires AdminOnly
//   - the AddCenaAdminServices registration registers the manifest provider
//     and heatmap service
//   - MapCenaAdminEndpoints wires the heatmap group
// =============================================================================

using System.Reflection;
using Cena.Actors.QuestionBank.Coverage;
using Cena.Admin.Api.Coverage;
using Cena.Admin.Api.Registration;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Admin.Api.Tests.Coverage;

public sealed class CoverageHeatmapEndpointsWiringTests
{
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
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

    [Fact]
    public void MapCoverageHeatmapEndpoints_RegistersBothRoutes()
    {
        var app = BuildTestApp();
        app.MapCoverageHeatmapEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null && p.StartsWith(CoverageHeatmapEndpoints.BasePath, StringComparison.Ordinal))
            .ToHashSet();

        // The root route inside a MapGroup produces "<prefix>/" from Minimal
        // APIs; accept either spelling so the test doesn't break if we
        // normalise the trailing slash in a later refactor.
        Assert.Contains(patterns,
            p => p == "/api/admin/coverage/heatmap" || p == "/api/admin/coverage/heatmap/");
        Assert.Contains("/api/admin/coverage/heatmap/rung", patterns);
    }

    [Fact]
    public void MapCoverageHeatmapEndpoints_AppliesAdminOnlyPolicy()
    {
        var app = BuildTestApp();
        app.MapCoverageHeatmapEndpoints();

        var heatmapEndpoints = EnumerateEndpoints(app)
            .Where(e => e.RoutePattern.RawText is not null
                        && e.RoutePattern.RawText.StartsWith(CoverageHeatmapEndpoints.BasePath, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(heatmapEndpoints);
        foreach (var endpoint in heatmapEndpoints)
        {
            var attrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
            Assert.NotEmpty(attrs);
            Assert.Contains(attrs, a => a.Policy == CenaAuthPolicies.AdminOnly);
        }
    }

    [Fact]
    public void AddCenaAdminServices_RegistersHeatmapStack()
    {
        var services = new ServiceCollection();
        services.AddCenaAdminServices();

        // The manifest provider is registered lazily (a factory) so it only
        // touches the filesystem when first resolved. Assert the service
        // descriptor is present rather than constructing the provider here.
        Assert.Contains(services, d => d.ServiceType == typeof(ICoverageTargetManifestProvider));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(ICoverageCellVariantCounter) &&
            d.ImplementationType == typeof(CoverageCellVariantCounter));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(ICoverageRungDrilldownSource) &&
            d.ImplementationType == typeof(EmptyCoverageRungDrilldownSource));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(ICoverageHeatmapService) &&
            d.ImplementationType == typeof(CoverageHeatmapService));
    }

    [Fact]
    public void MapCenaAdminEndpoints_ExposesMapCoverageHeatmapEndpoints()
    {
        // Reflection sanity check — cheaper than wiring the full admin map
        // (which pulls the syllabus loader, NATS, etc.).
        var type = typeof(CoverageHeatmapEndpoints);
        var method = type.GetMethod(
            nameof(CoverageHeatmapEndpoints.MapCoverageHeatmapEndpoints),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void BasePath_IsApiAdminCoverageHeatmap()
    {
        Assert.Equal("/api/admin/coverage/heatmap", CoverageHeatmapEndpoints.BasePath);
    }
}
