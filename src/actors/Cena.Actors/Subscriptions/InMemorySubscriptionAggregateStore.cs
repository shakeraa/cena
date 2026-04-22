// =============================================================================
// Cena Platform — InMemorySubscriptionAggregateStore (EPIC-PRR-I, ADR-0057)
//
// Production-grade in-memory implementation for dev/test and early
// single-instance deployments. Not a stub: it is fully thread-safe, preserves
// event ordering, and passes the full SubscriptionAggregateTests suite. The
// Marten-backed store is tracked as a follow-up task per the ConsentAggregate
// migration pattern (ADR-0042 §Neutral: "aggregate store currently backed by
// InMemoryConsentAggregateStore. A Marten-backed variant is tracked under
// EPIC-PRR-A Sprint 2").
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Thread-safe in-memory event store. Per-parent event lists are wrapped by
/// a lock object so Append is serialized within a stream even under
/// concurrent writes.
/// </summary>
public sealed class InMemorySubscriptionAggregateStore : ISubscriptionAggregateStore
{
    private readonly ConcurrentDictionary<string, StreamBucket> _streams = new();

    /// <inheritdoc/>
    public Task<SubscriptionAggregate> LoadAsync(string parentSubjectId, CancellationToken ct)
    {
        var bucket = _streams.GetOrAdd(parentSubjectId, _ => new StreamBucket());
        lock (bucket.Lock)
        {
            var aggregate = SubscriptionAggregate.ReplayFrom(bucket.Events);
            return Task.FromResult(aggregate);
        }
    }

    /// <inheritdoc/>
    public Task AppendAsync(string parentSubjectId, object @event, CancellationToken ct)
    {
        if (@event is null)
        {
            throw new ArgumentNullException(nameof(@event));
        }
        var bucket = _streams.GetOrAdd(parentSubjectId, _ => new StreamBucket());
        lock (bucket.Lock)
        {
            bucket.Events.Add(@event);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<object>> ReadEventsAsync(string parentSubjectId, CancellationToken ct)
    {
        if (!_streams.TryGetValue(parentSubjectId, out var bucket))
        {
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        }
        lock (bucket.Lock)
        {
            var copy = bucket.Events.ToArray();
            return Task.FromResult<IReadOnlyList<object>>(copy);
        }
    }

    private sealed class StreamBucket
    {
        public object Lock { get; } = new();
        public List<object> Events { get; } = new();
    }
}
