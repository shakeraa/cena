// =============================================================================
// Cena Platform — /api/admin/ai/test-connection route + handler smoke
//
// Per the route-smoke memory: every minimal-API endpoint group needs a smoke
// test, because direct-handler unit tests miss [FromBody] + service-vs-body
// inference defects. The previous shape ([FromBody] AiProvider provider) was
// a real victim of this trap: the endpoint compiled, the service tests passed,
// but the SPA's ofetch client sent body: "Anthropic" as Content-Type:
// text/plain, which the model binder rejected with 415 BEFORE the probe ran,
// surfacing a bare "Failed" badge with no actionable category.
//
// Asserts:
//   1. Route registered at the expected pattern
//   2. Endpoint gated by ModeratorOrAbove (matches AI group policy)
//   3. Endpoint declares its accepted body type as TestConnectionRequest
//      (the wrapping fix for the ofetch text/plain trap)
//   4. HandleAsync maps probe success → connected:true with details
//   5. HandleAsync maps probe failure → connected:false with the typed
//      category code so the SPA can render an actionable hint
// =============================================================================

using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.AiSettings;

public sealed class TestConnectionEndpointRouteSmokeTests
{
    private const string TestConnectionRoutePattern = "/api/admin/ai/test-connection";

    private static (WebApplication App, IAiGenerationService Service) BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        var service = Substitute.For<IAiGenerationService>();

        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("ai", o =>
            {
                o.PermitLimit = 100;
                o.Window = TimeSpan.FromMinutes(1);
            });
        });
        builder.Services.AddSingleton(service);

        return (builder.Build(), service);
    }

    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void MapAiTestConnectionEndpoint_RegistersRoute()
    {
        var (app, _) = BuildTestApp();
        app.MapAiTestConnectionEndpoint();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet();

        Assert.Contains(TestConnectionRoutePattern, patterns);
    }

    [Fact]
    public void TestConnectionRoute_RequiresModeratorOrAbove()
    {
        var (app, _) = BuildTestApp();
        app.MapAiTestConnectionEndpoint();

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == TestConnectionRoutePattern);

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
    }

    [Fact]
    public void TestConnectionRoute_DeclaresWrappedRequestDtoAsBody()
    {
        // The point of this smoke is to lock the wire-shape fix. The previous
        // signature ([FromBody] AiProvider) caused ofetch's body: "Anthropic"
        // to be sent as text/plain → 415 → bare "Failed" in the SPA. The fix
        // wraps the enum in TestConnectionRequest so ofetch JSON-encodes
        // automatically. If someone reverts this to [FromBody] AiProvider
        // the metadata check below fails.
        var (app, _) = BuildTestApp();
        app.MapAiTestConnectionEndpoint();

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == TestConnectionRoutePattern);

        var acceptsMetadata = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
        Assert.NotNull(acceptsMetadata);
        Assert.Equal(typeof(TestConnectionRequest), acceptsMetadata.RequestType);
    }

    [Fact]
    public async Task HandleAsync_ProbeSuccess_ReturnsConnectedTrueWithDetails()
    {
        var service = Substitute.For<IAiGenerationService>();
        service.TestConnectionAsync(AiProvider.Anthropic, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ConnectionTestResult.Ok(
                "Authenticated. Model 'claude-sonnet-4-6' acknowledged the probe.")));

        var result = await AiTestConnectionEndpoint.HandleAsync(
            new TestConnectionRequest(AiProvider.Anthropic),
            service,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var payload = Assert.IsType<AiTestConnectionResponseAccessor>(
            new AiTestConnectionResponseAccessor(ok.Value!));
        Assert.True(payload.Connected);
        Assert.Null(payload.Error);
        Assert.Contains("acknowledged the probe", payload.Details);
    }

    [Fact]
    public async Task HandleAsync_ProbeFailure_PropagatesCategoryCode()
    {
        var service = Substitute.For<IAiGenerationService>();
        service.TestConnectionAsync(AiProvider.Anthropic, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                ConnectionTestResult.Fail("Invalid API key", "AUTH_FAILED")));

        var result = await AiTestConnectionEndpoint.HandleAsync(
            new TestConnectionRequest(AiProvider.Anthropic),
            service,
            CancellationToken.None);

        var ok = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var payload = new AiTestConnectionResponseAccessor(ok.Value!);
        Assert.False(payload.Connected);
        Assert.Equal("Invalid API key", payload.Error);
        // The stable category code MUST reach the SPA so it can render an
        // actionable hint instead of a bare "Failed" badge — that visibility
        // is the whole point of this fix.
        Assert.Equal("AUTH_FAILED", payload.Details);
    }

    /// <summary>
    /// Reflection-based reader for the anonymous response object the endpoint
    /// returns ({ connected, error, details }). The endpoint emits an anonymous
    /// type so the wire shape lives entirely in the handler — this accessor
    /// exists only so test assertions can read those fields without changing
    /// the production contract.
    /// </summary>
    private sealed class AiTestConnectionResponseAccessor
    {
        public bool Connected { get; }
        public string? Error { get; }
        public string? Details { get; }

        public AiTestConnectionResponseAccessor(object value)
        {
            var t = value.GetType();
            Connected = (bool)t.GetProperty("connected")!.GetValue(value)!;
            Error = (string?)t.GetProperty("error")!.GetValue(value);
            Details = (string?)t.GetProperty("details")!.GetValue(value);
        }
    }
}
