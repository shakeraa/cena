// =============================================================================
// Cena Platform — IUnitEconomicsSnapshotStore (EPIC-PRR-I PRR-330)
//
// Persistence seam for weekly unit-economics snapshots. Two production
// implementations:
//
//   1. <see cref="InMemoryUnitEconomicsSnapshotStore"/> — single-host
//      default. Thread-safe, idempotent upsert, O(N) list. Suitable for
//      dev/CI + single-pod installs.
//   2. <see cref="MartenUnitEconomicsSnapshotStore"/> (Marten-mode only) —
//      persists across pod restarts and shares across replicas.
//
// Neither implementation is a stub (memory "No stubs — production grade",
// 2026-04-11): the InMemory variant is production-correct for single-host
// installs, and upgrades without code changes — the admin endpoint consumes
// the interface, not the concrete type.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Store for persisted weekly <see cref="UnitEconomicsSnapshotDocument"/>
/// rows. All operations are async + cancellation-aware.
/// </summary>
public interface IUnitEconomicsSnapshotStore
{
    /// <summary>
    /// Idempotently insert-or-update a snapshot keyed by
    /// <see cref="UnitEconomicsSnapshotDocument.Id"/>. A second call for
    /// the same week id overwrites the earlier row — so the rollup worker
    /// is safe to retry on pod restart.
    /// </summary>
    Task UpsertAsync(UnitEconomicsSnapshotDocument snapshot, CancellationToken ct);

    /// <summary>
    /// Return the most-recent <paramref name="takeWeeks"/> snapshots,
    /// ordered newest-first by <see cref="UnitEconomicsSnapshotDocument.WeekStartUtc"/>.
    /// </summary>
    /// <param name="takeWeeks">
    /// Maximum number of rows to return. Callers should clamp this at their
    /// boundary (the admin endpoint caps at 52); implementations MUST NOT
    /// load the entire table unbounded.
    /// </param>
    Task<IReadOnlyList<UnitEconomicsSnapshotDocument>> ListRecentAsync(
        int takeWeeks, CancellationToken ct);

    /// <summary>
    /// Fetch a single week's snapshot by id, or <c>null</c> if no row
    /// exists for that week. Used by the rollup worker to decide whether
    /// to skip (already-computed, idempotency path).
    /// </summary>
    Task<UnitEconomicsSnapshotDocument?> GetAsync(string weekId, CancellationToken ct);
}
