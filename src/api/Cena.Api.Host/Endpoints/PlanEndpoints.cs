// =============================================================================
// Cena Platform -- Plan/Review/Recommendations Endpoints (STB-02)
// Student daily plan and learning recommendations
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Api.Contracts.Plan;
using Cena.Infrastructure.Auth;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me/plan/today — today's learning plan
        app.MapGet("/api/me/plan/today", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

            if (profile is null)
                return Results.NotFound(new { error = "Student profile not found" });

            // STB-02 Phase 1: Stub data from StudentProfileSnapshot
            var dailyGoal = profile.DailyTimeGoalMinutes > 0 
                ? profile.DailyTimeGoalMinutes 
                : 30; // Default fallback

            var firstSubject = profile.Subjects.Length > 0 
                ? profile.Subjects[0] 
                : "math"; // Default fallback

            var plan = new TodaysPlanDto(
                DailyGoalMinutes: dailyGoal,
                CompletedMinutes: 0, // STB-02b: compute from today's events
                NextBlock: new PlanBlock(
                    Subject: firstSubject,
                    EstimatedMinutes: Math.Min(15, dailyGoal / 2)));

            return Results.Ok(plan);
        })
        .WithName("GetTodaysPlan")
        .RequireAuthorization();

        // GET /api/review/due — review items due (STB-02 Phase 1: stub)
        app.MapGet("/api/review/due", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            // STB-02 Phase 1: Always return empty (real SRS in STB-02b)
            var reviewDue = new ReviewDueDto(
                Count: 0,
                OldestDueAt: null,
                SampleSubjects: Array.Empty<string>());

            return Results.Ok(reviewDue);
        })
        .WithName("GetReviewDue")
        .RequireAuthorization();

        // GET /api/recommendations/sessions — recommended sessions (STB-02 Phase 1: stub)
        app.MapGet("/api/recommendations/sessions", async (
            HttpContext ctx,
            IDocumentStore store) =>
        {
            var studentId = GetStudentId(ctx.User);
            if (string.IsNullOrEmpty(studentId))
                return Results.Unauthorized();

            ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

            await using var session = store.QuerySession();
            var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

            // STB-02 Phase 1: Generate stub recommendations from subjects
            var recommendations = new List<RecommendedSession>();
            
            if (profile?.Subjects.Length > 0)
            {
                var difficulties = new[] { "easy", "medium", "hard" };
                
                for (int i = 0; i < Math.Min(3, profile.Subjects.Length); i++)
                {
                    recommendations.Add(new RecommendedSession(
                        SessionId: $"rec-{i + 1}-{Guid.NewGuid().ToString()[..8]}",
                        Subject: profile.Subjects[i],
                        Reason: "Based on your goals",
                        Difficulty: difficulties[i % 3],
                        EstimatedMinutes: 15));
                }
            }
            else
            {
                // Fallback if no subjects
                recommendations.Add(new RecommendedSession(
                    SessionId: $"rec-default-{Guid.NewGuid().ToString()[..8]}",
                    Subject: "math",
                    Reason: "Recommended for beginners",
                    Difficulty: "easy",
                    EstimatedMinutes: 15));
            }

            return Results.Ok(new RecommendedSessionsResponse(recommendations.ToArray()));
        })
        .WithName("GetRecommendedSessions")
        .RequireAuthorization();

        return app;
    }

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
