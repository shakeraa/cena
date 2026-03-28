// =============================================================================
// Cena Platform -- Mastery REST API Endpoints
// MST-017: Minimal API endpoints for mastery data (REST + SignalR, not GraphQL)
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Students;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Proto;
using Proto.Cluster;

namespace Cena.Actors.Api;

/// <summary>
/// Minimal API endpoint registration for mastery data.
/// All endpoints query the StudentActor via Proto.Actor grain client.
/// </summary>
public static class MasteryEndpoints
{
    /// <summary>
    /// Register all mastery REST endpoints. Call from Program.cs after app.Build().
    /// </summary>
    public static IEndpointRouteBuilder MapMasteryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/mastery")
            .WithTags("Mastery");

        // GET /api/v1/mastery/{studentId}?subject=math
        group.MapGet("/{studentId}", async (string studentId, string? subject, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var graphCache = ctx.RequestServices.GetService<IConceptGraphCache>();
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var response = MasteryApiService.BuildStudentMastery(
                studentId, overlay, graphCache, DateTimeOffset.UtcNow, subject);
            return Results.Ok(response);
        })
        .WithName("GetStudentMasteryV1")
        .RequireAuthorization();

        // GET /api/v1/mastery/{studentId}/topics/{topicClusterId}
        group.MapGet("/{studentId}/topics/{topicClusterId}", async (string studentId, string topicClusterId, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var graphCache = ctx.RequestServices.GetRequiredService<IConceptGraphCache>();
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var progress = MasteryApiService.BuildTopicProgress(
                overlay, graphCache, topicClusterId, DateTimeOffset.UtcNow);
            return Results.Ok(progress);
        })
        .WithName("GetTopicProgress")
        .RequireAuthorization();

        // GET /api/v1/mastery/{studentId}/frontier?maxResults=10
        group.MapGet("/{studentId}/frontier", async (string studentId, int? maxResults, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var graphCache = ctx.RequestServices.GetRequiredService<IConceptGraphCache>();
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var frontier = MasteryApiService.BuildFrontier(
                overlay, graphCache, DateTimeOffset.UtcNow, maxResults ?? 10);
            return Results.Ok(frontier);
        })
        .WithName("GetLearningFrontier")
        .RequireAuthorization();

        // GET /api/v1/mastery/{studentId}/decay-alerts
        group.MapGet("/{studentId}/decay-alerts", async (string studentId, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var graphCache = ctx.RequestServices.GetRequiredService<IConceptGraphCache>();
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var alerts = MasteryApiService.BuildDecayAlerts(
                overlay, graphCache, DateTimeOffset.UtcNow);
            return Results.Ok(alerts);
        })
        .WithName("GetDecayAlerts")
        .RequireAuthorization();

        // GET /api/v1/mastery/{studentId}/methodology-profile
        group.MapGet("/{studentId}/methodology-profile", async (string studentId, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var result = await QueryStudentActor<GetMethodologyProfile, ActorResult<MethodologyProfileResponse>>(
                actorSystem, studentId, new GetMethodologyProfile(studentId));

            if (result?.Success != true)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            return Results.Ok(result.Data);
        })
        .WithName("GetMethodologyProfile")
        .RequireAuthorization();

        // POST /api/v1/mastery/{studentId}/methodology-override
        group.MapPost("/{studentId}/methodology-override", async (
            string studentId,
            MethodologyOverrideRequest body,
            HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var teacherId = ctx.User.FindFirst("sub")?.Value ?? "unknown";

            var cmd = new TeacherMethodologyOverride(
                studentId, body.Level, body.LevelId, body.Methodology, teacherId);

            var result = await QueryStudentActor<TeacherMethodologyOverride, ActorResult>(
                actorSystem, studentId, cmd);

            if (result?.Success != true)
                return Results.BadRequest(new { error = result?.ErrorMessage ?? "Override failed" });

            return Results.Ok(new { message = "Methodology override applied" });
        })
        .WithName("PostMethodologyOverride")
        .RequireAuthorization();

        // GET /api/v1/mastery/{studentId}/review-schedule?maxItems=10
        group.MapGet("/{studentId}/review-schedule", async (string studentId, int? maxItems, HttpContext ctx) =>
        {
            var actorSystem = ctx.RequestServices.GetRequiredService<ActorSystem>();
            var result = await QueryStudentActor<GetReviewSchedule, ActorResult<IReadOnlyList<ReviewItem>>>(
                actorSystem, studentId, new GetReviewSchedule(studentId, maxItems ?? 10));

            if (result?.Success != true)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            return Results.Ok(result.Data);
        })
        .WithName("GetReviewSchedule")
        .RequireAuthorization();

        return app;
    }

    // =========================================================================
    // ACTOR COMMUNICATION HELPERS
    // =========================================================================

    private static async Task<IReadOnlyDictionary<string, ConceptMasteryState>?> GetMasteryOverlay(
        ActorSystem actorSystem, string studentId)
    {
        try
        {
            var identity = ClusterIdentity.Create(studentId, "student");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var result = await actorSystem.Cluster()
                .RequestAsync<ActorResult<MasteryOverlayResponse>>(
                    identity,
                    new GetMasteryOverlayQuery(studentId),
                    cts.Token);

            return result?.Success == true ? result.Data?.Overlay : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<TResult?> QueryStudentActor<TQuery, TResult>(
        ActorSystem actorSystem, string studentId, TQuery query)
        where TResult : class
    {
        try
        {
            var identity = ClusterIdentity.Create(studentId, "student");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            return await actorSystem.Cluster()
                .RequestAsync<TResult>(identity, query!, cts.Token);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Request body for teacher/admin methodology override.
/// </summary>
public sealed record MethodologyOverrideRequest(
    string Level,       // "Subject", "Topic", "Concept"
    string LevelId,
    string Methodology);
