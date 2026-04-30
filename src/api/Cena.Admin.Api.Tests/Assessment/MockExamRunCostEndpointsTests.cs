// =============================================================================
// PRR-322 — Mock-exam run cost endpoints route-smoke test
//
// Confirms MapMockExamRunCostEndpoints registers the 3 GETs at the
// expected paths and applies the ModeratorOrAbove policy. Direct-handler
// behaviour is unit-tested separately (the LINQ + computation paths are
// against Marten's QuerySession, which a route-smoke can't easily exercise
// without a fixture); the integration test against live cena-postgres
// covers end-to-end. Per the route-smoke memory: every minimal-API group
// gets a wiring test even when behaviour is covered elsewhere.
// =============================================================================

using Cena.Admin.Api;
using Cena.Admin.Api.Assessment;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Assessment;

public class MockExamRunCostEndpointsTests
{
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
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
        // The handler resolves IDocumentStore — in this wiring test we
        // never invoke the handler body (only inspect registered routes
        // / metadata) so a substitute is fine.
        builder.Services.AddSingleton(Substitute.For<IDocumentStore>());
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
    public void MapMockExamRunCostEndpoints_RegistersThreeRoutes()
    {
        var app = BuildTestApp();
        app.MapMockExamRunCostEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null && p.StartsWith("/api/admin/mock-exam-runs/cost", StringComparison.Ordinal))
            .ToHashSet();

        Assert.Contains("/api/admin/mock-exam-runs/cost/runs",       patterns);
        Assert.Contains("/api/admin/mock-exam-runs/cost/daily",      patterns);
        Assert.Contains("/api/admin/mock-exam-runs/cost/projection", patterns);
    }

    [Fact]
    public void MapMockExamRunCostEndpoints_AppliesModeratorOrAbovePolicy()
    {
        var app = BuildTestApp();
        app.MapMockExamRunCostEndpoints();

        var endpoints = EnumerateEndpoints(app)
            .Where(e => e.RoutePattern.RawText is not null &&
                        e.RoutePattern.RawText.StartsWith("/api/admin/mock-exam-runs/cost", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(3, endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
            Assert.NotEmpty(authAttrs);
            Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
        }
    }

    [Fact]
    public void MockExamRunCostEndpoints_ExtensionMethod_ExistsOnExpectedType()
    {
        // Static-shape guard: the registration extension method must
        // exist on this exact type so the call site in
        // CenaAdminServiceRegistration.MapCenaAdminEndpoints binds.
        // Catches accidental rename / namespace move.
        var asm = typeof(MockExamRunCostEndpoints).Assembly;
        var type = asm.GetType("Cena.Admin.Api.Assessment.MockExamRunCostEndpoints");
        Assert.NotNull(type);

        var mapMethod = type!.GetMethod(
            "MapMockExamRunCostEndpoints",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(mapMethod);
    }
}
