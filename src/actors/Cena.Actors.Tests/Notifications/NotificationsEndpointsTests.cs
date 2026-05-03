// =============================================================================
// Cena Platform — Notifications Endpoints Tests (PWA-BE-002)
// =============================================================================

using System.Security.Claims;
using Cena.Api.Host.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Actors.Tests.Notifications;

public sealed class NotificationsEndpointsTests
{
    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<Marten.IDocumentStore>(Substitute.For<Marten.IDocumentStore>());
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

    // ─────────────────────────────────────────────────────────────────────────
    // VAPID public key
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetVapidPublicKey_ReturnsKey_WhenConfigured()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebPush:VapidPublicKey"] = "test-public-key-123"
            })
            .Build();

        var result = NotificationsEndpoints.GetVapidPublicKey(config);

        var statusResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status200OK, statusResult.StatusCode);
    }

    [Fact]
    public void GetVapidPublicKey_ReturnsNotFound_WhenMissing()
    {
        var config = new ConfigurationBuilder().Build();

        var result = NotificationsEndpoints.GetVapidPublicKey(config);

        var statusResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Route registration
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MapNotificationsEndpoints_RegistersWebPushRoutes()
    {
        var app = BuildTestApp();
        app.MapNotificationsEndpoints();

        var endpoints = EnumerateEndpoints(app);
        var routes = endpoints.Select(e => e.RoutePattern.RawText).ToList();

        Assert.Contains("/api/notifications/web-push/subscribe", routes);
        Assert.Contains("/api/notifications/web-push/unsubscribe", routes);
    }

    [Fact]
    public void MapNotificationsEndpoints_RegistersPreferenceRoutes()
    {
        var app = BuildTestApp();
        app.MapNotificationsEndpoints();

        var endpoints = EnumerateEndpoints(app);
        var routes = endpoints.Select(e => e.RoutePattern.RawText).ToList();

        Assert.Contains("/api/notifications/preferences", routes);
    }

    [Fact]
    public void MapNotificationsEndpoints_RegistersVapidKeyRoute()
    {
        var app = BuildTestApp();
        app.MapNotificationsEndpoints();

        var endpoints = EnumerateEndpoints(app);
        var routes = endpoints.Select(e => e.RoutePattern.RawText).ToList();

        Assert.Contains("/api/notifications/vapid-key", routes);
    }

    [Fact]
    public void MapNotificationsEndpoints_VapidKey_AllowsAnonymous()
    {
        var app = BuildTestApp();
        app.MapNotificationsEndpoints();

        var endpoint = EnumerateEndpoints(app)
            .FirstOrDefault(e => e.RoutePattern.RawText == "/api/notifications/vapid-key");

        Assert.NotNull(endpoint);

        var authMetadata = endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>();
        Assert.Empty(authMetadata);
    }
}
