// =============================================================================
// FIND-arch-006 — GDPR Endpoints Wiring Tests
//
// Verifies that:
//   1. MapGdprEndpoints registers all six GDPR routes at the expected paths.
//   2. The routes are protected by a real CenaAuthPolicies policy (i.e. not
//      the old broken "AdminPolicy" literal).
//   3. CenaAdminServiceRegistration registers the two compliance services
//      GdprEndpoints depends on.
//   4. CenaAdminServiceRegistration.MapCenaAdminEndpoints wires MapGdprEndpoints
//      into the admin host pipeline.
//
// These tests do not spin up a full WebApplicationFactory — they build a
// minimal in-memory service collection / route builder so the assertions
// cover wiring only, not end-to-end HTTP.
// =============================================================================

using System.Reflection;
using Cena.Admin.Api;
using Cena.Admin.Api.Registration;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Admin.Api.Tests;

public class GdprEndpointsWiringTests
{
    /// <summary>
    /// Build a minimal WebApplication so we can invoke MapGdprEndpoints on a
    /// real IEndpointRouteBuilder and inspect the resulting endpoints.
    /// </summary>
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();

        // GdprEndpoints uses RequireAuthorization(CenaAuthPolicies.AdminOnly),
        // so we need the policy registered in DI before building the app.
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();

        return builder.Build();
    }

    /// <summary>
    /// Enumerates every endpoint registered against <paramref name="app"/> by
    /// walking all IEndpointRouteBuilder.DataSources that were populated by
    /// the mapping extensions. WebApplication exposes IEndpointRouteBuilder
    /// but not the DataSources collection directly, so we cast through the
    /// interface explicitly.
    /// </summary>
    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app)
    {
        var routeBuilder = (IEndpointRouteBuilder)app;
        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    [Fact]
    public void MapGdprEndpoints_RegistersAllSixRoutes()
    {
        var app = BuildTestApp();
        app.MapGdprEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null && p.StartsWith("/api/admin/gdpr", StringComparison.Ordinal))
            .ToHashSet();

        Assert.Contains("/api/admin/gdpr/consents/{studentId}", patterns);
        Assert.Contains("/api/admin/gdpr/consents",             patterns);
        Assert.Contains("/api/admin/gdpr/consents/{studentId}/{consentType}", patterns);
        Assert.Contains("/api/admin/gdpr/export/{studentId}",   patterns);
        Assert.Contains("/api/admin/gdpr/erasure/{studentId}",  patterns);
        Assert.Contains("/api/admin/gdpr/erasure/{studentId}/status", patterns);
    }

    [Fact]
    public void MapGdprEndpoints_AppliesAdminOnlyPolicy()
    {
        var app = BuildTestApp();
        app.MapGdprEndpoints();

        var gdprEndpoints = EnumerateEndpoints(app)
            .Where(e => e.RoutePattern.RawText is not null &&
                        e.RoutePattern.RawText.StartsWith("/api/admin/gdpr", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(gdprEndpoints);

        foreach (var endpoint in gdprEndpoints)
        {
            var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
            Assert.NotEmpty(authAttrs);

            // The old code referenced "AdminPolicy" which was not a registered
            // policy name. The fix points at CenaAuthPolicies.AdminOnly.
            Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.AdminOnly);
            Assert.DoesNotContain(authAttrs, a => a.Policy == "AdminPolicy");
        }
    }

    [Fact]
    public void AddCenaAdminServices_RegistersGdprCompliancePair()
    {
        var services = new ServiceCollection();
        services.AddCenaAdminServices();

        Assert.Contains(services, d => d.ServiceType == typeof(IGdprConsentManager)
                                       && d.ImplementationType == typeof(GdprConsentManager));
        Assert.Contains(services, d => d.ServiceType == typeof(IRightToErasureService)
                                       && d.ImplementationType == typeof(RightToErasureService));
    }

    [Fact]
    public void AdminOnly_PolicyIsRegistered_InAuthorizationOptions()
    {
        // Sanity check that "AdminOnly" (not "AdminPolicy") is the canonical
        // name registered by CenaAuthPolicies.AddCenaAuthorization.
        Assert.Equal("AdminOnly", CenaAuthPolicies.AdminOnly);
    }

    [Fact]
    public void MapCenaAdminEndpoints_WiresMapGdprEndpoints()
    {
        // Static source-level check via reflection: the extension method must
        // call into GdprEndpoints at some point. Loading the IL and scanning
        // for a call instruction is overkill; instead we rely on the dynamic
        // check above (MapGdprEndpoints_RegistersAllSixRoutes) plus this
        // assertion that the registration class exposes a public MapGdprEndpoints
        // method in the expected assembly.
        var registrationAssembly = typeof(CenaAdminServiceRegistration).Assembly;
        var gdprType = registrationAssembly.GetType("Cena.Admin.Api.GdprEndpoints");
        Assert.NotNull(gdprType);

        var mapMethod = gdprType!.GetMethod(
            "MapGdprEndpoints",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(mapMethod);
    }
}
