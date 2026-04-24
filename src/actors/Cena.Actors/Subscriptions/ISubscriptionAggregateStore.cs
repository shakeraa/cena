// =============================================================================
// Cena Platform — ISubscriptionAggregateStore (EPIC-PRR-I PRR-300, ADR-0057)
//
// Narrow persistence seam. Swap the InMemory implementation for a Marten-
// backed implementation in a follow-up task (matches the ADR-0042 migration
// pattern used in neighboring bounded contexts).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Store abstraction for subscription aggregates. A concrete implementation
/// MUST preserve event ordering per stream and MUST be idempotent when
/// appending the same event twice (gateway-webhook-dedup).
/// </summary>
public interface ISubscriptionAggregateStore
{
    /// <summary>Load the aggregate for a parent. Returns a fresh instance if no stream.</summary>
    Task<SubscriptionAggregate> LoadAsync(string parentSubjectId, CancellationToken ct);

    /// <summary>
    /// Append the produced event to the stream for <paramref name="parentSubjectId"/>
    /// and apply it to the aggregate. Caller has already validated the command.
    /// </summary>
    Task AppendAsync(string parentSubjectId, object @event, CancellationToken ct);

    /// <summary>Read the event history for a parent (for read-model projection).</summary>
    Task<IReadOnlyList<object>> ReadEventsAsync(string parentSubjectId, CancellationToken ct);
}
