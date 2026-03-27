// =============================================================================
// Cena Platform -- Mastery REST API Endpoints
// MST-017: Minimal API endpoints for mastery data (REST + SignalR, not GraphQL)
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Students;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        group.MapGet("/{studentId}", async (
            string studentId,
            string? subject,
            ActorSystem actorSystem,
            IConceptGraphCache? graphCache) =>
        {
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var response = MasteryApiService.BuildStudentMastery(
                studentId, overlay, graphCache, DateTimeOffset.UtcNow, subject);
            return Results.Ok(response);
        })
        .WithName("GetStudentMastery")
        .WithDescription("Get full mastery overlay for a student");

        // GET /api/v1/mastery/{studentId}/topics/{topicClusterId}
        group.MapGet("/{studentId}/topics/{topicClusterId}", async (
            string studentId,
            string topicClusterId,
            ActorSystem actorSystem,
            IConceptGraphCache graphCache) =>
        {
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var progress = MasteryApiService.BuildTopicProgress(
                overlay, graphCache, topicClusterId, DateTimeOffset.UtcNow);
            return Results.Ok(progress);
        })
        .WithName("GetTopicProgress")
        .WithDescription("Get aggregated mastery progress for a topic cluster");

        // GET /api/v1/mastery/{studentId}/frontier?maxResults=10
        group.MapGet("/{studentId}/frontier", async (
            string studentId,
            int? maxResults,
            ActorSystem actorSystem,
            IConceptGraphCache graphCache) =>
        {
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var frontier = MasteryApiService.BuildFrontier(
                overlay, graphCache, DateTimeOffset.UtcNow, maxResults ?? 10);
            return Results.Ok(frontier);
        })
        .WithName("GetLearningFrontier")
        .WithDescription("Get concepts the student is ready to learn next");

        // GET /api/v1/mastery/{studentId}/decay-alerts
        group.MapGet("/{studentId}/decay-alerts", async (
            string studentId,
            ActorSystem actorSystem,
            IConceptGraphCache graphCache) =>
        {
            var overlay = await GetMasteryOverlay(actorSystem, studentId);
            if (overlay == null)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            var alerts = MasteryApiService.BuildDecayAlerts(
                overlay, graphCache, DateTimeOffset.UtcNow);
            return Results.Ok(alerts);
        })
        .WithName("GetDecayAlerts")
        .WithDescription("Get concepts needing review due to memory decay");

        // GET /api/v1/mastery/{studentId}/review-schedule?maxItems=10
        group.MapGet("/{studentId}/review-schedule", async (
            string studentId,
            int? maxItems,
            ActorSystem actorSystem) =>
        {
            var result = await QueryStudentActor<GetReviewSchedule, ActorResult<IReadOnlyList<ReviewItem>>>(
                actorSystem, studentId, new GetReviewSchedule(studentId, maxItems ?? 10));

            if (result?.Success != true)
                return Results.NotFound(new { error = "Student not found or actor unavailable" });

            return Results.Ok(result.Data);
        })
        .WithName("GetReviewSchedule")
        .WithDescription("Get spaced repetition review schedule");

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
                .RequestAsync<TResult>(identity, query, cts.Token);
        }
        catch
        {
            return null;
        }
    }
}
