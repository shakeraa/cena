// Cena Platform -- Experiment Admin Service (ADM-019)

using Cena.Actors.Events;
using Cena.Actors.Tutoring;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IExperimentAdminService
{
    Task<ExperimentListResponse> GetExperimentsAsync();
    Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName);
    Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName);
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

    public async Task<ExperimentListResponse> GetExperimentsAsync()
    {
        await using var session = _store.QuerySession();

        // Count distinct students from session-started events to estimate experiment populations.
        // ExperimentService uses deterministic hash-based assignment, so we can derive arm counts
        // from the total student population without persisted assignment records.
        var uniqueStudentIds = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started_v1")
            .Select(e => ((SessionStarted_V1)e.Data).StudentId)
            .Distinct()
            .ToListAsync();

        var totalStudents = uniqueStudentIds.Count;

        var summaries = new List<ExperimentSummaryDto>();
        foreach (var (key, def) in Experiments)
        {
            var controlCount = 0;
            var treatmentCount = 0;

            foreach (var studentId in uniqueStudentIds)
            {
                var arm = GetArm(studentId, key, def.Arms);
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
                StartDate: DateTimeOffset.UtcNow.AddDays(-60), // Aligned with simulation seed window
                Description: def.Description));
        }

        _logger.LogDebug("Returned {Count} experiments with {Students} total students",
            summaries.Count, totalStudents);

        return new ExperimentListResponse(summaries);
    }

    public async Task<ExperimentDetailDto?> GetExperimentDetailAsync(string experimentName)
    {
        if (!Experiments.TryGetValue(experimentName, out var def))
            return null;

        await using var session = _store.QuerySession();

        // Gather unique student IDs from session events
        var uniqueStudentIds = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started_v1")
            .Select(e => ((SessionStarted_V1)e.Data).StudentId)
            .Distinct()
            .ToListAsync();

        // Partition students into arms using the same hash logic as ExperimentService
        var studentsByArm = new Dictionary<string, List<string>>();
        foreach (var arm in def.Arms)
            studentsByArm[arm] = new List<string>();

        foreach (var studentId in uniqueStudentIds)
        {
            var arm = GetArm(studentId, experimentName, def.Arms);
            studentsByArm[arm].Add(studentId);
        }

        // Query aggregate metrics from event stream
        var tutoringEpisodes = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_episode_completed_v1")
            .Select(e => (TutoringEpisodeCompleted_V1)e.Data)
            .ToListAsync();

        var masteryEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_mastered_v1")
            .Select(e => (ConceptMastered_V1)e.Data)
            .ToListAsync();

        // Build per-arm cohort metrics
        var cohorts = new List<CohortDto>();
        foreach (var arm in def.Arms)
        {
            var armStudents = studentsByArm[arm].ToHashSet();
            var armEpisodes = tutoringEpisodes.Where(e => armStudents.Contains(e.StudentId)).ToList();
            var armMastery = masteryEvents.Where(e => armStudents.Contains(e.StudentId)).ToList();

            var resolvedCount = armEpisodes.Count(e => e.ResolutionStatus == "resolved");
            var totalEpisodes = armEpisodes.Count;

            cohorts.Add(new CohortDto(
                ArmName: arm,
                StudentCount: armStudents.Count,
                AvgMasteryDelta: armMastery.Count > 0
                    ? (float)armMastery.Average(m => m.MasteryLevel)
                    : 0f,
                ConfusionResolutionRate: totalEpisodes > 0
                    ? (float)resolvedCount / totalEpisodes
                    : 0f,
                AvgTutoringTurns: armEpisodes.Count > 0
                    ? (float)armEpisodes.Average(e => e.TurnCount)
                    : 0f,
                AvgTimeToMasteryHours: armMastery.Count > 0
                    ? (float)armMastery.Average(m => m.InitialHalfLifeHours)
                    : 0f));
        }

        return new ExperimentDetailDto(
            Name: def.DisplayName,
            Status: "running",
            Arms: def.Arms,
            Description: def.Description,
            CohortBreakdown: cohorts,
            TotalStudents: uniqueStudentIds.Count);
    }

    public async Task<ExperimentFunnelResponse?> GetFunnelAsync(string experimentName)
    {
        if (!Experiments.TryGetValue(experimentName, out var def))
            return null;

        await using var session = _store.QuerySession();

        // Stage 1 — Assigned: all unique students with any session event
        var assignedStudents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "session_started_v1")
            .Select(e => ((SessionStarted_V1)e.Data).StudentId)
            .Distinct()
            .ToListAsync();
        var assignedSet = assignedStudents.ToHashSet();
        int assignedCount = assignedSet.Count;

        // Stage 2 — Engaged: students with a TutoringSessionStarted event (interacted with tutor)
        var engagedStudents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_session_started_v1")
            .Select(e => ((TutoringSessionStarted_V1)e.Data).StudentId)
            .Distinct()
            .ToListAsync();
        int engagedCount = engagedStudents.ToHashSet().Count;

        // Stage 3 — Confused: students with confusion-type annotations
        var confusedStudents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "annotation_added_v1")
            .Select(e => (AnnotationAdded_V1)e.Data)
            .Where(a => a.AnnotationType == "confusion")
            .Select(a => a.StudentId)
            .Distinct()
            .ToListAsync();
        int confusedCount = confusedStudents.ToHashSet().Count;

        // Stage 4 — Resolved: students with at least one resolved tutoring episode
        var resolvedStudents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_episode_completed_v1")
            .Select(e => (TutoringEpisodeCompleted_V1)e.Data)
            .Where(ep => ep.ResolutionStatus == "resolved")
            .Select(ep => ep.StudentId)
            .Distinct()
            .ToListAsync();
        int resolvedCount = resolvedStudents.ToHashSet().Count;

        // Stage 5 — Mastered: students who crossed the mastery threshold
        var masteredStudents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.EventTypeName == "concept_mastered_v1")
            .Select(e => ((ConceptMastered_V1)e.Data).StudentId)
            .Distinct()
            .ToListAsync();
        int masteredCount = masteredStudents.ToHashSet().Count;

        var stages = new List<FunnelStageDto>
        {
            new("Assigned",  assignedCount,  assignedCount > 0 ? 1.0f : 0f),
            new("Engaged",   engagedCount,   assignedCount > 0 ? (float)engagedCount / assignedCount : 0f),
            new("Confused",  confusedCount,  assignedCount > 0 ? (float)confusedCount / assignedCount : 0f),
            new("Resolved",  resolvedCount,  assignedCount > 0 ? (float)resolvedCount / assignedCount : 0f),
            new("Mastered",  masteredCount,  assignedCount > 0 ? (float)masteredCount / assignedCount : 0f),
        };

        _logger.LogDebug("Funnel for {Experiment}: {Stages}",
            experimentName, string.Join(" -> ", stages.Select(s => $"{s.Name}={s.Count}")));

        return new ExperimentFunnelResponse(def.DisplayName, stages);
    }

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
