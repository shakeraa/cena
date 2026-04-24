// =============================================================================
// Cena Platform — StudentProfileAbilityEstimateProvider (prr-149)
//
// Reads per-topic ability estimates from the student's existing Marten
// StudentProfileSnapshot.ConceptMastery map and adapts them into the
// AbilityEstimate shape AdaptiveScheduler consumes.
//
// This is a pragmatic phase-1 adapter: the scheduler's weakness signal
// only needs θ + a coarse standard-error + sample count, and the
// profile snapshot already carries per-concept MasteryProbability
// (0..1), AttemptCount, and LastInteraction. A dedicated per-topic IRT
// θ projection will replace this when RDY-080 calibration ships; until
// then the mapping `θ ≈ 2 * (p − 0.5)` is a stable coarse approximation
// (matches the scale the scheduler's MasteryTargetTheta = +0.5 is written
// against).
//
// NEVER writes to the profile — pure read adapter.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions;
using Marten;

namespace Cena.Api.Host.Services;

/// <summary>
/// Reads <see cref="StudentProfileSnapshot.ConceptMastery"/> and maps
/// each entry to an <see cref="AbilityEstimate"/> keyed by topic slug.
/// Falls back to an empty map when the snapshot is missing (cold-start).
/// </summary>
public sealed class StudentProfileAbilityEstimateProvider : ISessionAbilityEstimateProvider
{
    private readonly IDocumentStore _store;

    public StudentProfileAbilityEstimateProvider(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, AbilityEstimate>> GetAsync(
        string studentAnonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
            throw new ArgumentException(
                "Student anon id must be non-empty.", nameof(studentAnonId));

        await using var session = _store.QuerySession();
        var profile = await session
            .LoadAsync<StudentProfileSnapshot>(studentAnonId, ct)
            .ConfigureAwait(false);

        if (profile is null || profile.ConceptMastery.Count == 0)
            return new Dictionary<string, AbilityEstimate>(capacity: 0);

        var now = DateTimeOffset.UtcNow;
        var map = new Dictionary<string, AbilityEstimate>(profile.ConceptMastery.Count);
        foreach (var (masteryKey, state) in profile.ConceptMastery)
        {
            if (string.IsNullOrWhiteSpace(masteryKey)) continue;
            if (state is null) continue;

            // StudentProfileSnapshot uses TENANCY-P2a mastery keys of the
            // form `{enrollmentId}:{conceptId}`. BagrutTopicWeights keys
            // on the raw topic slug, so extract the concept slug half.
            var (_, topicSlug) = Cena.Actors.Events.MasteryKeys.Parse(masteryKey);
            if (string.IsNullOrWhiteSpace(topicSlug)) continue;

            // θ ≈ 2 * (p − 0.5) keeps the sign/magnitude in the range
            // the scheduler's MasteryTargetTheta (+0.5) is written
            // against. A perfectly-mastered concept (p = 1.0) → θ = 1.0;
            // a cold concept (p = 0.0) → θ = -1.0. Standard error is
            // approximated from attempt count (1/sqrt(n)) with a floor
            // to match the AbilityEstimate shape; it has no effect on
            // the scheduler's priority formula (which only reads θ),
            // only on downstream bucket classification.
            var theta = 2.0 * (state.PKnown - 0.5);
            var se = state.TotalAttempts > 0
                ? 1.0 / Math.Sqrt(state.TotalAttempts)
                : 1.0;

            // If two mastery keys share the same topic slug (multiple
            // enrollments of the same student) the later one wins; for
            // a scheduler approximation this is acceptable.
            map[topicSlug] = new AbilityEstimate(
                StudentAnonId: studentAnonId,
                TopicSlug: topicSlug,
                Theta: theta,
                StandardError: se,
                SampleSize: state.TotalAttempts,
                ComputedAtUtc: state.LastAttemptedAt ?? now,
                ObservationWindowWeeks: 0);
        }

        return map;
    }
}
