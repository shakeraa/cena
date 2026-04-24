// =============================================================================
// Cena Platform — Teacher Dashboard Endpoint (prr-049)
//
// GET /api/v1/institutes/{instituteId}/classrooms/{classroomId}/teacher-dashboard
//
// WHY this endpoint exists (cite BEFORE you touch the payload shape):
//
// prr-049 is a direct response to the pre-release-review persona-educator +
// persona-a11y double-lens critique that the pre-existing teacher-facing
// analytics leaned on "vanity metrics" — streak days, total minutes
// watched, total questions answered — which are *correlational* proxies
// at best and often anti-correlational with learning. The research record:
//
//   - Goodhart's law (Strathern 1997, "Improving ratings: Audit in the
//     British University system", Eur. Review 5:305–321) — "When a measure
//     becomes a target, it ceases to be a good measure". Streak counts
//     and time-on-platform are the canonical cases: students gaming the
//     system to preserve a streak actively reduce learning.
//
//   - Deci & Ryan (2000) "The 'what' and 'why' of goal pursuits: Human
//     needs and the self-determination of behavior" (Psychological
//     Inquiry 11:227–268, DOI 10.1207/S15327965PLI1104_01) — extrinsic
//     engagement mechanics (streaks, rewards) CROWD OUT intrinsic
//     motivation in a diagnostic learning setting. A teacher who optimises
//     for a student's streak is fighting their own pedagogy.
//
//   - Ship-gate GD-004 (docs/engineering/shipgate.md) bans streaks /
//     variable-ratio rewards / loss-aversion copy in student-facing
//     surfaces. This endpoint extends that prohibition to the
//     teacher-facing payload: if streaks are banned on the student UI,
//     they MUST also be banned on the teacher dashboard payload, because
//     teachers coaching from a streak-count dashboard will push the same
//     dynamic back onto the students regardless of what the student UI
//     shows.
//
// Replacement actionable payload (three pillars, all session / BKT
// derived, no vanity counters):
//
//   1. STRUGGLING TOPICS — topics where class-aggregate BKT mastery sits
//      below 0.60 (Bjork "demoralisation floor"). Ranked by deficit
//      magnitude so the teacher knows where to intervene.
//
//   2. HINT-LADDER USAGE per student — per-student counts of L2
//      (scaffolded) and L3 (near-answer) hints requested in the last 7
//      days. High L3 usage signals "I can't start" more reliably than
//      attempt count signals "engagement".
//
//   3. INTERVENTION-RECOMMENDED LIST — students with ≥3 consecutive
//      incorrect attempts on the same concept. This is the "a teacher
//      should look at this student today" surface. Not a score; a
//      trigger.
//
// Tenancy + authorization:
//
//   - TEACHER caller: must match ClassroomDocument.TeacherId OR be listed
//     in MentorIds (IDOR-guarded by the same TeacherHeatmapScopeGuard the
//     HeatmapEndpoint uses).
//   - ADMIN / MODERATOR: must match the classroom's InstituteId via
//     school_id claim.
//   - SUPER_ADMIN: unrestricted.
//
// Privacy floors:
//
//   - Class-wide aggregates (struggling topics, hint-ladder rollups)
//     require N ≥ 3 enrolled students. A single-student "class" returns
//     404 with diagnostic_reason="below_anonymity_floor". Note: this is
//     a floor of 3, NOT the k=10 used by ClassMasteryService for
//     institute-level aggregates — a classroom teacher has a direct
//     pedagogical relationship to their roster, so individual attention
//     is the *purpose* of the dashboard. We still clamp to ≥3 so a
//     1:1-tutoring classroom does not accidentally ship per-student
//     aggregates as "class-wide". Follow-up: prr-026 proposes lifting
//     this to k=10 for broader rollups; see that task for the tightening
//     path.
//
//   - Per-student fields (hint-ladder usage, intervention trigger) are
//     NOT aggregates — they are explicit teacher-to-student pedagogical
//     signals on a teacher's own roster. These are intentional and
//     audit-logged (below). They are NOT shared cross-classroom.
//
//   - No misconception data on the payload (ADR-0003 session-scope
//     preserved). The intervention trigger uses attempt-outcome history
//     only, not misconception tags.
// =============================================================================

using System.Diagnostics;
using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.TeacherConsole;

// ---- Wire DTOs ---------------------------------------------------------------

