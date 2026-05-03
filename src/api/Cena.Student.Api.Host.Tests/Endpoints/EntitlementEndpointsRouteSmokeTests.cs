// =============================================================================
// Cena Platform — EntitlementEndpointsRouteSmokeTests (Phase 1D-fix-2 iter 4 item 5-2)
//
// Catches the regression class direct-method-invocation tests can't see:
// "the route never registered." Calls MapEntitlementEndpoints on a real
// WebApplication's IEndpointRouteBuilder and asserts the routes show up
// in DataSources. If MapPost/MapGet ever throws at startup (e.g. due to a
// signature change in a handler that the binder can't resolve), this test
// fails — the unit-style endpoint tests would still pass with a stale DLL.
// =============================================================================

using System.Reflection;
using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class EntitlementEndpointsRouteSmokeTests
{
    [Fact]
    public void MapEntitlementEndpoints_registers_both_routes_on_builder()
    {
        var app = BuildAppWithFakeServices();
        app.MapEntitlementEndpoints();

        // WebApplication exposes its registered endpoints via the
        // IEndpointRouteBuilder.DataSources collection. Each MapGet/MapPost
        // adds a ModelEndpointDataSource; aggregating all endpoints across
        // every data source gives us the registered routes without
        // requiring the full pipeline (UseRouting/UseEndpoints) to run.
        var routePatterns = AllRegisteredRoutes(app)
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(EntitlementEndpoints.EntitlementRoute, routePatterns);
        Assert.Contains(EntitlementEndpoints.StartTrialRoute, routePatterns);
    }

    [Fact]
    public void MapEntitlementEndpoints_registers_routes_with_authorization_metadata()
    {
        var app = BuildAppWithFakeServices();
        app.MapEntitlementEndpoints();

        var endpoints = AllRegisteredRoutes(app)
            .Where(e =>
                string.Equals(e.RoutePattern.RawText,
                    EntitlementEndpoints.EntitlementRoute,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.RoutePattern.RawText,
                    EntitlementEndpoints.StartTrialRoute,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, endpoints.Count);
        foreach (var ep in endpoints)
        {
            // RequireAuthorization() adds an IAuthorizeData metadata entry.
            // Locking presence here so a future regression that strips
            // .RequireAuthorization() fails CI rather than shipping anon
            // access to billing endpoints.
            var hasAuth = ep.Metadata
                .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Any();
            Assert.True(hasAuth,
                $"Route {ep.RoutePattern.RawText} is missing RequireAuthorization metadata.");
        }
    }

    /// <summary>
    /// Build a WebApplication wired with NSubstitute fakes for every service
    /// EntitlementEndpoints' handlers depend on. The route binder needs the
    /// DI container to know each handler parameter is a service (vs body)
    /// — without registrations, parameter inference fails. We don't need
    /// the fakes to do anything — the test just exercises route shape.
    /// </summary>
    private static WebApplication BuildAppWithFakeServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Mock.Of<IStudentEntitlementResolver>());
        builder.Services.AddSingleton(Mock.Of<IStudentTrialConsumptionStore>());
        builder.Services.AddSingleton(Mock.Of<ISubscriptionAggregateStore>());
        builder.Services.AddSingleton(Mock.Of<ITrialAllotmentConfigStore>());
        builder.Services.AddSingleton(Mock.Of<ITrialFingerprintLedgerStore>());
        builder.Services.AddSingleton(Mock.Of<IPaymentMethodSetupProvider>());
        builder.Services.AddSingleton(Mock.Of<IParentChildBindingStore>());
        builder.Services.AddSingleton(new DiscountAssignmentService(
            new InMemoryDiscountAssignmentStore(),
            new InMemoryDiscountCouponProvider(),
            new NullDiscountIssuedEmailDispatcher(),
            TimeProvider.System));
        builder.Services.AddSingleton(new EncryptedFieldAccessor(
            new InMemorySubjectKeyStore(SubjectKeyDerivation.FromEnvironment())));
        builder.Services.AddSingleton(TimeProvider.System);
        return builder.Build();
    }

    /// <summary>
    /// Walk the WebApplication's IEndpointRouteBuilder.DataSources to gather
    /// every RouteEndpoint registered via MapGet/MapPost/etc. Avoids the
    /// composite EndpointDataSource which requires UseRouting() + a built
    /// pipeline.
    /// </summary>
    private static IReadOnlyList<RouteEndpoint> AllRegisteredRoutes(WebApplication app)
    {
        // IEndpointRouteBuilder is exposed via the internal DataSources list
        // — accessible by reflection on WebApplication for test purposes.
        var dataSourcesProp = typeof(WebApplication)
            .GetProperty("DataSources",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var dataSources = dataSourcesProp?.GetValue(app)
            as ICollection<EndpointDataSource>
            ?? Array.Empty<EndpointDataSource>();
        return dataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }
}
