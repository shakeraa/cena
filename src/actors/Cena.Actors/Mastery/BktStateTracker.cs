// =============================================================================
// Cena Platform — BKT state tracker default implementation (prr-222)
//
// Composes the existing BktTracer (pure math) with the skill-keyed
// mastery store. Every UpdateAsync call:
//
//   1. Loads the prior P(L) for the (student, target, skill) tuple
//      (defaulting to BktParameters.InitialPL when no row exists).
//   2. Runs BktTracer.Update for the observation.
//   3. Upserts the row (enforces the dedup invariant).
//   4. Emits a MasteryUpdated_V2 event for downstream projections /
//      audit via the injected event sink.
//
// The event sink is optional — tests can inject null to assert the
// pure-math behaviour without a stubbed sink.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery.Events;

namespace Cena.Actors.Mastery;

/// <summary>
/// Sink for <see cref="MasteryUpdated_V2"/> events. The default projection
/// builder wires this to the Marten event store; tests can supply an
/// in-memory collector.
/// </summary>
public interface IMasteryEventSink
{
    /// <summary>Append a V2 event to the downstream event log.</summary>
    Task AppendAsync(MasteryUpdated_V2 @event, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IBktStateTracker"/>. Pure composition over
/// <see cref="BktTracer"/>, <see cref="ISkillKeyedMasteryStore"/>, and an
/// optional <see cref="IMasteryEventSink"/>.
/// </summary>
public sealed class BktStateTracker : IBktStateTracker
{
    private readonly ISkillKeyedMasteryStore _store;
    private readonly IBktParameterProvider _parameters;
    private readonly IMasteryEventSink? _sink;

    /// <summary>
    /// Initial P(L) used when no row exists yet for a key. Mirrors
    /// <see cref="BktParameters.PInit"/> (Koedinger default per ADR-0039);
    /// keeping it explicit here so the tracker is self-documenting in
    /// isolation.
    /// </summary>
    public const float InitialPL = (float)BktParameters.PInit;

    public BktStateTracker(
        ISkillKeyedMasteryStore store,
        IBktParameterProvider parameters,
        IMasteryEventSink? sink = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _sink = sink;
    }

    /// <inheritdoc />
    public async Task<float> UpdateAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        SkillCode skillCode,
        bool isCorrect,
        DateTimeOffset occurredAt,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var key = new MasteryKey(studentAnonId, examTargetCode, skillCode);
        var prior = await _store.TryGetAsync(key, ct).ConfigureAwait(false);
        var priorProbability = prior?.MasteryProbability ?? InitialPL;
        var priorAttempts = prior?.AttemptCount ?? 0;

        var bktParams = _parameters.GetParameters(skillCode.Value);
        var posterior = BktTracer.Update(priorProbability, isCorrect, bktParams);

        var nextRow = new SkillKeyedMasteryRow(
            Key: key,
            MasteryProbability: posterior,
            AttemptCount: priorAttempts + 1,
            UpdatedAt: occurredAt,
            Source: MasteryEventSource.Native);

        await _store.UpsertAsync(nextRow, ct).ConfigureAwait(false);

        if (_sink is not null)
        {
            var @event = new MasteryUpdated_V2(
                StudentAnonId: studentAnonId,
                ExamTargetCode: examTargetCode,
                SkillCode: skillCode,
                MasteryProbability: posterior,
                UpdatedAt: occurredAt,
                Source: MasteryEventSource.Native);
            await _sink.AppendAsync(@event, ct).ConfigureAwait(false);
        }

        return posterior;
    }
}
