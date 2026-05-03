// =============================================================================
// Cena Platform — MartenUnitEconomicsSnapshotStore (EPIC-PRR-I PRR-330)
//
// Production Marten-backed implementation of <see cref="IUnitEconomicsSnapshotStore"/>.
// Wired by <c>AddSubscriptionsMarten</c>; replaces the InMemory default so
// weekly snapshots survive pod restarts and are shared across replicas.
//
// Pattern mirrors MartenExamTargetRetentionExtensionStore (prr-229) and
// MartenSkillKeyedMasteryStore (prr-222): document-style, single row per
// week keyed by <see cref="UnitEconomicsSnapshotDocument.Id"/>
// ("week-YYYY-MM-DD").
//
// Memory "No stubs — production grade": this is the canonical persistence
// path for the admin unit-economics dashboard history chart. An InMemory
// store that loses every rollup on pod restart would silently turn the
// history surface into a single-week view; the Marten variant is what
// makes the 12-week trend line real.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten-backed store for weekly unit-economics snapshots. Document is
/// registered via <see cref="SubscriptionMartenRegistration.RegisterSubscriptionsContext"/>.
/// </summary>
public sealed class MartenUnitEconomicsSnapshotStore : IUnitEconomicsSnapshotStore
{
    private readonly IDocumentStore _store;

    public MartenUnitEconomicsSnapshotStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        UnitEconomicsSnapshotDocument snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Id))
        {
            throw new ArgumentException(
                "Snapshot id must be non-empty.", nameof(snapshot));
        }

        await using var session = _store.LightweightSession();
        // Marten's Store is upsert-by-id — the row is overwritten if the
        // key already exists, which is exactly the idempotency contract.
        session.Store(snapshot);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitEconomicsSnapshotDocument>> ListRecentAsync(
        int takeWeeks, CancellationToken ct)
    {
        if (takeWeeks <= 0)
        {
            return Array.Empty<UnitEconomicsSnapshotDocument>();
        }

        await using var session = _store.QuerySession();
        var rows = await session
            .Query<UnitEconomicsSnapshotDocument>()
            .OrderByDescending(d => d.WeekStartUtc)
            .Take(takeWeeks)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows is IReadOnlyList<UnitEconomicsSnapshotDocument> ro
            ? ro
            : rows.ToArray();
    }

    /// <inheritdoc />
    public async Task<UnitEconomicsSnapshotDocument?> GetAsync(
        string weekId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(weekId))
        {
            return null;
        }

        await using var session = _store.QuerySession();
        return await session
            .LoadAsync<UnitEconomicsSnapshotDocument>(weekId, ct)
            .ConfigureAwait(false);
    }
}
