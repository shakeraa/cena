// =============================================================================
// Cena Platform — Teacher Console: Mastery Heatmap Endpoint (RDY-070 Phase 1A, F6)
//
// GET /api/v1/institutes/{instituteId}/classrooms/{classroomId}/mastery-heatmap
//
// Returns the topic × student mastery heatmap for a single InstructorLed
// classroom. Phase 1A is backend-only; the Vue view is Phase 1B.
//
// Scoping:
//   TEACHER   — must match ClassroomDocument.TeacherId (IDOR-guarded below).
//   ADMIN     — must match the classroom's InstituteId via school_id claim
//               (SameOrg semantics applied inline).
//   SUPER_ADMIN — unrestricted.
//   Others    — 403.
//
// Data flow:
//   1. Look up the classroom; 404 if missing, 403 if InstructorLed guard fails.
//   2. Query the enrollment event stream for rostered studentIds in this
//      classroom (withdrawals removed).
//   3. For each student, fetch the stream and pluck ConceptAttempted_V{1,2,3}.
//   4. Fold into a ClassMasteryHeatmapDocument via the pure projection.
//   5. Project the document into a wire-friendly response DTO.
//
// Rebuild-safety: the endpoint always rebuilds from the event store, so
// step 4 is idempotent and equals any streaming Marten projection that
// sees the same events (the streaming version is Phase 1B).
// =============================================================================

using System.Diagnostics;
using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Syllabus;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Features.TeacherConsole;

// ---- Wire DTOs ---------------------------------------------------------------

public sealed record HeatmapTopicDto(
    string Slug,
    int Order,
    string? MinistryCode,
    string? ParentSlug,
    string? Title);

public sealed record HeatmapCellDto(
    string StudentAnonId,
    string TopicSlug,
    double Mastery,
    int SampleSize,
    DateTimeOffset LastAttemptAt);

public sealed record HeatmapResponse(
    string InstituteId,
    string ClassroomId,
    IReadOnlyList<HeatmapTopicDto> Topics,
    IReadOnlyList<string> Students,
    IReadOnlyList<HeatmapCellDto> Cells,
    int AttemptCount,
    DateTimeOffset UpdatedAt);

// ---- Endpoint ---------------------------------------------------------------

public static class HeatmapEndpoint
{
    public const string Route = "/api/v1/institutes/{instituteId}/classrooms/{classroomId}/mastery-heatmap";

    public static IEndpointRouteBuilder MapHeatmapEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetMasteryHeatmap")
            .WithTags("Teacher Console", "Mastery")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .Produces<HeatmapResponse>(StatusCodes.Status200OK)
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
        IMinistryTopicHierarchy topics,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("TeacherHeatmap");
        var sw = Stopwatch.StartNew();

        await using var session = store.QuerySession();

        // 1. Classroom lookup. 404 before scope so we don't leak existence.
        var classroom = await session.Query<ClassroomDocument>()
            .FirstOrDefaultAsync(c => c.ClassroomId == classroomId, ct);
        if (classroom is null)
            throw new EntityNotFoundException($"classroom '{classroomId}' not found");

        // 2. Route's instituteId must match the classroom's real institute.
        //    Prevents a scoped caller from grabbing a classroom by naming a
        //    friendly institute id in the URL.
        if (!string.Equals(classroom.InstituteId, instituteId, StringComparison.Ordinal))
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' not found in institute '{instituteId}'");

        // 3. InstructorLed-only surface (per RDY-070 scope). PrivateTutor /
        //    PersonalMentorship classrooms use a different heatmap view.
        if (classroom.Mode != ClassroomMode.InstructorLed)
            throw new EntityNotFoundException(
                $"classroom '{classroomId}' is not an InstructorLed surface");

        // 4. Scope / IDOR check. Throws ForbiddenException → 403 via
        //    ExceptionHandler.
        TeacherHeatmapScopeGuard.VerifyTeacherOrAdminAccess(ctx.User, classroom);

        // 5. Resolve the roster by replaying enrollment events for this
        //    classroom. QueryRawEventDataOnly<T> materialises all events of
        //    type T; we filter to this classroom in memory (low cardinality
        //    per classroom in practice — tens of rows).
        var roster = await BuildRosterAsync(session, classroomId, ct);

