// =============================================================================
// Cena Platform -- Plan/Review/Recommendations Endpoints
// Reads StudentProfileSnapshot (HLR timers), AnalyticsRollupService
// (daily time breakdown), and SubjectMasteryTimeline projections.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Api.Contracts.Plan;
using Cena.Api.Host.Services;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

namespace Cena.Api.Host.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/plan/today", GetTodaysPlan)
            .WithName("GetTodaysPlan")
            .RequireAuthorization()
    .Produces<TodaysPlanDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status404NotFound)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/review/due", GetReviewDue)
            .WithName("GetReviewDue")
            .RequireAuthorization()
    .Produces<ReviewDueDto>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        app.MapGet("/api/recommendations/sessions", GetRecommendedSessions)
            .WithName("GetRecommendedSessions")
            .RequireAuthorization()
    .Produces<RecommendedSessionsResponse>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized);

        return app;
    }

    // GET /api/me/plan/today — real CompletedMinutes + recommendation-driven NextBlock
    private static async Task<IResult> GetTodaysPlan(
        HttpContext ctx,
        IDocumentStore store,
        [FromServices] IAnalyticsRollupService analytics,
        [FromServices] IRecommendationService recommendations)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (profile is null)
            return Results.NotFound(new { error = "Student profile not found" });

        var dailyGoal = profile.DailyTimeGoalMinutes > 0 ? profile.DailyTimeGoalMinutes : 30;

        var today = DateTime.UtcNow.Date;
        var breakdown = await analytics.GetTimeBreakdownAsync(studentId, today, ctx.RequestAborted);
        var completedMinutes = breakdown?.TotalMinutes ?? 0;

        var remaining = Math.Max(0, dailyGoal - completedMinutes);
        var nextBlock = remaining == 0
            ? null
            : await recommendations.GetNextBlockAsync(studentId, remaining, ctx.RequestAborted);

        var plan = new TodaysPlanDto(
            DailyGoalMinutes: dailyGoal,
            CompletedMinutes: completedMinutes,
            NextBlock: nextBlock);

        return Results.Ok(plan);
    }

    // GET /api/review/due — computed from HLR state on StudentProfileSnapshot
    private static async Task<IResult> GetReviewDue(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (profile is null || profile.HalfLifeMap.Count == 0)
            return Results.Ok(new ReviewDueDto(0, null, Array.Empty<string>()));

        var now = DateTimeOffset.UtcNow;
        var dueCount = 0;
        DateTimeOffset? oldestDueAt = null;

        foreach (var (conceptId, halfLifeHours) in profile.HalfLifeMap)
        {
            if (halfLifeHours <= 0) continue;

            profile.ConceptMastery.TryGetValue(conceptId, out var mastery);
            var lastReviewAt = mastery?.LastAttemptedAt ?? profile.CreatedAt;
            var deltaHours = (now - lastReviewAt).TotalHours;
            var predictedRecall = Math.Pow(2, -deltaHours / halfLifeHours);

            if (predictedRecall < RecommendationService.RecallReviewThreshold)
            {
                dueCount++;
                var dueAt = lastReviewAt.AddHours(
                    -halfLifeHours * Math.Log2(RecommendationService.RecallReviewThreshold));
                if (oldestDueAt is null || dueAt < oldestDueAt)
                    oldestDueAt = dueAt;
            }
        }

        // Attribute due items to the subjects with the weakest mastery timelines.
        var sampleSubjects = Array.Empty<string>();
        if (dueCount > 0 && profile.Subjects.Length > 0)
        {
            var ids = profile.Subjects.Select(s => $"{studentId}:{s}").ToList();
            var timelines = await session.Query<SubjectMasteryTimeline>()
                .Where(t => ids.Contains(t.Id))
                .ToListAsync(ctx.RequestAborted);

            sampleSubjects = profile.Subjects
                .OrderByDescending(s =>
                {
                    var tl = timelines.FirstOrDefault(t => string.Equals(t.Subject, s, StringComparison.OrdinalIgnoreCase));
                    if (tl is null || tl.Snapshots.Count == 0) return 1.0;
                    var latest = tl.Snapshots.OrderByDescending(x => x.Date).First();
                    return Math.Clamp(1.0 - latest.AverageMastery, 0.0, 1.0);
                })
                .Take(5)
                .ToArray();
        }

        var response = new ReviewDueDto(
            Count: dueCount,
            OldestDueAt: oldestDueAt?.UtcDateTime,
            SampleSubjects: sampleSubjects);

        return Results.Ok(response);
    }

    // GET /api/recommendations/sessions — delegates to IRecommendationService
    private static async Task<IResult> GetRecommendedSessions(
        HttpContext ctx,
        [FromServices] IRecommendationService recommendations,
        int? max = 3)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        var items = await recommendations.RankForStudentAsync(
            studentId,
            Math.Max(1, Math.Min(max ?? 3, 10)),
            ctx.RequestAborted);

        return Results.Ok(new RecommendedSessionsResponse(items));
    }

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
