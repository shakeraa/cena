// =============================================================================
// Cena Platform -- Content Serving Endpoints (CNT-010)
// Student-facing endpoints for published questions, subjects, and explanations.
// Includes ETag-based caching and Cache-Control headers for CDN compatibility.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cena.Actors.Diagrams;
using Cena.Actors.Questions;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content")
            .WithTags("Content")
            .RequireAuthorization();

        // GET /api/content/questions/{id} — published question with translations
        group.MapGet("/questions/{id}", async (
            string id, HttpContext httpContext, IDocumentStore store) =>
        {
            await using var session = store.QuerySession();
            var question = await session.Query<QuestionState>()
                .FirstOrDefaultAsync(q => q.Id == id
                    && q.Status == QuestionLifecycleStatus.Published);

            if (question is null)
                return Results.NotFound(new { error = $"Question {id} not found or not published" });

            var etag = ComputeETag(id, question.EventVersion);
            if (IsNotModified(httpContext, etag))
                return Results.StatusCode(304);

            httpContext.Response.Headers.ETag = etag;
            httpContext.Response.Headers.CacheControl = "public, max-age=3600";
            httpContext.Response.Headers.Vary = "Accept-Language";

            return Results.Ok(new
            {
                questionId = question.Id,
                question.Subject,
                question.ConceptIds,
                question.Stem,
                question.Options,
                question.BloomsLevel,
                difficulty = question.Difficulty,
                question.LanguageVersions,
                question.Explanation,
                version = question.EventVersion
            });
        });

        // GET /api/content/questions/{id}/explanation — get explanation
        group.MapGet("/questions/{id}/explanation", async (
            string id, IDocumentStore store, HttpContext httpContext) =>
        {
            await using var session = store.QuerySession();
            var question = await session.Query<QuestionState>()
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question is null)
                return Results.NotFound(new { error = $"Question {id} not found" });

            var etag = ComputeETag($"expl-{id}", question.EventVersion);
            if (IsNotModified(httpContext, etag))
                return Results.StatusCode(304);

            httpContext.Response.Headers.ETag = etag;
            httpContext.Response.Headers.CacheControl = "public, max-age=7200";

            return Results.Ok(new
            {
                questionId = question.Id,
                explanation = question.Explanation ?? "",
                aiPrompt = question.AiProvenance?.PromptText,
                version = question.EventVersion
            });
        });

        // GET /api/content/subjects — list available subjects with question counts
        group.MapGet("/subjects", async (IDocumentStore store) =>
        {
            await using var session = store.QuerySession();
            var questions = await session.Query<QuestionState>()
                .Where(q => q.Status == QuestionLifecycleStatus.Published)
                .ToListAsync();

            var subjects = questions
                .GroupBy(q => q.Subject ?? "Unknown")
                .Select(g => new { subject = g.Key, questionCount = g.Count() })
                .OrderBy(s => s.subject)
                .ToList();

            return Results.Ok(new { subjects });
        });

        // GET /api/content/subjects/{subject}/topics — list topics for a subject
        group.MapGet("/subjects/{subject}/topics", async (
            string subject, IDocumentStore store) =>
        {
            await using var session = store.QuerySession();
            var questions = await session.Query<QuestionState>()
                .Where(q => q.Subject == subject
                    && q.Status == QuestionLifecycleStatus.Published)
                .ToListAsync();

            var topics = questions
                .SelectMany(q => q.ConceptIds.Select(c => new { conceptId = c, q }))
                .GroupBy(x => x.conceptId)
                .Select(g => new { conceptId = g.Key, questionCount = g.Count() })
                .OrderBy(t => t.conceptId)
                .ToList();

            return Results.Ok(new { subject, topics });
        });

        // GET /api/content/diagrams/{id} — serve cached diagram
        group.MapGet("/diagrams/{id:guid}", async (
            Guid id, HttpContext httpContext, IDiagramCache diagramCache) =>
        {
            var diagram = await diagramCache.GetByIdAsync(id);
            if (diagram is null)
                return Results.NotFound(new { error = "Diagram not found" });

            var etag = $"\"{diagram.Id}-{diagram.HitCount}\"";
            if (IsNotModified(httpContext, etag))
                return Results.StatusCode(304);

            httpContext.Response.Headers.ETag = etag;
            httpContext.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.Ok(new
            {
                diagram.Id,
                diagram.MermaidCode,
                diagram.SvgContent,
                diagram.Subject,
                diagram.Topic,
                diagram.DiagramType,
                diagram.ResultDescription,
                diagram.CreatedAt
            });
        });

        return app;
    }

    private static string ComputeETag(string key, int version)
    {
        var input = $"{key}:{version}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"\"{Convert.ToHexStringLower(hash[..8])}\"";
    }

    private static bool IsNotModified(HttpContext context, string etag)
    {
        if (context.Request.Headers.TryGetValue("If-None-Match", out var values))
        {
            return values.Any(v => v == etag);
        }
        return false;
    }
}
