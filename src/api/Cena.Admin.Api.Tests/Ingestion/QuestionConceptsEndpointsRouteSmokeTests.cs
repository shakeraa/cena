// =============================================================================
// Cena Platform — Question Concepts endpoint route-smoke (ADR-0062 Phase 1)
//
// Per the route-smoke memory: every minimal-API endpoint group needs a
// smoke test, because direct-handler unit tests miss [FromBody] +
// service-vs-body inference defects. Asserts:
//   1. GET  /api/admin/ingestion/items/{id}/concepts is registered.
//   2. POST /api/admin/ingestion/items/{id}/concepts is registered.
//   3. Both routes are gated by the ModeratorOrAbove policy.
//   4. Both routes are gated by the "api" rate-limit bucket.
//   5. The POST body parameter is bound from request body, not from
//      services (the [FromBody] inference defect this test exists to
//      catch).
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class QuestionConceptsEndpointsRouteSmokeTests
{
    private const string ConceptsRoutePattern = "/api/admin/ingestion/items/{id}/concepts";
    private const string EnhanceRoutePattern  = "/api/admin/ingestion/items/{id}/enhance-text";

    private const string SyntheticTaxonomyJson = """
    {
      "version": "test",
      "tracks": {
        "math_5u": {
          "name": "5u",
          "topics": {
            "calculus": {
              "name": "Calculus",
              "subtopics": {
                "derivative_rules": { "conceptId": "CAL-003", "bloom_range": [3,5] }
              }
            }
          }
        }
      }
    }
    """;

    private static WebApplication BuildTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCenaAuthorization();
        builder.Services.AddRouting();
        // Antiforgery service must be registered for the POST endpoint's
        // .DisableAntiforgery() metadata to actually permit inferred body
        // binding under .NET 9 minimal APIs (the inference scan runs at
        // endpoint construction, BEFORE auth/rate limiting).
        builder.Services.AddAntiforgery();
        builder.Services.AddRateLimiter(o =>
        {
            o.AddFixedWindowLimiter("api", w =>
            {
                w.PermitLimit = 100;
                w.Window = TimeSpan.FromMinutes(1);
            });
        });
        // The endpoint resolves IDocumentStore + BagrutTaxonomyCatalog
        // from DI. The smoke test only inspects routing metadata, so
        // wire a synthetic catalog (no Marten — store dep stays
        // unresolved which is fine for routing-only assertions).
        builder.Services.AddSingleton(BagrutTaxonomyCatalog.Parse(SyntheticTaxonomyJson));
        return builder.Build();
    }

    private static List<RouteEndpoint> Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    [Fact]
    public void MapsBothRoutes()
    {
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var routes = Endpoints(app)
            .Where(e => e.RoutePattern.RawText == ConceptsRoutePattern)
            .ToList();

        // Two endpoints share the same pattern: one for GET, one for POST.
        Assert.Equal(2, routes.Count);
        var methods = routes
            .SelectMany(e => e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>())
            .SelectMany(m => m.HttpMethods)
            .ToHashSet();
        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    [Fact]
    public void MapsEnhanceTextRoute_ProtectedByModeratorOrAbove()
    {
        // ADR-0062 Phase 1.5 — POST /items/{id}/enhance-text. Curator-
        // initiated OCR cleanup pass; auth + rate-limit enforced by the
        // group, exception mapping happens inside the handler.
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var endpoint = Endpoints(app)
            .SingleOrDefault(e => e.RoutePattern.RawText == EnhanceRoutePattern);
        Assert.NotNull(endpoint);

        var methods = endpoint!.Metadata
            .GetOrderedMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
            .SelectMany(m => m.HttpMethods)
            .ToHashSet();
        Assert.Contains("POST", methods);

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);

        var rateLimits = endpoint.Metadata
            .GetOrderedMetadata<EnableRateLimitingAttribute>();
        Assert.NotEmpty(rateLimits);
        Assert.Contains(rateLimits, r => r.PolicyName == "api");
    }

    [Fact]
    public void GetRoute_RequiresModeratorOrAbove()
    {
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var endpoint = Endpoints(app)
            .Single(e =>
                e.RoutePattern.RawText == ConceptsRoutePattern
                && e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                    .Any(m => m.HttpMethods.Contains("GET")));

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
    }

    [Fact]
    public void PostRoute_RequiresModeratorOrAbove()
    {
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var endpoint = Endpoints(app)
            .Single(e =>
                e.RoutePattern.RawText == ConceptsRoutePattern
                && e.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                    .Any(m => m.HttpMethods.Contains("POST")));

        var authAttrs = endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>();
        Assert.NotEmpty(authAttrs);
        Assert.Contains(authAttrs, a => a.Policy == CenaAuthPolicies.ModeratorOrAbove);
    }

    [Fact]
    public void BothRoutes_UseApiRateLimitBucket()
    {
        var app = BuildTestApp();
        app.MapQuestionConceptsEndpoints();

        var conceptRoutes = Endpoints(app)
            .Where(e => e.RoutePattern.RawText == ConceptsRoutePattern)
            .ToList();

        Assert.Equal(2, conceptRoutes.Count);

        foreach (var endpoint in conceptRoutes)
        {
            var rateLimits = endpoint.Metadata
                .GetOrderedMetadata<EnableRateLimitingAttribute>();
            Assert.NotEmpty(rateLimits);
            Assert.Contains(rateLimits, r => r.PolicyName == "api");
        }
    }
}
