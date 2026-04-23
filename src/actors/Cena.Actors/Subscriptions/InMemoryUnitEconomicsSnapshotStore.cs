// =============================================================================
// Cena Platform — InMemoryUnitEconomicsSnapshotStore (EPIC-PRR-I PRR-330)
//
// Single-host in-memory implementation of <see cref="IUnitEconomicsSnapshotStore"/>.
// Thread-safe; idempotent; does NOT survive a pod restart.
//
// This is production-grade for single-host deployments (no Marten wired):
// every write goes through the dictionary under the gate, every list is a
// snapshot copy, and callers see consistent ordering regardless of
// concurrent writers. For multi-host deployments the Marten variant takes
// over via the DI registration switch in SubscriptionServiceRegistration.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Single-host unit-economics snapshot store. Safe for the default
/// <c>AddSubscriptions</c> composition (dev, CI, single-pod installs).
/// </summary>
public sealed class InMemoryUnitEconomicsSnapshotStore : IUnitEconomicsSnapshotStore
{
    // Keyed by UnitEconomicsSnapshotDocument.Id so upsert is a single
    // TryAdd-then-overwrite on the concurrent dict.
    private readonly ConcurrentDictionary<string, UnitEconomicsSnapshotDocument> _rows = new();

    /// <inheritdoc />
    public Task UpsertAsync(UnitEconomicsSnapshotDocument snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        // ConcurrentDictionary's indexer semantics: overwrite on duplicate
        // key. That's the exact idempotent-upsert contract the worker
        // needs — a second run for the same week just replaces the row.
        _rows[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UnitEconomicsSnapshotDocument>> ListRecentAsync(
        int takeWeeks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (takeWeeks <= 0)
        {
            return Task.FromResult<IReadOnlyList<UnitEconomicsSnapshotDocument>>(
                Array.Empty<UnitEconomicsSnapshotDocument>());
        }

        // Snapshot the values first, then sort — avoids holding an enumerator
        // across a concurrent upsert.
        var rows = _rows.Values
            .OrderByDescending(r => r.WeekStartUtc)
            .Take(takeWeeks)
            .ToArray();

        return Task.FromResult<IReadOnlyList<UnitEconomicsSnapshotDocument>>(rows);
    }

    /// <inheritdoc />
    public Task<UnitEconomicsSnapshotDocument?> GetAsync(string weekId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(weekId))
        {
            return Task.FromResult<UnitEconomicsSnapshotDocument?>(null);
        }

        _rows.TryGetValue(weekId, out var doc);
        return Task.FromResult<UnitEconomicsSnapshotDocument?>(doc);
    }
}