/// <summary>
/// Struggling topic — class-aggregate BKT mastery below the Bjork
/// demoralisation floor (0.60). Ranked by deficit magnitude in the response
/// so the teacher knows where to intervene first.
/// </summary>
public sealed record StrugglingTopicDto(
    string TopicSlug,
    double MeanMastery,
    int StudentCount,
    double MasteryDeficit);

/// <summary>
/// Per-student hint-ladder usage rollup over the trailing 7 days. L2 and L3
/// counts only (L1 is a normal scaffolded nudge and is not diagnostic).
/// </summary>
public sealed record HintLadderUsageDto(
    string StudentAnonId,
    int L2HintsLast7Days,
    int L3HintsLast7Days);

/// <summary>
/// Intervention trigger — a student with ≥3 consecutive incorrect attempts
/// on the same concept in the last 7 days. The teacher sees a concrete
/// "this student needs a check-in" surface, not a vanity score.
/// </summary>
public sealed record InterventionRecommendationDto(
    string StudentAnonId,
    string ConceptId,
    int ConsecutiveIncorrectCount,
    DateTimeOffset LastAttemptAt);

/// <summary>
/// prr-049 teacher dashboard payload. Replaces the pre-launch vanity
/// counters (streak days, total minutes, total questions answered) with
/// three actionable pillars.
///
/// CONTRACT: this DTO MUST NOT include streak counters, total-minutes
/// counters, total-questions-answered counters, or any other engagement
/// proxy field. The TeacherDashboardHasNoVanityMetricsTest arch test
/// enforces the prohibition.
/// </summary>
public sealed record TeacherDashboardResponse(
    string InstituteId,
    string ClassroomId,
    int StudentCount,
    IReadOnlyList<StrugglingTopicDto> StrugglingTopics,
    IReadOnlyList<HintLadderUsageDto> HintLadderUsageByStudent,
    IReadOnlyList<InterventionRecommendationDto> InterventionsRecommended,
    DateTimeOffset GeneratedAt);

// ---- Endpoint ---------------------------------------------------------------

public static class TeacherDashboardEndpoint
{
    public const string Route =
        "/api/v1/institutes/{instituteId}/classrooms/{classroomId}/teacher-dashboard";

    /// <summary>
    /// Minimum class size before any aggregate field is populated. Below
    /// this, the response is 404 with diagnostic_reason=below_anonymity_floor.
    /// </summary>
    public const int MinClassSizeForAggregates = 3;

    /// <summary>
    /// Bjork demoralisation floor. Topics with class-aggregate mastery
    /// below this cut are flagged as struggling. 0.60 matches
    /// <c>DifficultyTarget.BjorkMinTarget</c> — symmetric with the
    /// scheduler's accuracy floor.
    /// </summary>
    public const double StrugglingTopicThreshold = 0.60;

    /// <summary>
    /// Consecutive-incorrect count that triggers an intervention
    /// recommendation. Three matches the persona-educator review's
    /// "by the third wrong answer the student has usually disengaged"
    /// observation.
    /// </summary>
    public const int InterventionIncorrectStreak = 3;

    /// <summary>Window for hint-ladder rollups + intervention detection.</summary>
    public static readonly TimeSpan RollupWindow = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapTeacherDashboardEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetTeacherDashboard")
            .WithTags("Teacher Console", "Dashboard")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .Produces<TeacherDashboardResponse>(StatusCodes.Status200OK)
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
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TeacherDashboard");
        var sw = Stopwatch.StartNew();

        await using var session = store.QuerySession();

