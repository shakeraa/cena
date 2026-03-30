// =============================================================================
// Cena Platform -- Diagram Admin Endpoints (LLM-009)
// =============================================================================

using Cena.Actors.Diagrams;
using Cena.Admin.Api.Validation;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class DiagramEndpoints
{
    public static IEndpointRouteBuilder MapDiagramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/diagrams")
            .WithTags("Diagram Generation")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("api");

        // POST /api/admin/diagrams/generate — Generate a diagram (with cache check)
        group.MapPost("/generate", async (
            DiagramGenerateRequest body,
            IDiagramGenerator generator,
            IDiagramCache cache) =>
        {
            if (string.IsNullOrWhiteSpace(body.Subject))
                return Results.BadRequest(new { error = "Subject is required." });
            if (string.IsNullOrWhiteSpace(body.Topic))
                return Results.BadRequest(new { error = "Topic is required." });
            if (string.IsNullOrWhiteSpace(body.Description))
                return Results.BadRequest(new { error = "Description is required." });

            var request = new DiagramRequest(
                Subject: body.Subject,
                Topic: body.Topic,
                DiagramType: body.DiagramType,
                Description: body.Description,
                Language: body.Language ?? "he");

            // Check cache first
            if (!body.SkipCache)
            {
                var cached = await cache.GetAsync(request);
                if (cached is not null)
                {
                    return Results.Ok(new DiagramGenerateResponse(
                        Id: cached.Id,
                        MermaidCode: cached.MermaidCode,
                        SvgContent: cached.SvgContent,
                        Description: cached.ResultDescription,
                        GeneratedAt: cached.CreatedAt,
                        FromCache: true));
                }
            }

            var result = await generator.GenerateAsync(request);
            var doc = await cache.StoreAsync(request, result);

            return Results.Ok(new DiagramGenerateResponse(
                Id: doc.Id,
                MermaidCode: result.MermaidCode,
                SvgContent: result.SvgContent,
                Description: result.Description,
                GeneratedAt: result.GeneratedAt,
                FromCache: false));
        }).WithName("GenerateDiagram");

        // GET /api/admin/diagrams/cache — List cached diagrams
        group.MapGet("/cache", async (
            int? page,
            int? pageSize,
            IDiagramCache cache) =>
        {
            var validPage = ParameterValidator.ValidatePage(page);
            var validPageSize = ParameterValidator.ValidatePageSize(pageSize);
            var items = await cache.ListAsync(validPage, validPageSize);

            return Results.Ok(new DiagramCacheListResponse(
                Items: items.Select(d => new DiagramCacheItem(
                    Id: d.Id,
                    Subject: d.Subject,
                    Topic: d.Topic,
                    DiagramType: d.DiagramType,
                    Description: d.Description,
                    Language: d.Language,
                    MermaidCode: d.MermaidCode,
                    CreatedAt: d.CreatedAt,
                    HitCount: d.HitCount)).ToList(),
                Page: validPage,
                PageSize: validPageSize));
        }).WithName("ListCachedDiagrams");

        // GET /api/admin/diagrams/{id} — Get specific diagram
        group.MapGet("/{id:guid}", async (
            Guid id,
            IDiagramCache cache) =>
        {
            var doc = await cache.GetByIdAsync(id);
            if (doc is null) return Results.NotFound();

            return Results.Ok(new DiagramCacheItem(
                Id: doc.Id,
                Subject: doc.Subject,
                Topic: doc.Topic,
                DiagramType: doc.DiagramType,
                Description: doc.Description,
                Language: doc.Language,
                MermaidCode: doc.MermaidCode,
                CreatedAt: doc.CreatedAt,
                HitCount: doc.HitCount));
        }).WithName("GetCachedDiagram");

        // DELETE /api/admin/diagrams/{id} — Remove from cache
        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDiagramCache cache) =>
        {
            var deleted = await cache.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).WithName("DeleteCachedDiagram");

        return app;
    }
}

// ── Request / Response DTOs ──

public sealed record DiagramGenerateRequest(
    string Subject,
    string Topic,
    DiagramType DiagramType,
    string Description,
    string? Language = "he",
    bool SkipCache = false);

public sealed record DiagramGenerateResponse(
    Guid Id,
    string MermaidCode,
    string SvgContent,
    string Description,
    DateTimeOffset GeneratedAt,
    bool FromCache);

public sealed record DiagramCacheListResponse(
    IReadOnlyList<DiagramCacheItem> Items,
    int Page,
    int PageSize);

public sealed record DiagramCacheItem(
    Guid Id,
    string Subject,
    string Topic,
    DiagramType DiagramType,
    string Description,
    string Language,
    string MermaidCode,
    DateTimeOffset CreatedAt,
    int HitCount);
