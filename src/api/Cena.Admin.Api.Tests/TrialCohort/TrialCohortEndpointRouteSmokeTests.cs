// =============================================================================
// Cena Platform — TrialCohortEndpoint route-smoke (Phase 4).
//
// Per the route-smoke memory: every minimal-API endpoint group needs a
// smoke test, because direct-handler unit tests miss [FromBody] +
// service-vs-body inference defects. Asserts:
//   1. GET /api/admin/cohorts/trial is registered
//   2. The endpoint requires ModeratorOrAbove
//   3. Default window (no from/to) returns 200 with metrics from reader
//   4. Invalid 'from' returns 400 with structured error
//   5. Inverted window (to <= from) returns 400
// =============================================================================

using System.Text.Json;
using Cena.Admin.Api.Features.TrialCohort;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Cena.Admin.Api.Tests.TrialCohort;

public sealed class TrialCohortEndpointRouteSmokeTests
{
    private const string Route = "/api/admin/cohorts/trial";

    private static (WebApplication App, ITrialCohortReader Reader) BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        var reader = Substitute.For<ITrialCohortReader>();

        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        builder.Services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("api", x =>
            {
                x.PermitLimit = 100;
                x.Window = TimeSpan.FromMinutes(1);
            });
        });
        builder.Services.AddSingleton(reader);
        builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);

        return (builder.Build(), reader);
    }

    private static List<RouteEndpoint> EnumerateEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void Endpoint_is_registered_and_requires_moderator_or_above()
    {
        var (app, _) = BuildTestApp();
        app.MapTrialCohortEndpoint();

        var endpoint = EnumerateEndpoints(app).Single(e => e.RoutePattern.RawText == Route);
        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
    }

    private static (int Status, string Body) Invoke(
        WebApplication app, string? from = null, string? to = null)
    {
        var endpoint = EnumerateEndpoints(app).Single(e => e.RoutePattern.RawText == Route);

        var ctx = new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() },
        };
        ctx.Request.Method = HttpMethods.Get;
        ctx.Request.Path = Route;

        if (from is not null || to is not null)
        {
            var qs = new List<string>();
            if (from is not null) qs.Add($"from={Uri.EscapeDataString(from)}");
            if (to is not null) qs.Add($"to={Uri.EscapeDataString(to)}");
            ctx.Request.QueryString = new QueryString("?" + string.Join("&", qs));
        }

        endpoint.RequestDelegate!(ctx).GetAwaiter().GetResult();
        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, new StreamReader(ctx.Response.Body).ReadToEnd());
    }

    [Fact]
    public void Default_window_returns_200_with_reader_metrics()
    {
        var (app, reader) = BuildTestApp();
        app.MapTrialCohortEndpoint();

        reader.GetMetricsAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new TrialCohortMetricsDto(
                WindowStart: DateTimeOffset.UtcNow.AddDays(-30),
                WindowEnd: DateTimeOffset.UtcNow,
                ActiveTrialsCount: 7,
                TrialsStartedInWindow: 12,
                TrialsConvertedInWindow: 4,
                TrialsExpiredInWindow: 1,
                ConversionRatePct: 80.0m,
                AvgDaysToConvert: 8.5m,
                MedianDaysToConvert: 7m,
                AvgTutorTurnsAtConvert: 23.0m,
                AvgPhotoDiagnosticsAtConvert: 4.5m));

        var (status, body) = Invoke(app);
        Assert.Equal(200, status);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(7, doc.RootElement.GetProperty("activeTrialsCount").GetInt32());
        Assert.Equal(12, doc.RootElement.GetProperty("trialsStartedInWindow").GetInt32());
        Assert.Equal(80.0m, doc.RootElement.GetProperty("conversionRatePct").GetDecimal());
    }

    [Fact]
    public void Invalid_from_returns_400_with_structured_error()
    {
        var (app, _) = BuildTestApp();
        app.MapTrialCohortEndpoint();

        var (status, body) = Invoke(app, from: "not-a-date", to: "2026-04-30");
        Assert.Equal(400, status);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("invalid_from", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Inverted_window_returns_400()
    {
        var (app, _) = BuildTestApp();
        app.MapTrialCohortEndpoint();

        var (status, body) = Invoke(app, from: "2026-05-01", to: "2026-04-01");
        Assert.Equal(400, status);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("invalid_window", doc.RootElement.GetProperty("error").GetString());
    }
}