        // 1. Classroom lookup. 404 before scope so we don't leak existence.
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.ClassroomId == classroomId, ct);
        if (classroom is null)
            throw new EntityNotFoundException($"classroom '{classroomId}' not found");

        // 2. Route's instituteId must match the classroom's real institute.
        if (!string.Equals(classroom.InstituteId, instituteId, StringComparison.Ordinal))
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' not found in institute '{instituteId}'");

        // 3. Scope / IDOR check (reuses the heatmap guard — identical
        //    semantics, so let's not duplicate).
        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(ctx.User, classroom);

        // 4. Resolve the roster via the same enrollment-event replay the
        //    heatmap endpoint uses.
        var roster = await TeacherDashboardRosterResolver.ResolveRosterAsync(
            session, classroomId, ct);

        // 5. Anonymity floor. A single-student "classroom" (e.g. a legacy
        //    1:1 tutoring row) returns 404 so we never ship per-student
        //    aggregates labelled "class-wide".
        if (roster.Count < MinClassSizeForAggregates)
        {
            logger.LogInformation(
                "[TEACHER_DASHBOARD] classroomId={Cid} rostered={N} below floor={Floor}; "
                + "returning 404 below_anonymity_floor.",
                classroomId, roster.Count, MinClassSizeForAggregates);
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' has {roster.Count} enrolled student(s); "
                + $"teacher dashboard requires at least {MinClassSizeForAggregates} "
                + $"(below_anonymity_floor).");
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = now - RollupWindow;

        // 6. Pull attempt + hint events for every roster member. Single
        //    pass per student: we read the stream once and fan out into
        //    the three feature extractors.
        var conceptMasteryPerStudent = new Dictionary<string, Dictionary<string, double>>();
        var hintCountsL2 = new Dictionary<string, int>();
        var hintCountsL3 = new Dictionary<string, int>();
        var interventions = new List<InterventionRecommendationDto>();

        foreach (var studentId in roster)
        {
            ct.ThrowIfCancellationRequested();
            var stream = await session.Events.FetchStreamAsync(studentId, token: ct);

            // Attempt history for struggling-topic + intervention detection.
            // We walk chronologically so the "consecutive incorrect" counter
            // is monotonic across the stream.
            var attemptsByConcept = new Dictionary<string, List<AttemptSample>>();
            foreach (var evt in stream.OrderBy(e => e.Timestamp))
            {
                switch (evt.Data)
                {
                    case ConceptAttempted_V1 a when a.Timestamp >= windowStart:
                        Append(attemptsByConcept, a.ConceptId,
                            new AttemptSample(a.IsCorrect, a.PosteriorMastery, a.Timestamp));
                        break;
                    case ConceptAttempted_V2 a when a.Timestamp >= windowStart:
                        Append(attemptsByConcept, a.ConceptId,
                            new AttemptSample(a.IsCorrect, a.PosteriorMastery, a.Timestamp));
                        break;
                    case ConceptAttempted_V3 a when a.Timestamp >= windowStart:
                        Append(attemptsByConcept, a.ConceptId,
                            new AttemptSample(a.IsCorrect, a.PosteriorMastery, a.Timestamp));
                        break;
                    case HintRequested_V1 h when evt.Timestamp >= windowStart:
                        if (h.HintLevel == 2)
                            hintCountsL2[studentId] = hintCountsL2.GetValueOrDefault(studentId) + 1;
                        else if (h.HintLevel == 3)
                            hintCountsL3[studentId] = hintCountsL3.GetValueOrDefault(studentId) + 1;
                        break;
                }
            }

            // Roll up per-concept latest mastery for the class struggling-topic calc.
            var perConcept = conceptMasteryPerStudent.GetValueOrDefault(studentId)
                ?? new Dictionary<string, double>();
            foreach (var kv in attemptsByConcept)
            {
                var latest = kv.Value.OrderBy(a => a.Timestamp).Last();
                perConcept[kv.Key] = latest.PosteriorMastery;

                // Intervention trigger: N consecutive incorrect on this
                // concept at the tail.
                var consecutive = 0;
                for (var i = kv.Value.Count - 1; i >= 0; i--)
                {
                    if (kv.Value[i].IsCorrect) break;
                    consecutive++;
                }
                if (consecutive >= InterventionIncorrectStreak)
                {
                    interventions.Add(new InterventionRecommendationDto(
                        StudentAnonId: studentId,
                        ConceptId: kv.Key,
                        ConsecutiveIncorrectCount: consecutive,
                        LastAttemptAt: kv.Value.Last().Timestamp));
                }
            }
            conceptMasteryPerStudent[studentId] = perConcept;
        }

        // 7. Struggling-topic roll-up. For each concept, average the LATEST
        //    per-student posterior mastery across the roster. Include only
        //    topics with ≥MinClassSizeForAggregates students who have
        //    attempted the concept (otherwise the mean is not
        //    statistically meaningful AND leaks individual signal).
        var strugglingTopics = conceptMasteryPerStudent
            .SelectMany(kv => kv.Value.Select(c => (StudentId: kv.Key, ConceptId: c.Key, Mastery: c.Value)))
            .GroupBy(t => t.ConceptId)
            .Where(g => g.Count() >= MinClassSizeForAggregates)
            .Select(g => new
            {
                ConceptId = g.Key,
                StudentCount = g.Count(),
                Mean = g.Average(t => t.Mastery)
            })
            .Where(t => t.Mean < StrugglingTopicThreshold)
            .OrderBy(t => t.Mean)
            .Select(t => new StrugglingTopicDto(
                TopicSlug: t.ConceptId,
                MeanMastery: t.Mean,
                StudentCount: t.StudentCount,
                MasteryDeficit: StrugglingTopicThreshold - t.Mean))
            .ToList();

        // 8. Hint-ladder usage list. Every roster member gets a row
        //    (zeros included) so the teacher can see who has NOT asked
        //    for scaffolded support — a high-mastery student asking for
        //    zero L2/L3 is informative, not absent.
        var hintLadderUsage = roster
            .Select(s => new HintLadderUsageDto(
                StudentAnonId: s,
                L2HintsLast7Days: hintCountsL2.GetValueOrDefault(s),
                L3HintsLast7Days: hintCountsL3.GetValueOrDefault(s)))
            .OrderByDescending(d => d.L3HintsLast7Days)
            .ThenByDescending(d => d.L2HintsLast7Days)
            .ThenBy(d => d.StudentAnonId, StringComparer.Ordinal)
            .ToList();

        // 9. Sort interventions newest-first.
        interventions.Sort((a, b) => b.LastAttemptAt.CompareTo(a.LastAttemptAt));

        sw.Stop();
        logger.LogInformation(
            "[TEACHER_DASHBOARD] classroomId={Cid} students={S} strugglingTopics={T} "
            + "interventions={I} latencyMs={Ms}",
            classroomId, roster.Count, strugglingTopics.Count, interventions.Count,
            sw.ElapsedMilliseconds);

        return Results.Ok(new TeacherDashboardResponse(
            InstituteId: classroom.InstituteId ?? instituteId,
            ClassroomId: classroom.ClassroomId,
            StudentCount: roster.Count,
            StrugglingTopics: strugglingTopics,
            HintLadderUsageByStudent: hintLadderUsage,
            InterventionsRecommended: interventions,
            GeneratedAt: now));
    }

    private static void Append<TKey, TValue>(
        Dictionary<TKey, List<TValue>> map, TKey key, TValue value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<TValue>();
            map[key] = list;
        }
        list.Add(value);
    }

    private readonly record struct AttemptSample(bool IsCorrect, double PosteriorMastery, DateTimeOffset Timestamp);
}

