// =============================================================================
// Cena Platform — Marten-backed Concept Curation Calibration Counter
//
// Counts distinct question streams that have at least one
// QuestionConceptsConfirmed_V1 event. Backs the publish-gate check in
// QuestionBankService.PublishAsync per ADR-0062 §5 (first 200 calibration
// corpus). Reads from the Marten event log directly rather than a
// projection because:
//
//   * The count is rare-read (publish is infrequent vs. attempt / read).
//     A projection would be cache invalidation + index maintenance for a
//     query that runs maybe a few hundred times a day.
//   * Once the threshold crosses 200, this counter is never queried
//     again for that calibration window — the gate stays open forever
//     per ADR-0062 (monotone). Caching a "true" once is enough.
//   * Reading directly from the event store is the simplest pattern
//     that won't lie under projection lag — projection-replay-gap
//     defects are real per `feedback_event_sourcing_replay_check`, and
//     a publish-gate that under-counts because the projection is 30s
//     behind would let through items the curator hasn't actually
//     confirmed.
//
// Cache discipline:
//   * Once IsCalibrationCompleteAsync returns true, cache it forever in
//     this process. The threshold can only be crossed once.
//   * Below the threshold, cache the count for a short TTL (5 seconds)
//     so a burst of publish calls doesn't all hit the DB.
//   * The cache is per-instance; restarting the process re-reads on
//     first call. No cross-instance coherence problem because the count
//     is monotone.
// =============================================================================

using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Mastery.Extraction;

public sealed class MartenConceptCurationCalibrationCounter : IConceptCurationCalibrationCounter
{
    private readonly IDocumentStore _store;
    private readonly ILogger<MartenConceptCurationCalibrationCounter> _logger;
    private readonly int _threshold;

    // Memoised "calibration complete" flag. Set once on the first read
    // that observes count >= threshold; never reset. Monotone by ADR-0062.
    private volatile bool _calibrationComplete;

    // Short-TTL cache for the in-flight count (only consulted while
    // calibration is incomplete). Stores (timestamp, count). 5s is short
    // enough to feel real-time during an admin's multi-publish session
    // and long enough to absorb a kanban "publish all queued" batch.
    private readonly object _countCacheLock = new();
    private DateTimeOffset _countCacheAt = DateTimeOffset.MinValue;
    private int _cachedCount;
    private static readonly TimeSpan CountCacheTtl = TimeSpan.FromSeconds(5);

    public MartenConceptCurationCalibrationCounter(
        IDocumentStore store,
        IConfiguration configuration,
        ILogger<MartenConceptCurationCalibrationCounter> logger)
    {
        _store = store;
        _logger = logger;
        // ADR-0062 default 200; allow ops to tighten or loosen if
        // telemetry shows a different curator-precision plateau.
        _threshold = configuration.GetValue<int?>("Cena:Concepts:CalibrationThreshold")
                     ?? 200;
    }

    public int CalibrationThreshold => _threshold;

    public async Task<int> GetConfirmedItemCountAsync(CancellationToken ct = default)
    {
        if (_calibrationComplete)
            return _threshold;

        lock (_countCacheLock)
        {
            if (DateTimeOffset.UtcNow - _countCacheAt < CountCacheTtl)
                return _cachedCount;
        }

        int count;
        await using (var session = _store.QuerySession())
        {
            // QueryRawEventDataOnly<T>() makes Marten do the alias
            // resolution; earlier we tried filtering by EventTypeName
            // ourselves and the FullName/Name guesses both missed
            // (Marten serialises with a snake_cased alias by default,
            // surfaced by the integration test). QuestionId on the
            // event equals the stream id by contract — Distinct
            // collapses re-confirms (curator changing their mind on
            // the same item still counts once).
            var confirms = await session.Events
                .QueryRawEventDataOnly<Cena.Actors.Events.QuestionConceptsConfirmed_V1>()
                .ToListAsync(ct);

            count = confirms
                .Select(e => e.QuestionId)
                .Where(qid => !string.IsNullOrEmpty(qid))
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        if (count >= _threshold)
        {
            _calibrationComplete = true;
            _logger.LogInformation(
                "[ConceptCalibration] threshold reached — confirmed={Count}, threshold={Threshold}; gate open",
                count, _threshold);
            return _threshold;
        }

        lock (_countCacheLock)
        {
            _cachedCount = count;
            _countCacheAt = DateTimeOffset.UtcNow;
        }
        return count;
    }

    public async Task<bool> IsCalibrationCompleteAsync(CancellationToken ct = default)
    {
        if (_calibrationComplete) return true;
        var count = await GetConfirmedItemCountAsync(ct);
        return count >= _threshold;
    }
}