        // 6. Pull every enrolled student's attempt events and fold.
        var projection = new ClassMasteryHeatmapProjection();
        var resolver = new HierarchyConceptTopicResolver(topics);
        var attempts = new List<AttemptSample>();

        foreach (var studentId in roster)
        {
            ct.ThrowIfCancellationRequested();
            var stream = await session.Events.FetchStreamAsync(studentId, token: ct);
            foreach (var evt in stream)
            {
                switch (evt.Data)
                {
                    case ConceptAttempted_V1 a:
                        attempts.Add(new AttemptSample(a.StudentId, a.ConceptId, a.PosteriorMastery, a.Timestamp));
                        break;
                    case ConceptAttempted_V2 a:
                        attempts.Add(new AttemptSample(a.StudentId, a.ConceptId, a.PosteriorMastery, a.Timestamp));
                        break;
                    case ConceptAttempted_V3 a:
                        attempts.Add(new AttemptSample(a.StudentId, a.ConceptId, a.PosteriorMastery, a.Timestamp));
                        break;
                }
            }
        }

        var doc = projection.Rebuild(
            instituteId: classroom.InstituteId ?? instituteId,
            classroomId: classroom.ClassroomId,
            enrolledStudentAnonIds: roster,
            attempts: attempts,
            resolver: resolver);

        // 7. Enrich topics with display metadata from the hierarchy so the
        //    UI does not have to re-query the syllabus.
        var topicDtos = doc.TopicSlugs
            .Select(slug =>
            {
                var t = topics.GetTopic(slug);
                return new HeatmapTopicDto(
                    Slug: slug,
                    Order: t?.Order ?? 0,
                    MinistryCode: t?.MinistryCode ?? topics.GetMinistryCode(slug),
                    ParentSlug: t?.ParentSlug ?? topics.Parent(slug),
                    Title: PickTitle(t, "en"));
            })
            .OrderBy(t => t.Order)
            .ThenBy(t => t.Slug, StringComparer.Ordinal)
            .ToList();

        var cellDtos = doc.Cells
            .Select(kv =>
            {
                var (studentAnonId, topicSlug) = SplitCellKey(kv.Key);
                return new HeatmapCellDto(
                    StudentAnonId: studentAnonId,
                    TopicSlug: topicSlug,
                    Mastery: kv.Value.Mastery,
                    SampleSize: kv.Value.SampleSize,
                    LastAttemptAt: kv.Value.LastAttemptAt);
            })
            .OrderBy(c => c.StudentAnonId, StringComparer.Ordinal)
            .ThenBy(c => c.TopicSlug, StringComparer.Ordinal)
            .ToList();

        sw.Stop();
        logger.LogInformation(
            "[TEACHER_HEATMAP] classroomId={Cid} students={S} topics={T} cells={C} attempts={A} latencyMs={Ms}",
            classroomId, doc.StudentAnonIds.Count, topicDtos.Count, cellDtos.Count,
            doc.AttemptCount, sw.ElapsedMilliseconds);

