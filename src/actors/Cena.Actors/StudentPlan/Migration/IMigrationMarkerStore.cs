// =============================================================================
// Cena Platform — IMigrationMarkerStore (prr-219)
//
// Optional companion interface on IStudentPlanAggregateStore that exposes
// whether a migration marker has already been written for a given
// (studentAnonId, migrationSourceId) pair. Separate interface so the
// Marten-backed production store can implement it with an indexed query
// without forcing every store to scan the full stream.
//
// When a store does NOT implement this interface, the migration service
// falls back to loading the aggregate and scanning for the
// StudentPlanMigrated_V1 event manually — slower but correct.
// =============================================================================

namespace Cena.Actors.StudentPlan.Migration;

/// <summary>
/// Fast migration-marker lookup. Implementors SHOULD ensure this is
/// O(1) or O(log n) in the number of events on a stream — not a full
/// scan — so idempotency checks don't become a migration bottleneck.
/// </summary>
public interface IMigrationMarkerStore
{
    /// <summary>
    /// Return true when a <c>StudentPlanMigrated_V1</c> event with the
    /// given <paramref name="migrationSourceId"/> has already been
    /// written to the student's stream.
    /// </summary>
    Task<bool> HasMarkerAsync(
        string studentAnonId,
        string migrationSourceId,
        CancellationToken ct = default);
}
