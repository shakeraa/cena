// =============================================================================
// Cena Platform — Advancement Trajectory Redactor (RDY-061 Phase 6)
//
// Converts per-student advancement events into cohort-safe trajectory
// vectors suitable for ReasoningBank pattern distillation. No student-
// identifying fields cross the redactor boundary.
//
// Per ADR-0003 amendment:
//   - Advancement trajectories are BEHAVIOURAL (chapter timing, mastery
//     curve, spiral-review cadence). Fair game for cross-student
//     pattern learning.
//   - Affective inference is EXCLUDED (no "Student X struggled
//     emotionally" derivation).
//   - 30-day retention ceiling matches misconception rules.
//
// The redactor writes bucketed features + archetype tags, never raw
// student ids, names, or emails. A final guard scans the output string
// for the forbidden patterns before hand-off.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Advancement;

public sealed record AdvancementTrajectoryVector(
    string TrajectoryId,             // deterministic: hash(studentId + trackId + seqNo)
    string TrackId,                  // track is cohort-level, not PII
    string SyllabusVersion,
    string Archetype,                // derived from mastery curve shape, never student-identifying
    float TotalChapters,
    float ChaptersMastered,
    float ChaptersInProgress,
    float ChaptersLocked,
    float AverageTimeToMasteryDays,
    float SpiralReviewsTriggered,
    float AverageRetentionAfterReview,
    DateTimeOffset BucketedAtHour    // timestamp bucketed to hour to prevent correlation
);

public interface IAdvancementTrajectoryRedactor
{
    /// <summary>
    /// Convert advancement state + event list into a PII-free vector.
    /// Returns null if the state has insufficient signal (e.g. 0 chapters
    /// touched — not worth learning from yet).
    /// </summary>
    AdvancementTrajectoryVector? Redact(
        StudentAdvancementState state,
        IReadOnlyList<IDelegatedEvent> events);
}

public sealed class AdvancementTrajectoryRedactor : IAdvancementTrajectoryRedactor
{
    // Forbidden patterns in the trajectory. If any appears in the
    // serialized vector, we refuse to emit — defence in depth for
    // cases where a future author adds a new field and forgets to
    // redact it.
    private static readonly string[] ForbiddenSubstrings =
    {
        "@", "studentId", "student_id", "StudentId",
        "email", "Email", "fullName", "FullName",
    };

    public AdvancementTrajectoryVector? Redact(
        StudentAdvancementState state,
        IReadOnlyList<IDelegatedEvent> events)
    {
        if (state.ChapterStatuses.Count == 0) return null;
        var touchedCount = state.ChapterStatuses.Count(kvp => kvp.Value != ChapterStatus.Locked);
        if (touchedCount == 0) return null;

        var mastered = state.ChapterStatuses.Count(kvp => kvp.Value == ChapterStatus.Mastered);
        var inProgress = state.ChapterStatuses.Count(kvp => kvp.Value == ChapterStatus.InProgress);
        var locked = state.ChapterStatuses.Count(kvp => kvp.Value == ChapterStatus.Locked);

        // Average time-to-mastery — derived from ChapterMastered_V1 events
        // paired with ChapterStarted_V1 events.
        var started = events.OfType<ChapterStarted_V1>().ToDictionary(e => e.ChapterId, e => e.FirstAttemptAt);
        var masteredEvents = events.OfType<ChapterMastered_V1>().ToList();
        float? avgDays = null;
        if (masteredEvents.Count > 0)
        {
            var totalDays = 0.0;
            int paired = 0;
            foreach (var m in masteredEvents)
            {
                if (started.TryGetValue(m.ChapterId, out var s))
                {
                    totalDays += (m.MasteredAt - s).TotalDays;
                    paired++;
                }
            }
            if (paired > 0) avgDays = (float)(totalDays / paired);
        }

        var spiralReviews = events.OfType<SpiralReviewCompleted_V1>().ToList();
        var avgRetention = spiralReviews.Count > 0
            ? spiralReviews.Average(r => r.RetentionAfterReview)
            : 0f;

        var archetype = DeriveArchetype(mastered, inProgress, locked, avgDays);
        var trajectoryId = DeterministicId(state.Id, state.EventVersion);

        var vector = new AdvancementTrajectoryVector(
            TrajectoryId: trajectoryId,
            TrackId: state.TrackId,
            SyllabusVersion: state.SyllabusVersion,
            Archetype: archetype,
            TotalChapters: state.ChapterStatuses.Count,
            ChaptersMastered: mastered,
            ChaptersInProgress: inProgress,
            ChaptersLocked: locked,
            AverageTimeToMasteryDays: avgDays ?? 0f,
            SpiralReviewsTriggered: spiralReviews.Count,
            AverageRetentionAfterReview: avgRetention,
            BucketedAtHour: BucketToHour(state.LastAdvancedAt));

        // Defence-in-depth: serialise + scan for PII substrings. Any hit
        // is a bug — refuse to emit, log loudly (upstream).
        var serialized = JsonSerializer.Serialize(vector);
        foreach (var forbid in ForbiddenSubstrings)
        {
            if (serialized.Contains(forbid, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return vector;
    }

    private static string DeriveArchetype(int mastered, int inProgress, int locked, float? avgDays)
    {
        // Purely behavioural buckets, not affective labels.
        // - fast-advancer: majority mastered + avg < 7d
        // - steady-learner: majority mastered + avg >= 7d
        // - early-explorer: mostly in-progress
        // - onboarding: mostly locked (just started)
        var total = mastered + inProgress + locked;
        if (total == 0) return "unknown";
        if (mastered * 2 > total && avgDays is not null && avgDays < 7f) return "fast-advancer";
        if (mastered * 2 > total) return "steady-learner";
        if (inProgress * 2 > total) return "early-explorer";
        return "onboarding";
    }

    private static DateTimeOffset BucketToHour(DateTimeOffset at)
        => new(at.Year, at.Month, at.Day, at.Hour, 0, 0, at.Offset);

    private static string DeterministicId(string advancementId, int eventVersion)
    {
        // Hash the advancement id so the trajectory id doesn't leak
        // student-trackability across buckets. Paired with eventVersion
        // so a given state snapshot has a stable id on re-emission.
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{advancementId}::v{eventVersion}"));
        return "traj-" + Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
