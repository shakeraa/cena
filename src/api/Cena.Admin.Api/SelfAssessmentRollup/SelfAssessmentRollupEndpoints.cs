// =============================================================================
// Cena Platform — Self-Assessment Classroom Roll-up (RDY-057b)
//
// Admin-only aggregate view over OnboardingSelfAssessmentDocument.
// Answers "what does my classroom feel about each subject?" without
// exposing any individual student's assessment. Zero PII by construction
// — the query never surfaces StudentId, rationales, or free-text.
//
// Fields returned per classroom:
//   - perSubjectConfidenceHistogram: 5-bucket count per subject
//   - topStrengthTags / topFrictionTags: name + count (not student-linked)
//   - topicFeelingHistogram: {Solid/Unsure/Anxious/New} counts per topic
//   - respondentCount + skippedCount
//
// NOT returned:
//   - any per-student row
//   - free-text
//   - studentIds / names
//
// Retention: reads are bounded to documents with ExpiresAt > UtcNow so
// stale (reaped) entries don't bias a classroom's snapshot.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Errors;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.SelfAssessmentRollup;

public static class SelfAssessmentRollupEndpoints
{
    public static IEndpointRouteBuilder MapSelfAssessmentRollupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/self-assessment")
            .WithTags("SelfAssessment")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("api");

        // GET /api/admin/self-assessment/rollup/classroom/{classroomId}
        group.MapGet("/rollup/classroom/{classroomId}", GetClassroomRollupAsync)
            .WithName("GetSelfAssessmentClassroomRollup")
            .Produces<ClassroomRollupResponse>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetClassroomRollupAsync(
        string classroomId,
        ClaimsPrincipal user,
        IDocumentStore store,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(classroomId))
            return Results.BadRequest(new CenaError(
                ErrorCodes.CENA_INTERNAL_VALIDATION,
                "classroomId is required.",
                ErrorCategory.Validation, null, null));

        await using var session = store.QuerySession();

        // Tenant-scope check: load the classroom, verify the caller's
        // school_id matches (or SUPER_ADMIN bypass).
        var classroom = await session.LoadAsync<ClassroomDocument>(classroomId, ct);
        if (classroom is null)
            return Results.NotFound(new CenaError(
                "CENA_CLASSROOM_NOT_FOUND",
                $"No classroom with id {classroomId}.",
                ErrorCategory.NotFound, null, null));

        var schoolScope = TenantScope.GetSchoolFilter(user);
        if (schoolScope is not null && classroom.SchoolId != schoolScope)
            return Results.Forbid();

        // Fetch all students in the classroom. We rely on the existing
        // enrollment/classroom membership model — for the rollup we just
        // need their StudentIds so we can do a WhereIn lookup on the
        // self-assessment store.
        var studentIds = await GetClassroomStudentIdsAsync(session, classroomId, ct);
        if (studentIds.Count == 0)
            return Results.Ok(EmptyRollup(classroomId, 0));

        var now = DateTimeOffset.UtcNow;
        var docs = await session.Query<OnboardingSelfAssessmentDocument>()
            .Where(d => studentIds.Contains(d.StudentId) &&
                        (d.ExpiresAt == null || d.ExpiresAt > now))
            .ToListAsync(ct);

        return Results.Ok(BuildRollup(classroomId, studentIds.Count, docs));
    }

    // Exposed internal for unit tests so the aggregation logic can be
    // exercised without a live Marten session.
    internal static ClassroomRollupResponse BuildRollup(
        string classroomId,
        int classroomSize,
        IReadOnlyList<OnboardingSelfAssessmentDocument> docs)
    {
        var respondents = docs.Count;
        var skipped = docs.Count(d => d.Skipped);

        var confidence = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        foreach (var d in docs)
        {
            if (d.Skipped) continue;
            foreach (var (subject, score) in d.SubjectConfidence)
            {
                if (score < 1 || score > 5) continue;
                if (!confidence.TryGetValue(subject, out var bucket))
                    confidence[subject] = bucket = InitLikertBucket();
                bucket[score]++;
            }
        }

        var strengths = new Dictionary<string, int>(StringComparer.Ordinal);
        var friction = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in docs)
        {
            if (d.Skipped) continue;
            foreach (var s in d.Strengths) strengths[s] = strengths.GetValueOrDefault(s) + 1;
            foreach (var f in d.FrictionPoints) friction[f] = friction.GetValueOrDefault(f) + 1;
        }

        var feelings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var d in docs)
        {
            if (d.Skipped) continue;
            foreach (var (topic, feeling) in d.TopicFeelings)
            {
                if (!feelings.TryGetValue(topic, out var bucket))
                    feelings[topic] = bucket = InitFeelingBucket();
                var key = feeling.ToString();
                bucket[key] = bucket.GetValueOrDefault(key) + 1;
            }
        }

        return new ClassroomRollupResponse(
            ClassroomId: classroomId,
            ClassroomSize: classroomSize,
            RespondentCount: respondents,
            SkippedCount: skipped,
            GeneratedAt: DateTimeOffset.UtcNow,
            ConfidenceHistogram: confidence.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(p => p.Key).Select(p => p.Value).ToList()),
            TopStrengthTags: TopN(strengths, 8),
            TopFrictionTags: TopN(friction, 8),
            TopicFeelingHistogram: feelings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value));
    }

    private static Dictionary<int, int> InitLikertBucket() =>
        new() { [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0 };

    private static Dictionary<string, int> InitFeelingBucket() =>
        new(StringComparer.Ordinal)
        {
            ["Solid"] = 0,
            ["Unsure"] = 0,
            ["Anxious"] = 0,
            ["New"] = 0,
        };

    private static List<TagCount> TopN(Dictionary<string, int> counts, int n) =>
        counts.OrderByDescending(kvp => kvp.Value)
              .Take(n)
              .Select(kvp => new TagCount(kvp.Key, kvp.Value))
              .ToList();

    private static ClassroomRollupResponse EmptyRollup(string classroomId, int size) =>
        new(classroomId, size, 0, 0, DateTimeOffset.UtcNow,
            new Dictionary<string, List<int>>(),
            new List<TagCount>(),
            new List<TagCount>(),
            new Dictionary<string, Dictionary<string, int>>());

    private static async Task<List<string>> GetClassroomStudentIdsAsync(
        IQuerySession session, string classroomId, CancellationToken ct)
    {
        // Classroom membership is tracked via ClassroomJoinRequestDocument
        // rows with Status=Approved. Returning a List (not HashSet) keeps
        // Marten's downstream WhereIn happy for the self-assessment query.
        var members = await session.Query<ClassroomJoinRequestDocument>()
            .Where(r => r.ClassroomId == classroomId &&
                        r.Status == JoinRequestStatus.Approved)
            .Select(r => r.StudentId)
            .ToListAsync(ct);
        return members.Distinct().ToList();
    }
}

// ── Wire DTOs ────────────────────────────────────────────────────────────

public sealed record ClassroomRollupResponse(
    string ClassroomId,
    int ClassroomSize,
    int RespondentCount,
    int SkippedCount,
    DateTimeOffset GeneratedAt,
    Dictionary<string, List<int>> ConfidenceHistogram,      // subject → [c1,c2,c3,c4,c5]
    List<TagCount> TopStrengthTags,
    List<TagCount> TopFrictionTags,
    Dictionary<string, Dictionary<string, int>> TopicFeelingHistogram);

public sealed record TagCount(string Tag, int Count);
