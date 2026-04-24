// =============================================================================
// Cena Platform — SkillKeyedMasteryRow + store contract (prr-222)
//
// Projection row for the skill-keyed mastery state. One row per
// (StudentId, ExamTargetCode, SkillCode) tuple — the dedup invariant is
// enforced by the store on apply.
//
// The row exposes provenance (Source) so audits can tell which rows came
// from V1 upcast assumptions vs. native V2 writes.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.Mastery;

/// <summary>
/// Single row of the skill-keyed mastery projection.
/// </summary>
/// <param name="Key">Composite projection key (dedup invariant).</param>
/// <param name="MasteryProbability">Current BKT P(L) in [0.001, 0.999].</param>
/// <param name="AttemptCount">Monotone count of attempts folded in.</param>
/// <param name="UpdatedAt">Wall-clock of the most recent fold.</param>
/// <param name="Source">
/// Provenance tag carried over from the most recent event. See
/// <see cref="Events.MasteryEventSource"/> for the legal values.
/// </param>
public sealed record SkillKeyedMasteryRow(
    MasteryKey Key,
    float MasteryProbability,
    int AttemptCount,
    DateTimeOffset UpdatedAt,
    string Source);

/// <summary>
/// Read / write port for the skill-keyed mastery projection. The dedup
/// invariant (unique <see cref="MasteryKey"/>) is enforced on every
/// <see cref="UpsertAsync"/> call.
/// </summary>
public interface ISkillKeyedMasteryStore
{
    /// <summary>
    /// Look up a mastery row by its composite key. Returns <c>null</c>
    /// when no attempts have been folded for that tuple yet.
    /// </summary>
    Task<SkillKeyedMasteryRow?> TryGetAsync(
        MasteryKey key,
        CancellationToken ct = default);

    /// <summary>
    /// List all mastery rows for a student. Used by the erasure cascade
    /// (prr-223) to enumerate what needs to be shredded and by the
    /// scheduler to pick the next target / skill.
    /// </summary>
    Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default);

    /// <summary>
    /// List mastery rows for a specific (student, target) pair. Used by
    /// the cross-target isolation test in prr-222 and by the retention
    /// worker (prr-229) to shred per-target rows when the 24-month
    /// clock elapses.
    /// </summary>
    Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByTargetAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert a row. If the key already exists, the stored row is
    /// replaced in-place; the dedup invariant prevents a second row from
    /// being created for the same tuple. Implementations MUST reject
    /// invalid probabilities (outside [0.001, 0.999]) with
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    Task UpsertAsync(
        SkillKeyedMasteryRow row,
        CancellationToken ct = default);

    /// <summary>
    /// Delete every row for a student. Used by the RTBF cascade
    /// (<see cref="Rtbf.ExamTargetErasureCascade"/>) and by the retention
    /// worker for per-target shreds (via
    /// <see cref="DeleteByTargetAsync"/>).
    /// </summary>
    Task<int> DeleteByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete rows for a specific (student, target) pair. Used by the
    /// retention worker when the 24-month post-archive clock elapses for
    /// a single target (rather than the whole student).
    /// </summary>
    Task<int> DeleteByTargetAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        CancellationToken ct = default);
}
