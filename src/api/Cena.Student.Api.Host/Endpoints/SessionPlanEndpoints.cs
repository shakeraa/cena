// =============================================================================
// Cena Platform — Session Plan Endpoints (prr-149)
//
// GET /api/session/{sessionId}/plan — returns the current SessionPlanSnapshot
// for the session. Auth-gated to the session owner (idor check against the
// SessionPlanDocument.StudentAnonId).
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Sessions;
using Cena.Actors.Sessions.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public sealed record SessionPlanTopicDto(
    string TopicSlug,
    double PriorityScore,
    double WeaknessComponent,
    double TopicWeightComponent,
    double PrerequisiteComponent,
    string Rationale);

public sealed record SessionPlanDto(
    string SessionId,
    string StudentAnonId,
    DateTimeOffset GeneratedAtUtc,
    string MotivationProfile,
    DateTimeOffset? DeadlineUtc,
    int WeeklyBudgetMinutes,
    string InputsSource,
    IReadOnlyList<SessionPlanTopicDto> Topics);

public static class SessionPlanEndpoints
{
    public const string Route = "/api/session/{sessionId}/plan";

    public static IEndpointRouteBuilder MapSessionPlanEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetSessionPlan")
            .WithTags("Sessions", "Plan")
            .RequireAuthorization()
            .Produces<SessionPlanDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string sessionId,
        HttpContext ctx,
        IDocumentStore store,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Results.BadRequest(new { error = "missing-session-id" });

        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var doc = await session
            .LoadAsync<SessionPlanDocument>(SessionPlanDocument.DocumentId(sessionId), ct)
            .ConfigureAwait(false);

        if (doc is null)
            return Results.NotFound(new { error = $"No plan found for session '{sessionId}'." });

        // IDOR: confirm the caller owns this session plan.
        if (!string.Equals(doc.StudentAnonId, studentId, StringComparison.Ordinal))
            return Results.Forbid();

        var dto = new SessionPlanDto(
            SessionId: doc.SessionId,
            StudentAnonId: doc.StudentAnonId,
            GeneratedAtUtc: doc.GeneratedAtUtc,
            MotivationProfile: doc.MotivationProfile.ToString(),
            DeadlineUtc: doc.DeadlineUtc,
            WeeklyBudgetMinutes: doc.WeeklyBudgetMinutes,
            InputsSource: doc.InputsSource,
            Topics: doc.Topics
                .Select(t => new SessionPlanTopicDto(
                    TopicSlug: t.TopicSlug,
                    PriorityScore: t.PriorityScore,
                    WeaknessComponent: t.WeaknessComponent,
                    TopicWeightComponent: t.TopicWeightComponent,
                    PrerequisiteComponent: t.PrerequisiteComponent,
                    Rationale: t.Rationale))
                .ToList());

        return Results.Ok(dto);
    }

    private static string? GetStudentId(ClaimsPrincipal user)
        => user.FindFirstValue("student_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue("user_id");
}