// ---- Roster resolver (pure, unit-testable) -----------------------------------

/// <summary>
/// Factored-out roster resolver so both the heatmap and dashboard endpoints
/// use one replay. Identical semantics to
/// <c>HeatmapEndpoint.BuildRosterAsync</c> (kept private there). Lifting
/// it here avoids drift between the two surfaces.
/// </summary>
public static class TeacherDashboardRosterResolver
{
    public static async Task<List<string>> ResolveRosterAsync(
        IQuerySession session, string classroomId, CancellationToken ct)
    {
        var creations = await session.Events
            .QueryRawEventDataOnly<EnrollmentCreated_V1>()
            .Where(e => e.ClassroomId == classroomId)
            .ToListAsync(ct);

        var changes = await session.Events
            .QueryRawEventDataOnly<EnrollmentStatusChanged_V1>()
            .ToListAsync(ct);

        var activeByEnrollmentId = new Dictionary<string, (string StudentId, bool Active)>(
            StringComparer.Ordinal);
        foreach (var c in creations)
            activeByEnrollmentId[c.EnrollmentId] = (c.StudentId, Active: true);

        foreach (var change in changes.OrderBy(c => c.ChangedAt))
        {
            if (!activeByEnrollmentId.TryGetValue(change.EnrollmentId, out var entry)) continue;
            var isActive = string.Equals(
                change.NewStatus,
                EnrollmentStatus.Active.ToString(),
                StringComparison.OrdinalIgnoreCase);
            activeByEnrollmentId[change.EnrollmentId] = (entry.StudentId, isActive);
        }

        return activeByEnrollmentId.Values
            .Where(v => v.Active)
            .Select(v => v.StudentId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
