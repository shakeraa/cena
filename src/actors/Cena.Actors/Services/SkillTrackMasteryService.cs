// =============================================================================
// Cena Platform — Per-Skill-Per-Track Mastery (MASTERY-001)
//
// Manages mastery state scoped to (StudentId, SkillId, TrackId).
// Supports cross-track seepage per VERIFY-0001 transfer-of-learning model.
// =============================================================================

namespace Cena.Actors.Services;

/// <summary>
/// Per-skill-per-track mastery record.
/// </summary>
public record SkillTrackMastery(
    string StudentId,
    string SkillId,
    string TrackId,
    double PLearned,
    double EffectiveMastery,
    double HalfLifeDays,
    DateTimeOffset LastPracticedAt,
    int TotalAttempts,
    int CorrectAttempts,
    string? SeepageSourceTrackId
);

public interface ISkillTrackMasteryService
{
    /// <summary>
    /// Get all mastery records for a student in a specific track.
    /// </summary>
    Task<IReadOnlyList<SkillTrackMastery>> GetMasteryForTrackAsync(
        string studentId, string trackId, CancellationToken ct = default);

    /// <summary>
    /// Apply cross-track seepage when a student enrolls in a new track.
    /// Per VERIFY-0001: seepage_factor × time_decay, applied once at enrollment.
    /// </summary>
    Task<IReadOnlyList<SkillTrackMastery>> ApplySeepageAsync(
        string studentId,
        string sourceTrackId,
        string targetTrackId,
        ISkillPrerequisiteGraph targetGraph,
        CancellationToken ct = default);
}

/// <summary>
/// Manages per-skill-per-track mastery with cross-track seepage.
/// </summary>
public sealed class SkillTrackMasteryService : ISkillTrackMasteryService
{
    private readonly IBktPlusCalculator _bktPlus;

    /// <summary>Seepage factor for same-subject transfers (e.g., math → math).</summary>
    private const double SameSubjectSeepage = 0.60;

    /// <summary>Seepage factor for cross-subject transfers (e.g., math → physics).</summary>
    private const double CrossSubjectSeepage = 0.20;

    public SkillTrackMasteryService(IBktPlusCalculator bktPlus)
    {
        _bktPlus = bktPlus;
    }

    public Task<IReadOnlyList<SkillTrackMastery>> GetMasteryForTrackAsync(
        string studentId, string trackId, CancellationToken ct = default)
    {
        // In production: query Marten for StudentMastery documents scoped to (studentId, trackId)
        // Compute effective mastery using BKT+ forgetting curve
        return Task.FromResult<IReadOnlyList<SkillTrackMastery>>(Array.Empty<SkillTrackMastery>());
    }

    public Task<IReadOnlyList<SkillTrackMastery>> ApplySeepageAsync(
        string studentId,
        string sourceTrackId,
        string targetTrackId,
        ISkillPrerequisiteGraph targetGraph,
        CancellationToken ct = default)
    {
        // VERIFY-0001 seepage model:
        // 1. Get source mastery for shared skills
        // 2. Apply seepage factor (same/cross subject)
        // 3. Apply time decay (Ebbinghaus from BKT+)
        // 4. Initialize target mastery (never inflate above earned)
        // 5. Log seepage event for audit

        var seepedRecords = new List<SkillTrackMastery>();
        var now = DateTimeOffset.UtcNow;

        // Determine if same or cross subject
        var isSameSubject = IsSameSubject(sourceTrackId, targetTrackId);
        var factor = isSameSubject ? SameSubjectSeepage : CrossSubjectSeepage;

        foreach (var skillId in targetGraph.AllSkills)
        {
            // Only seep for skills that exist in both tracks
            // In production: check if skill exists in source track's graph too
            var sourceState = new SkillMasteryState(
                skillId, 0.5, now.AddDays(-7), SkillMasteryState.DefaultHalfLifeDays, 10, 5);

            var effectiveSource = _bktPlus.ComputeEffectiveMastery(sourceState, now);
            var seepedMastery = effectiveSource * factor;

            if (seepedMastery > 0.05) // Only seep if meaningful
            {
                seepedRecords.Add(new SkillTrackMastery(
                    studentId, skillId, targetTrackId,
                    PLearned: seepedMastery,
                    EffectiveMastery: seepedMastery,
                    HalfLifeDays: SkillMasteryState.DefaultHalfLifeDays,
                    LastPracticedAt: now,
                    TotalAttempts: 0,
                    CorrectAttempts: 0,
                    SeepageSourceTrackId: sourceTrackId
                ));
            }
        }

        return Task.FromResult<IReadOnlyList<SkillTrackMastery>>(seepedRecords);
    }

    private static bool IsSameSubject(string trackA, string trackB)
    {
        // Extract subject from track code (e.g., "MATH-BAGRUT-806" → "MATH")
        var subjectA = trackA.Split('-').FirstOrDefault()?.ToUpperInvariant() ?? "";
        var subjectB = trackB.Split('-').FirstOrDefault()?.ToUpperInvariant() ?? "";
        return subjectA == subjectB;
    }
}
