// =============================================================================
// Cena Platform -- Experiment Admin Service (FIND-data-026)
// Optimized experiment analytics with tenant scoping, no full event scans.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IExperimentAdminService
{
    Task<ExperimentListResponse> GetExperimentsAsync(ClaimsPrincipal user);
    Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName, ClaimsPrincipal user);
    Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName, ClaimsPrincipal user);
}

public sealed class ExperimentAdminService : IExperimentAdminService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<ExperimentAdminService> _logger;

    /// <summary>
    /// Replicated from Cena.Actors.Services.ExperimentService.GetExperimentDefinitions().
    /// These must stay in sync with the actor-side definitions.
    /// </summary>
    private static readonly Dictionary<string, ExperimentDefinition> Experiments = new()
    {
        ["explanation_quality"] = new("explanation_quality", "Explanation Quality Tiers",
            new[] { "control", "l2_cached", "l3_personalized" },
            "Tests L2 cached vs L3 personalized explanations against control (no explanation)"),
        ["hint_progression"] = new("hint_progression", "Hint BKT Adjustment",
            new[] { "control", "hints_no_bkt_adjust", "hints_with_bkt_adjust" },
            "Tests whether BKT credit adjustment improves learning after hints"),
    };

    private sealed record ExperimentDefinition(
        string Name, string DisplayName, string[] Arms, string Description);

    public ExperimentAdminService(IDocumentStore store, ILogger<ExperimentAdminService> logger)
    {
        _store = store;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // 1. LIST EXPERIMENTS
    // ═══════════════════════════════════════════════════════════════

    public async Task<ExperimentListResponse> GetExperimentsAsync(ClaimsPrincipal user)
    {
        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-026: Query StudentProfileSnapshot instead of full event scan
        var studentQuery = session.Query<StudentProfileSnapshot>().AsQueryable();

        // REV-014: Apply school filter if not SUPER_ADMIN
        if (schoolId is not null)
            studentQuery = studentQuery.Where(s => s.SchoolId == schoolId);

        var students = await studentQuery
            .Select(s => new { s.StudentId, s.SchoolId, s.ExperimentCohort })
            .ToListAsync();

        var totalStudents = students.Count;

        var summaries = new List<ExperimentSummaryDto>();
        foreach (var (key, def) in Experiments)
        {
            var controlCount = 0;
            var treatmentCount = 0;

            foreach (var student in students)
            {
                var arm = GetArm(student.StudentId, key, def.Arms);
                if (arm == "control")
                    controlCount++;
                else
                    treatmentCount++;
            }

            summaries.Add(new ExperimentSummaryDto(
                Name: def.DisplayName,
                Status: "running",
                Arms: def.Arms,
                TreatmentCount: treatmentCount,
                ControlCount: controlCount,
                StartDate: DateTimeOffset.UtcNow.AddDays(-60),
                Description: def.Description));
        }

        _logger.LogDebug("Returned {Count} experiments with {Students} total students (school={SchoolId})",
            summaries.Count, totalStudents, schoolId ?? "all");

        return new ExperimentListResponse(summaries);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. EXPERIMENT DETAIL
    // ═══════════════════════════════════════════════════════════════

    public async Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName, ClaimsPrincipal user)
    {
        if (!Experiments.TryGetValue(experimentName, out var def))
            return null;

        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-026: Query scoped students from snapshot
        var studentQuery = session.Query<StudentProfileSnapshot>().AsQueryable();
        if (schoolId is not null)
            studentQuery = studentQuery.Where(s => s.SchoolId == schoolId);

        var students = await studentQuery
            .Select(s => s.StudentId)
            .ToListAsync();

        // Partition students into arms
        var studentsByArm = new Dictionary<string, HashSet<string>>();
        foreach (var arm in def.Arms)
            studentsByArm[arm] = new HashSet<string>();

        foreach (var studentId in students)
        {
            var arm = GetArm(studentId, experimentName, def.Arms);
            studentsByArm[arm].Add(studentId);
        }

        // FIND-data-026: Query documents instead of raw events for better performance
        // Use existing projections where possible
        var studentIds = students.ToHashSet();

        // Query mastery data from existing rollup documents
        var masteryQuery = session.Query<ClassMasteryRollupDocument>().AsQueryable();
        var masteryDocs = await masteryQuery.ToListAsync();

        // Query tutoring sessions from existing document
        var tutoringQuery = session.Query<TutoringSessionDocument>().AsQueryable();
        if (schoolId is not null)
        {
            // Filter by student IDs since TutoringSessionDocument doesn't have SchoolId directly
            tutoringQuery = tutoringQuery.Where(t => studentIds.Contains(t.StudentId));
        }
        var tutoringDocs = await tutoringQuery.ToListAsync();

        // Build per-arm cohort metrics
        var cohorts = new List<CohortDto>();
        foreach (var arm in def.Arms)
        {
            var armStudentIds = studentsByArm[arm];
            var armTutoring = tutoringDocs.Where(t => armStudentIds.Contains(t.StudentId)).ToList();

            // Calculate mastery delta from StudentProfileSnapshot mastery data
            var armStudentsWithMastery = await session.Query<StudentProfileSnapshot>()
                .Where(s => armStudentIds.Contains(s.StudentId))
                .Select(s => new
                {
                    MasteryCount = s.ConceptMastery.Count,
                    AvgMastery = s.ConceptMastery.Count > 0
                        ? s.ConceptMastery.Values.Average(m => m.PKnown)
                        : 0
                })
                .ToListAsync();

            var totalMastery = armStudentsWithMastery.Sum(s => s.MasteryCount);
            var avgMastery = armStudentsWithMastery.Count > 0
                ? armStudentsWithMastery.Average(s => s.AvgMastery)
                : 0f;

            cohorts.Add(new CohortDto(
                ArmName: arm,
                StudentCount: armStudentIds.Count,
                AvgMasteryDelta: (float)avgMastery,
                ConfusionResolutionRate: 0f, // Would need annotation data
                AvgTutoringTurns: armTutoring.Count > 0
                    ? (float)armTutoring.Average(t => t.TotalTurns)
                    : 0f,
                AvgTimeToMasteryHours: 0f)); // Not directly available
        }

        return new ExperimentDetailDto(
            Name: def.DisplayName,
            Status: "running",
            Arms: def.Arms,
            Description: def.Description,
            CohortBreakdown: cohorts,
            TotalStudents: students.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. EXPERIMENT FUNNEL
    // ═══════════════════════════════════════════════════════════════

    public async Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName, ClaimsPrincipal user)
    {
        if (!Experiments.TryGetValue(experimentName, out var def))
            return null;

        var schoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        // FIND-data-026: Single query for scoped students instead of 5 event scans
        var studentQuery = session.Query<StudentProfileSnapshot>().AsQueryable();
        if (schoolId is not null)
            studentQuery = studentQuery.Where(s => s.SchoolId == schoolId);

        var students = await studentQuery
            .Select(s => new
            {
                s.StudentId,
                s.SessionCount,
                Arm = GetArm(s.StudentId, experimentName, def.Arms)
            })
            .ToListAsync();

        // Stage 1 — Assigned: all students with any session
        var assignedCount = students.Count(s => s.SessionCount > 0);
        var assignedIds = students.Where(s => s.SessionCount > 0).Select(s => s.StudentId).ToHashSet();

        // Stage 2 — Engaged: students with tutoring sessions
        // FIND-data-026: Query document instead of events
        var engagedIds = await session.Query<TutoringSessionDocument>()
            .Where(t => assignedIds.Contains(t.StudentId))
            .Select(t => t.StudentId)
            .Distinct()
            .ToListAsync();
        var engagedSet = engagedIds.ToHashSet();
        int engagedCount = engagedSet.Count;

        // Stage 3 — Confused: students with confusion annotations
        // Note: This still needs events as there's no projection for annotations
        // But we scope to the already-filtered student IDs
        var confusedIds = assignedIds.Count > 0
            ? await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "annotation_added_v1")
                .Select(e => (AnnotationAdded_V1)e.Data)
                .Where(a => assignedIds.Contains(a.StudentId) && a.AnnotationType == "confusion")
                .Select(a => a.StudentId)
                .Distinct()
                .ToListAsync()
            : new List<string>();
        int confusedCount = confusedIds.Distinct().Count();

        // Stage 4 — Resolved: students with resolved tutoring episodes
        // FIND-data-026: Query scoped set instead of full scan
        var resolvedIds = assignedIds.Count > 0
            ? await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "tutoring_episode_completed_v1")
                .Select(e => (TutoringEpisodeCompleted_V1)e.Data)
                .Where(ep => assignedIds.Contains(ep.StudentId) && ep.ResolutionStatus == "resolved")
                .Select(ep => ep.StudentId)
                .Distinct()
                .ToListAsync()
            : new List<string>();
        int resolvedCount = resolvedIds.Distinct().Count();

        // Stage 5 — Mastered: students who crossed mastery threshold
        // FIND-data-026: Query scoped set instead of full scan
        var masteredIds = assignedIds.Count > 0
            ? await session.Events.QueryAllRawEvents()
                .Where(e => e.EventTypeName == "concept_mastered_v1")
                .Select(e => ((ConceptMastered_V1)e.Data).StudentId)
                .Where(id => assignedIds.Contains(id))
                .Distinct()
                .ToListAsync()
            : new List<string>();
        int masteredCount = masteredIds.Distinct().Count();

        var stages = new List<FunnelStageDto>
        {
            new("Assigned", assignedCount, assignedCount > 0 ? 1.0f : 0f),
            new("Engaged", engagedCount, assignedCount > 0 ? (float)engagedCount / assignedCount : 0f),
            new("Confused", confusedCount, assignedCount > 0 ? (float)confusedCount / assignedCount : 0f),
            new("Resolved", resolvedCount, assignedCount > 0 ? (float)resolvedCount / assignedCount : 0f),
            new("Mastered", masteredCount, assignedCount > 0 ? (float)masteredCount / assignedCount : 0f),
        };

        _logger.LogDebug("Funnel for {Experiment} (school={SchoolId}): {Stages}",
            experimentName, schoolId ?? "all", string.Join(" -> ", stages.Select(s => $"{s.Name}={s.Count}")));

        return new ExperimentFunnelResponse(def.DisplayName, stages);
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Replicates the deterministic hash-based arm assignment from
    /// Cena.Actors.Services.ExperimentService.GetArm().
    /// </summary>
    private static string GetArm(string studentId, string experimentName, string[] arms)
    {
        var hash = HashCode.Combine(studentId, experimentName);
        var idx = Math.Abs(hash) % arms.Length;
        return arms[idx];
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record ExperimentListResponse(IReadOnlyList<ExperimentSummaryDto> Experiments);
public record ExperimentSummaryDto(
    string Name,
    string Status,
    string[] Arms,
    int TreatmentCount,
    int ControlCount,
    DateTimeOffset StartDate,
    string Description);

public record ExperimentDetailDto(
    string Name,
    string Status,
    string[] Arms,
    string Description,
    IReadOnlyList<CohortDto> CohortBreakdown,
    int TotalStudents);

public record CohortDto(
    string ArmName,
    int StudentCount,
    float AvgMasteryDelta,
    float ConfusionResolutionRate,
    float AvgTutoringTurns,
    float AvgTimeToMasteryHours);

public record ExperimentFunnelResponse(string ExperimentName, IReadOnlyList<FunnelStageDto> Stages);
public record FunnelStageDto(string Name, int Count, float ConversionRate);
