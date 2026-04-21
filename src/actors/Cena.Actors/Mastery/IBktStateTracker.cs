// =============================================================================
// Cena Platform — IBktStateTracker contract (prr-222)
//
// Per prr-222 scope: the tracker takes a (SkillCode, ExamTargetCode) pair
// (plus StudentAnonId) on every update so the dedup invariant
// (StudentId, ExamTargetCode, SkillCode) can be enforced at write time.
//
// The tracker is the ONLY write surface for mastery state. Consumers that
// need to read current posteriors should use
// ISkillKeyedMasteryStore.TryGetAsync, not the tracker — the tracker is
// intentionally a write-only port to keep the read path simple for the
// scheduler.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.Mastery;

/// <summary>
/// Write-only port for mastery state. Every learning-session attempt
/// results in exactly one <see cref="UpdateAsync"/> call that folds the
/// observation (<paramref name="isCorrect"/>) into the posterior for the
/// <c>(studentAnonId, examTargetCode, skillCode)</c> tuple.
/// </summary>
public interface IBktStateTracker
{
    /// <summary>
    /// Apply a single BKT observation to the mastery state keyed on the
    /// (<paramref name="studentAnonId"/>, <paramref name="examTargetCode"/>,
    /// <paramref name="skillCode"/>) tuple. Appends a
    /// <see cref="Events.MasteryUpdated_V2"/> to the underlying store and
    /// updates the projection in the same transaction.
    /// </summary>
    /// <param name="studentAnonId">Pseudonymous student id.</param>
    /// <param name="examTargetCode">
    /// Catalog code of the exam context this attempt happened in. REQUIRED
    /// — attempts without an exam context are rejected.
    /// </param>
    /// <param name="skillCode">Skill being observed.</param>
    /// <param name="isCorrect">Observation: did the student succeed?</param>
    /// <param name="occurredAt">Wall-clock of the attempt.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// The posterior P(L) after the update (clamped to [0.001, 0.999]).
    /// Callers that need the full row should read via
    /// <see cref="ISkillKeyedMasteryStore.TryGetAsync"/>.
    /// </returns>
    Task<float> UpdateAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        SkillCode skillCode,
        bool isCorrect,
        DateTimeOffset occurredAt,
        CancellationToken ct = default);
}
