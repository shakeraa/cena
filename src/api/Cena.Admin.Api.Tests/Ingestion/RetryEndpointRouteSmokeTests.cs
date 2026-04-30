// =============================================================================
// Cena Platform — Retry endpoint route-smoke (PRR-RETRY-IMPL).
//
// Per the route-smoke memory: every minimal-API endpoint group needs a smoke
// test, because direct-handler unit tests miss [FromBody] + service-vs-body
// inference defects. Asserts:
//   1. POST /api/admin/ingestion/items/{id}/retry is registered
//   2. The endpoint is gated by the ModeratorOrAbove policy
//   3. The handler maps service results to expected status codes:
//        true  → 200
//        false → 404 (item not found)
//        BYTES_NOT_PERSISTED → 409 with error="bytes_not_persisted"
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class RetryEndpointRouteSmokeTests
{
    private const string RetryRoutePattern = "/api/admin/ingestion/items/{id}/retry";

    private static (WebApplication App, IIngestionPipelineService Service) BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        var service = Substitute.For<IIngestionPipelineService>();

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
        builder.Services.AddSingleton(service);

        return (builder.Build(), service);
    }

    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void MapIngestionPipelineEndpoints_RegistersRetryRoute()
    {
        var (app, _) = BuildTestApp();
        app.MapIngestionPipelineEndpoints();

        var patterns = EnumerateEndpoints(app)
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet();

        Assert.Contains(RetryRoutePattern, patterns);
    }

    [Fact]
    public void RetryRoute_RequiresModeratorOrAbove()
    {
        var (app, _) = BuildTestApp();
        app.MapIngestionPipelineEndpoints();

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == RetryRoutePattern);

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
    }

    private static (int Status, string Body) Invoke(
        WebApplication app, string id, RouteEndpoint endpoint)
    {
        // Use app.Services (which includes ILoggerFactory + the IIngestionPipelineService
        // we registered) so Results.Ok/NotFound/Json can resolve their dependencies.
        var ctx = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() },
        };
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = $"/api/admin/ingestion/items/{id}/retry";
        ctx.Request.RouteValues["id"] = id;

        endpoint.RequestDelegate!(ctx).GetAwaiter().GetResult();
        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, new StreamReader(ctx.Response.Body).ReadToEnd());
    }

    [Fact]
    public void Returns_200_When_Service_Returns_True()
    {
        var (app, service) = BuildTestApp();
        app.MapIngestionPipelineEndpoints();
        service.RetryItemAsync("pi-good").Returns(Task.FromResult(true));

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == RetryRoutePattern);

        var (status, _) = Invoke(app, "pi-good", endpoint);
        Assert.Equal(200, status);
    }

    [Fact]
    public void Returns_404_When_Item_Missing()
    {
        var (app, service) = BuildTestApp();
        app.MapIngestionPipelineEndpoints();
        service.RetryItemAsync("missing").Returns(Task.FromResult(false));

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == RetryRoutePattern);

        var (status, _) = Invoke(app, "missing", endpoint);
        Assert.Equal(404, status);
    }

    [Fact]
    public void Returns_409_With_BytesNotPersisted_Error_Body_For_Legacy_Items()
    {
        var (app, service) = BuildTestApp();
        app.MapIngestionPipelineEndpoints();
        service.RetryItemAsync("pi-legacy").Returns<Task<bool>>(_ =>
            throw new InvalidOperationException(
                "BYTES_NOT_PERSISTED: this pipeline item was uploaded before bytes-persistence was wired."));

        var endpoint = EnumerateEndpoints(app)
            .Single(e => e.RoutePattern.RawText == RetryRoutePattern);

        var (status, body) = Invoke(app, "pi-legacy", endpoint);
        Assert.Equal(409, status);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("bytes_not_persisted", doc.RootElement.GetProperty("error").GetString());
        Assert.Contains("BYTES_NOT_PERSISTED",
            doc.RootElement.GetProperty("message").GetString() ?? "");
    }
}
