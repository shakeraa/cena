// =============================================================================
// Cena Platform — IStuckDiagnosisRepository (RDY-063 Phase 1)
//
// Narrow repository surface around StuckDiagnosisDocument. Separate from
// the classifier so a) we can test the classifier without a DB, b) the
// persistence failure mode can never break the hot path (persist errors
// are recorded as metrics, not propagated).
// =============================================================================

namespace Cena.Actors.Diagnosis;

public interface IStuckDiagnosisRepository
{
    /// <summary>
    /// Persist a diagnosis for a (session, question) pair. Implementation
    /// MUST NOT throw — failures are swallowed and reported via the
    /// provided metrics helper.
    /// </summary>
    Task PersistAsync(
        string sessionId,
        string studentAnonId,
        string questionId,
        StuckDiagnosis diagnosis,
        int retentionDays,
        CancellationToken ct = default);

    /// <summary>
    /// Recent diagnoses for a given question id, capped at <paramref name="limit"/>.
    /// Used by the admin item-quality dashboard.
    /// </summary>
    Task<IReadOnlyList<StuckDiagnosisDocument>> GetRecentByQuestionAsync(
        string questionId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Top items by stuck-type rate within a time window. Returns one
    /// row per (questionId, primary) grouping, ordered by total count
    /// descending. Admin-only: intended for curriculum review ("which
    /// questions are triggering the most encoding-stuck signals?").
    /// </summary>
    Task<IReadOnlyList<StuckItemAggregate>> GetTopItemsAsync(
        StuckType? filterType, int days, int limit, CancellationToken ct = default);

    /// <summary>
    /// Overall stuck-type distribution within a time window. Returns one
    /// count per StuckType. Used by the admin distribution bar chart.
    /// </summary>
    Task<IReadOnlyDictionary<StuckType, int>> GetDistributionAsync(
        int days, CancellationToken ct = default);
}

/// <summary>
/// Aggregate row returned by <see cref="IStuckDiagnosisRepository.GetTopItemsAsync"/>.
/// Contains NO PII (anon ids are never surfaced either).
/// </summary>
public sealed record StuckItemAggregate(
    string QuestionId,
    StuckType Primary,
    int Count,
    int DistinctStudentsCount,
    float AvgConfidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);
