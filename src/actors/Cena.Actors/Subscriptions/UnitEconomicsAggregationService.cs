// =============================================================================
// Cena Platform — UnitEconomicsAggregationService (EPIC-PRR-I PRR-330)
//
// Reads the Marten event store for a window and feeds the pure
// UnitEconomicsCalculator. Production use: scheduled weekly by a
// hosted-service; on-demand by the admin dashboard endpoint.
//
// Streams the subscription events between windowStart/windowEnd, derives
// a summary per parent (current tier + status), and hands both to the
// calculator. Attribution of renewals/cancels to a specific tier within
// the window relies on the current summary tier — a refinement that does
// per-event tier history is deferred to a follow-up Marten projection.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Aggregates subscription events from Marten into a
/// <see cref="UnitEconomicsSnapshot"/>. Runs reads; does not write.
/// </summary>
public sealed class UnitEconomicsAggregationService
{
    private readonly IDocumentStore _store;

    public UnitEconomicsAggregationService(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Compute a snapshot by reading all subscription streams active in the
    /// window. Caller owns the window boundaries — typically 7 days for the
    /// weekly-rollup worker, 30 days for the admin dashboard view.
    /// </summary>
    public async Task<UnitEconomicsSnapshot> ComputeAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        if (windowEnd <= windowStart)
        {
            throw new ArgumentException(
                "Window end must be after start.", nameof(windowEnd));
        }

        await using var session = _store.QuerySession();

        // Fetch all subscription event streams. At pilot scale a scan is
        // acceptable; at 10k+ subscriptions this should back onto an indexed
        // Marten event-stream metadata query.
        var allEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.Timestamp >= windowStart && e.Timestamp < windowEnd)
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);

        // Group events by stream key to build per-parent summaries.
        var summaries = new List<SubscriptionSummary>();
        foreach (var group in allEvents.GroupBy(e => e.StreamKey))
        {
            var streamEvents = group.Select(e => e.Data).ToList();
            var aggregate = SubscriptionAggregate.ReplayFrom(streamEvents);
            if (aggregate.State.Status != SubscriptionStatus.Unsubscribed)
            {
                summaries.Add(new SubscriptionSummary(
                    Tier: aggregate.State.CurrentTier,
                    Status: aggregate.State.Status));
            }
        }

        var eventData = allEvents.Select(e => e.Data).ToList();
        return UnitEconomicsCalculator.Compute(windowStart, windowEnd, summaries, eventData);
    }
}
