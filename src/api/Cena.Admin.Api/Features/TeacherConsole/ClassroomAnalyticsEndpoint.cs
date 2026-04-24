// =============================================================================
// Cena Platform — Classroom Analytics Endpoint (prr-026)
//
// GET /api/v1/institutes/{instituteId}/classrooms/{classroomId}/analytics/aggregate
//
// Aggregate statistical surface for a classroom. Distinct from:
//
//   - /teacher-dashboard  → floor=3 (prr-049). The teacher's direct roster of
//                           minors they personally coach. 1:1 pedagogical
//                           relationship is the point; per-student signal is
//                           part of the DoD.
//
//   - /mastery-heatmap    → floor implicit (RDY-070). Row-wise per-student
//                           view — governed by the same IDOR guard as the
//                           teacher-dashboard, again 1:1 context.
//
// This endpoint, by contrast, serves **broader statistical claims** the
// teacher uses across classrooms / grades / time windows. Per prr-026 +
// persona-privacy consensus, those claims must carry the k=10 floor the
// Israel PPL Amendment 13 guidance recommends for education-sector
// statistical releases.
//
// On a below-floor classroom, the endpoint returns 404 (NOT 403) so the
// existence of the classroom is not leaked across the anonymity boundary.
// The diagnostic reason is reported as `below_anonymity_floor` (identical
// string to the prr-049 teacher-dashboard code path so operators have a
// single grep term).
//
// Tenancy + authorization: identical guards to HeatmapEndpoint /
// TeacherDashboardEndpoint (TEACHER must own or mentor the classroom;
// ADMIN/MODERATOR must match institute; SUPER_ADMIN unrestricted).
//
// Payload: mean class-wide mastery, mean hint-ladder rate, session
// count — all strict aggregates, zero per-student fields.
// =============================================================================

using System.Diagnostics;
using Cena.Actors.Events;
using Cena.Infrastructure.Analytics;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.TeacherConsole;

/// <summary>
/// Aggregate response for the classroom analytics surface. Contains ONLY
/// class-wide statistics — no per-student fields, no misconception tags
/// (ADR-0003), no streak counters (GD-004).
/// </summary>
public sealed record ClassroomAnalyticsAggregateResponse(
    string InstituteId,
    string ClassroomId,
    int StudentCount,
    double MeanMastery,
    double MeanHintLadderRate,
    int AttemptCount,
    int AnonymityFloorK,
    DateTimeOffset GeneratedAt);

public static class ClassroomAnalyticsEndpoint
{
    public const string Route =
        "/api/v1/institutes/{instituteId}/classrooms/{classroomId}/analytics/aggregate";

    /// <summary>
    /// Surface label used by the k-anonymity enforcer metric. Stable so the
    /// k-anonymity dashboard can group suppression events by route.
    /// </summary>
    public const string SurfaceName = "/classrooms/{id}/analytics/aggregate";

    public static IEndpointRouteBuilder MapClassroomAnalyticsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetClassroomAnalyticsAggregate")
            .WithTags("Teacher Console", "Analytics")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .Produces<ClassroomAnalyticsAggregateResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        string instituteId,
        string classroomId,
        HttpContext ctx,
        IDocumentStore store,
        IKAnonymityEnforcer enforcer,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ClassroomAnalyticsAggregate");
        var sw = Stopwatch.StartNew();

        await using var session = store.QuerySession();

        // 1. Classroom lookup. 404 before scope so we don't leak existence.
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.ClassroomId == classroomId, ct);
        if (classroom is null)
            throw new EntityNotFoundException($"classroom '{classroomId}' not found");

        if (!string.Equals(classroom.InstituteId, instituteId, StringComparison.Ordinal))
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' not found in institute '{instituteId}'");

        // 2. IDOR guard — identical to heatmap/teacher-dashboard.
        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(ctx.User, classroom);

        // 3. Roster via the same replay the teacher-dashboard uses. No
        //    drift between the two surfaces — if we ever change roster
        //    semantics we update one helper, not two.
        var roster = await TeacherDashboardRosterResolver.ResolveRosterAsync(
            session, classroomId, ct);

        // 4. prr-026: k-anonymity floor for aggregate statistical claims.
        //    AssertMinimumGroupSize throws InsufficientAnonymityException on
        //    a below-floor roster; we translate that into 404 (NOT 403) so
        //    existence of the classroom is not leaked across the anonymity
        //    boundary. The enforcer has already incremented
        //    cena_k_anonymity_suppressed_total{surface,k} before the throw.
        //
        //    We use AssertMinimumGroupSize (not MeetsFloor) because an
        //    aggregate response with fewer than k students is not
        //    partially-suppressible — the WHOLE payload is a class-wide
        //    statistic, so either the cohort qualifies or the endpoint
        //    returns nothing.
        try
        {
            enforcer.AssertMinimumGroupSize(
                roster,
                IKAnonymityEnforcer.DefaultClassroomAggregateK,
                SurfaceName,
                StringComparer.Ordinal);
        }
        catch (InsufficientAnonymityException)
        {
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' aggregate unavailable (below_anonymity_floor).");
        }

        // 5. Walk the event stream once per student. Same shape as the
        //    teacher-dashboard loop but rolled up to class-wide aggregates.
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - TeacherDashboardEndpoint.RollupWindow;

        var totalMastery = 0.0;
        var masterySampleCount = 0;
        var totalHintsL2AndL3 = 0;
        var totalAttempts = 0;

        foreach (var studentId in roster)
        {
            ct.ThrowIfCancellationRequested();
            var stream = await session.Events.FetchStreamAsync(studentId, token: ct);

            foreach (var evt in stream)
            {
                switch (evt.Data)
                {
                    case ConceptAttempted_V1 a when a.Timestamp >= windowStart:
                        totalAttempts++;
                        totalMastery += a.PosteriorMastery;
                        masterySampleCount++;
                        break;
                    case ConceptAttempted_V2 a when a.Timestamp >= windowStart:
                        totalAttempts++;
                        totalMastery += a.PosteriorMastery;
                        masterySampleCount++;
                        break;
                    case ConceptAttempted_V3 a when a.Timestamp >= windowStart:
                        totalAttempts++;
                        totalMastery += a.PosteriorMastery;
                        masterySampleCount++;
                        break;
                    case HintRequested_V1 h when evt.Timestamp >= windowStart:
                        if (h.HintLevel >= 2) totalHintsL2AndL3++;
                        break;
                }
            }
        }

        var meanMastery = masterySampleCount > 0
            ? totalMastery / masterySampleCount
            : 0.0;
        var meanHintRate = totalAttempts > 0
            ? (double)totalHintsL2AndL3 / totalAttempts
            : 0.0;

        sw.Stop();
        logger.LogInformation(
            "[CLASSROOM_ANALYTICS] classroomId={Cid} students={S} attempts={A} latencyMs={Ms}",
            classroomId, roster.Count, totalAttempts, sw.ElapsedMilliseconds);

        return Results.Ok(new ClassroomAnalyticsAggregateResponse(
            InstituteId: classroom.InstituteId ?? instituteId,
            ClassroomId: classroom.ClassroomId,
            StudentCount: roster.Count,
            MeanMastery: meanMastery,
            MeanHintLadderRate: meanHintRate,
            AttemptCount: totalAttempts,
            AnonymityFloorK: IKAnonymityEnforcer.DefaultClassroomAggregateK,
            GeneratedAt: now));
    }
}
