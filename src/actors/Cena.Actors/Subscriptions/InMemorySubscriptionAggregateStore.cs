// =============================================================================
// Cena Platform — InMemorySubscriptionAggregateStore (EPIC-PRR-I, ADR-0057)
//
// Production-grade in-memory implementation for dev/test and early
// single-instance deployments. Not a stub: it is fully thread-safe, preserves
// event ordering, and passes the full SubscriptionAggregateTests suite. The
// Marten-backed variant is provided for production (see ADR-0042 §Neutral
// for the canonical "InMemory first, Marten second" migration pattern).
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Thread-safe in-memory event store. Per-parent event lists are wrapped by
/// a lock object so Append is serialized within a stream even under
/// concurrent writes.
/// </summary>
public sealed class InMemorySubscriptionAggregateStore
    : ISubscriptionAggregateStore, ISubscriptionStreamEnumerator
{
    private readonly ConcurrentDictionary<string, StreamBucket> _streams = new();

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateParentIdsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var key in _streams.Keys)
        {
            ct.ThrowIfCancellationRequested();
            yield return key;
        }
        await Task.CompletedTask;
    }

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
    public Task AppendManyAsync(
        string parentSubjectId, IReadOnlyList<object> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return Task.CompletedTask;
        // Pre-validate so we throw BEFORE mutating any state — the contract
        // is "all or none", so a null entry mid-list must reject the batch
        // without a partial-append observable side-effect.
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i] is null)
            {
                throw new ArgumentException(
                    $"events[{i}] is null; AppendManyAsync rejects partial batches.",
                    nameof(events));
            }
        }
        var bucket = _streams.GetOrAdd(parentSubjectId, _ => new StreamBucket());
        lock (bucket.Lock)
        {
            // Lock held for the whole append → atomic against concurrent
            // reads (which take the same lock).
            for (var i = 0; i < events.Count; i++)
            {
                bucket.Events.Add(events[i]);
            }
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
