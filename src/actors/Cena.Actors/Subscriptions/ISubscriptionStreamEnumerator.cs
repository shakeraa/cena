// =============================================================================
// Cena Platform — ISubscriptionStreamEnumerator (Phase 1D, support for worker)
//
// Narrow read-side seam used by background workers (TrialExpiryWorker
// today; the lifecycle-email worker historically went a different route)
// that need to fan out across all currently-known subscription streams.
//
// Distinct from ISubscriptionAggregateStore on purpose:
//   * ISubscriptionAggregateStore is the per-stream Load/Append surface
//     (the hot path on the request side).
//   * ISubscriptionStreamEnumerator is the cross-stream enumeration
//     surface used by once-a-tick workers — it is intentionally async-
//     enumerable so a Marten implementation can stream rather than
//     materialise the full set in memory.
//
// Two implementations:
//   - InMemorySubscriptionStreamEnumerator (wraps the InMemory store)
//   - MartenSubscriptionStreamEnumerator   (queries the Marten event log)
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Cross-stream enumeration surface for Subscriptions. Used by the
/// once-a-tick <see cref="TrialExpiryWorker"/> and any future bulk-scan
/// worker (e.g., reconciliation against Stripe).
/// </summary>
public interface ISubscriptionStreamEnumerator
{
    /// <summary>
    /// Yield the parent subject id of every currently-known subscription
    /// stream. Order is implementation-defined; callers MUST treat the
    /// stream as unordered. Empty when no streams exist.
    /// </summary>
    IAsyncEnumerable<string> EnumerateParentIdsAsync(CancellationToken ct);
}
