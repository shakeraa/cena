// =============================================================================
// Cena Platform -- Knowledge/Content REST Endpoints (STB-08 Phase 1 + STB-08b)
// Concepts and learning path endpoints with real catalog data
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Content;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Content;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Api.Host.Endpoints;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("")
            .RequireAuthorization();

        // Content/Concepts endpoints
        group.MapGet("/api/v1/content/concepts", GetConcepts).WithName("GetConcepts").WithTags("Content")
    .Produces<ConceptListDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/api/v1/content/concepts/{id}", GetConceptDetail).WithName("GetConceptDetail").WithTags("Content")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/api/v1/content/concepts/{id}/graph", GetConceptGraph).WithName("GetConceptGraph").WithTags("Content")
    .Produces<ConceptGraphDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/api/v1/content/search", SearchConcepts).WithName("SearchConcepts").WithTags("Content")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/api/v1/content/bagrut", GetBagrutConcepts).WithName("GetBagrutConcepts").WithTags("Content")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        
        // Learning paths
        group.MapGet("/api/v1/content/paths", GetLearningPaths).WithName("GetLearningPaths").WithTags("Content")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);
        group.MapGet("/api/v1/content/paths/{id}", GetLearningPathDetail).WithName("GetLearningPathDetail").WithTags("Content")
    .Produces<LearningPathDetailDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // Knowledge path endpoint
        group.MapGet("/api/v1/knowledge/path", GetKnowledgePath).WithName("GetKnowledgePath").WithTags("Knowledge")
    .Produces<PathDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    // GET /api/content/concepts — returns list of concepts
    private static async Task<IResult> GetConcepts(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string? subject = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var concepts = string.IsNullOrEmpty(subject)
            ? await catalog.GetConceptsBySubjectAsync("Mathematics") // Default
            : await catalog.GetConceptsBySubjectAsync(subject);

        var dtos = concepts.Select(c => new ConceptSummary(
            c.ConceptId,
            c.Name,
            c.Subject,
            c.Topics.FirstOrDefault() ?? "General",
            MapDifficulty(c.Difficulty),
            "available" // Status would come from student progress
        )).ToArray();

        var dto = new ConceptListDto(Items: dtos);
        return Results.Ok(dto);
    }

    // GET /api/content/concepts/{id} — returns concept detail
    private static async Task<IResult> GetConceptDetail(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var concept = await catalog.GetConceptAsync(id);
        if (concept == null)
            return Results.NotFound(new { Error = "Concept not found" });

        var dto = MapToDetailDto(concept);
        return Results.Ok(dto);
    }

    // GET /api/content/concepts/{id}/graph — returns concept graph
    private static async Task<IResult> GetConceptGraph(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var graph = await catalog.GetConceptGraphAsync(id);
        if (graph == null)
            return Results.NotFound(new { Error = "Concept not found" });

        var dto = new ConceptGraphDto(
            Concept: MapToDetailDto(graph.Concept),
            Prerequisites: graph.Prerequisites.Select(MapToSummaryDto).ToArray(),
            Successors: graph.Successors.Select(MapToSummaryDto).ToArray(),
            Parents: graph.Parents.Select(MapToSummaryDto).ToArray()
        );

        return Results.Ok(dto);
    }

    // GET /api/content/search?q={query} — search concepts
    private static async Task<IResult> SearchConcepts(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string q,
        string? subject = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest(new { Error = "Query parameter 'q' is required" });

        var concepts = await catalog.SearchConceptsAsync(q, subject);
        var dtos = concepts.Select(MapToSummaryDto).ToArray();

        return Results.Ok(new { Items = dtos, Count = dtos.Length });
    }

    // GET /api/content/bagrut — Bagrut-relevant concepts
    private static async Task<IResult> GetBagrutConcepts(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string? subject = null,
        int? limit = 20)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var concepts = await catalog.GetBagrutConceptsAsync(subject, limit ?? 20);
        var dtos = concepts.Select(c => new BagrutConceptDto(
            c.ConceptId,
            c.Name,
            c.Subject,
            c.BagrutFrequencyScore,
            MapDifficulty(c.Difficulty)
        )).ToArray();

        return Results.Ok(new { Items = dtos, Count = dtos.Length });
    }

    // GET /api/content/paths — returns learning paths
    private static async Task<IResult> GetLearningPaths(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string? subject = null,
        string? grade = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var paths = await catalog.GetLearningPathsAsync(subject, grade);
        var dtos = paths.Select(p => new LearningPathSummaryDto(
            p.PathId,
            p.Name,
            p.Description,
            p.Subject,
            p.TargetGrade,
            p.Difficulty,
            p.EstimatedHours,
            p.Concepts.Count
        )).ToArray();

        return Results.Ok(new { Items = dtos, Count = dtos.Length });
    }

    // GET /api/content/paths/{id} — returns learning path detail
    private static async Task<IResult> GetLearningPathDetail(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var path = await catalog.GetLearningPathDetailAsync(id);
        if (path == null)
            return Results.NotFound(new { Error = "Learning path not found" });

        var dto = new LearningPathDetailDto(
            PathId: path.Path.PathId,
            Name: path.Path.Name,
            Description: path.Path.Description,
            Subject: path.Path.Subject,
            TargetGrade: path.Path.TargetGrade,
            Difficulty: path.Path.Difficulty,
            EstimatedHours: path.Path.EstimatedHours,
            Concepts: path.Concepts.Select(c => new PathConceptDto(
                c.Concept.ConceptId,
                c.Concept.Name,
                c.SequenceOrder,
                c.IsRequired,
                MapDifficulty(c.Concept.Difficulty)
            )).ToArray()
        );

        return Results.Ok(dto);
    }

    // GET /api/knowledge/path?from={conceptA}&to={conceptB} — returns learning path
    private static async Task<IResult> GetKnowledgePath(
        HttpContext ctx,
        [FromServices] IContentCatalogService catalog,
        string? from = null,
        string? to = null)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return Results.BadRequest(new { Error = "Both 'from' and 'to' concept IDs are required" });
        }

        // Get concept graph for the 'from' concept to build path
        var graph = await catalog.GetConceptGraphAsync(from);
        if (graph == null)
            return Results.NotFound(new { Error = "Source concept not found" });

        // Build path using BFS through successor relationships
        var path = BuildPath(graph, to);
        
        if (path == null)
        {
            // Return direct path if no graph path found
            path = new[] { from, to };
        }

        // Load concept details for each step
        var nodes = new List<PathNode>();
        for (int i = 0; i < path.Length; i++)
        {
            var concept = await catalog.GetConceptAsync(path[i]);
            if (concept != null)
            {
                nodes.Add(new PathNode(
                    concept.ConceptId,
                    concept.Name,
                    i + 1,
                    "available" // Would be based on student progress
                ));
            }
        }

        var edges = new List<PathEdge>();
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            edges.Add(new PathEdge(nodes[i].ConceptId, nodes[i + 1].ConceptId, "dependency"));
        }

        var dto = new PathDto(
            FromConceptId: from,
            ToConceptId: to,
            Nodes: nodes.ToArray(),
            Edges: edges.ToArray(),
            TotalSteps: nodes.Count,
            EstimatedMinutes: nodes.Count * 40);

        return Results.Ok(dto);
    }

    private static string[]? BuildPath(ConceptGraph startGraph, string targetId)
    {
        // BFS to find path from start to target
        var visited = new HashSet<string> { startGraph.Concept.ConceptId };
        var queue = new Queue<List<string>>();
        queue.Enqueue(new List<string> { startGraph.Concept.ConceptId });

        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var current = path.Last();

            if (current == targetId)
                return path.ToArray();

            // Get successors of current
            var currentGraph = startGraph.Successors.FirstOrDefault(s => s.ConceptId == current);
            if (currentGraph == null && current == startGraph.Concept.ConceptId)
                currentGraph = startGraph.Concept;

            if (currentGraph != null)
            {
                foreach (var successor in startGraph.Successors)
                {
                    if (!visited.Contains(successor.ConceptId))
                    {
                        visited.Add(successor.ConceptId);
                        var newPath = new List<string>(path) { successor.ConceptId };
                        queue.Enqueue(newPath);
                    }
                }
            }
        }

        return null;
    }

    private static ConceptDetailDto MapToDetailDto(Cena.Infrastructure.Documents.ConceptDocument c)
    {
        return new ConceptDetailDto(
            c.ConceptId,
            c.Name,
            c.Description ?? $"Learn about {c.Name}",
            c.Subject,
            c.Topics.FirstOrDefault() ?? "General",
            MapDifficulty(c.Difficulty),
            "available",
            null, // Mastery would come from student progress
            c.PrerequisiteIds.ToArray(),
            c.SuccessorIds.ToArray(),
            c.EstimatedQuestionsToMaster * 3, // ~3 min per question
            c.EstimatedQuestionsToMaster
        );
    }

    private static ConceptSummary MapToSummaryDto(Cena.Infrastructure.Documents.ConceptDocument c)
    {
        return new ConceptSummary(
            c.ConceptId,
            c.Name,
            c.Subject,
            c.Topics.FirstOrDefault() ?? "General",
            MapDifficulty(c.Difficulty),
            "available"
        );
    }

    private static string MapDifficulty(double difficulty)
    {
        return difficulty switch
        {
            < 0.35 => "beginner",
            < 0.65 => "intermediate",
            _ => "advanced"
        };
    }

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }
}

// Additional DTOs
public record ConceptGraphDto(
    ConceptDetailDto Concept,
    ConceptSummary[] Prerequisites,
    ConceptSummary[] Successors,
    ConceptSummary[] Parents);

public record BagrutConceptDto(
    string ConceptId,
    string Name,
    string Subject,
    int BagrutFrequency,
    string Difficulty);

public record LearningPathSummaryDto(
    string PathId,
    string Name,
    string Description,
    string Subject,
    string TargetGrade,
    string Difficulty,
    int EstimatedHours,
    int ConceptCount);

public record LearningPathDetailDto(
    string PathId,
    string Name,
    string Description,
    string Subject,
    string TargetGrade,
    string Difficulty,
    int EstimatedHours,
    PathConceptDto[] Concepts);

public record PathConceptDto(
    string ConceptId,
    string Name,
    int SequenceOrder,
    bool IsRequired,
    string Difficulty);