        return Results.Ok(new HeatmapResponse(
            InstituteId: classroom.InstituteId ?? instituteId,
            ClassroomId: classroom.ClassroomId,
            Topics: topicDtos,
            Students: doc.StudentAnonIds.AsReadOnly(),
            Cells: cellDtos,
            AttemptCount: doc.AttemptCount,
            UpdatedAt: doc.UpdatedAt));
    }

    private static async Task<List<string>> BuildRosterAsync(
        IQuerySession session, string classroomId, CancellationToken ct)
    {
        var creations = await session.Events
            .QueryRawEventDataOnly<EnrollmentCreated_V1>()
            .Where(e => e.ClassroomId == classroomId)
            .ToListAsync(ct);

        // EnrollmentStatusChanged_V1 doesn't carry ClassroomId directly,
        // so load all and filter by EnrollmentId set. Phase 1A's data
        // volume keeps this tractable; Phase 1B will add a roster read
        // model to replace this fan-out.
        var changes = await session.Events
            .QueryRawEventDataOnly<EnrollmentStatusChanged_V1>()
            .ToListAsync(ct);

        var activeByEnrollmentId = new Dictionary<string, (string StudentId, bool Active)>(StringComparer.Ordinal);
        foreach (var c in creations)
            activeByEnrollmentId[c.EnrollmentId] = (c.StudentId, Active: true);

        foreach (var change in changes.OrderBy(c => c.ChangedAt))
        {
            if (!activeByEnrollmentId.TryGetValue(change.EnrollmentId, out var entry)) continue;
            var isActive = string.Equals(change.NewStatus, EnrollmentStatus.Active.ToString(), StringComparison.OrdinalIgnoreCase);
            activeByEnrollmentId[change.EnrollmentId] = (entry.StudentId, isActive);
        }

        return activeByEnrollmentId.Values
            .Where(v => v.Active)
            .Select(v => v.StudentId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    private static string? PickTitle(MinistryTopic? topic, string locale)
    {
        if (topic is null) return null;
        if (topic.TitleByLocale.TryGetValue(locale, out var t)) return t;
        return topic.TitleByLocale.Values.FirstOrDefault();
    }

    private static (string StudentAnonId, string TopicSlug) SplitCellKey(string key)
    {
        var pipe = key.IndexOf('|');
        return pipe < 0
            ? (key, "")
            : (key.Substring(0, pipe), key.Substring(pipe + 1));
    }
}

// ---- Scope guard (pure, unit-testable) --------------------------------------

/// <summary>
/// Static helper that decides whether a caller may view a given classroom's
/// heatmap. Extracted so the IDOR regression test can exercise the logic
/// without booting a WebApplicationFactory.
/// </summary>
public static class TeacherHeatmapScopeGuard
{
    public static void VerifyTeacherOrAdminAccess(ClaimsPrincipal caller, ClassroomDocument classroom)
    {
        if (caller is null) throw new ArgumentNullException(nameof(caller));
        if (classroom is null) throw new ArgumentNullException(nameof(classroom));

        var role = GetRole(caller);

        switch (role)
        {
            case "SUPER_ADMIN":
                return;

            case "ADMIN":
            case "MODERATOR":
                // Institute-scoped: ADMIN may only view classrooms in their
                // own institute. Without a matching school_id claim, deny.
                var schoolId = caller.FindFirstValue("school_id");
                if (!string.IsNullOrEmpty(classroom.InstituteId)
                    && string.Equals(schoolId, classroom.InstituteId, StringComparison.Ordinal))
                    return;
                throw new ForbiddenException(
                    ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                    $"{role} caller cannot view classroom '{classroom.ClassroomId}' " +
                    $"in institute '{classroom.InstituteId}'.");

            case "TEACHER":
                var callerId = GetCallerId(caller);
                var isOwner = !string.IsNullOrEmpty(callerId)
                    && string.Equals(classroom.TeacherId, callerId, StringComparison.Ordinal);
                var isMentor = classroom.MentorIds?.Contains(callerId ?? "", StringComparer.Ordinal) == true;
                if (isOwner || isMentor) return;
                throw new ForbiddenException(
                    ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                    $"TEACHER '{callerId}' cannot view classroom '{classroom.ClassroomId}' " +
                    $"owned by '{classroom.TeacherId}'.");

            default:
                throw new ForbiddenException(
                    ErrorCodes.CENA_AUTH_IDOR_VIOLATION,
                    $"Caller role '{role}' is not permitted to view the teacher heatmap.");
        }
    }

    private static string? GetRole(ClaimsPrincipal caller)
        => caller.FindFirstValue(ClaimTypes.Role)
           ?? caller.FindFirstValue("role");

    private static string? GetCallerId(ClaimsPrincipal caller)
        => caller.FindFirstValue("sub")
           ?? caller.FindFirstValue("user_id")
           ?? caller.FindFirstValue(ClaimTypes.NameIdentifier);
}

// ---- Resolver adapter -------------------------------------------------------

/// <summary>
/// Adapts <see cref="IMinistryTopicHierarchy"/> to
/// <see cref="IConceptTopicResolver"/> so the projection can depend on the
/// narrow concept→topic lookup without pulling in the whole hierarchy
/// abstraction.
/// </summary>
public sealed class HierarchyConceptTopicResolver : IConceptTopicResolver
{
    private readonly IMinistryTopicHierarchy _hierarchy;

    public HierarchyConceptTopicResolver(IMinistryTopicHierarchy hierarchy)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
    }

    public string? TopicSlugFor(string conceptId)
        => _hierarchy.TopicSlugForLearningObjective(conceptId);
}
