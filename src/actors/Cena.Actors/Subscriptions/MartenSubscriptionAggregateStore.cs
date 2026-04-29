// =============================================================================
// Cena Platform — MartenSubscriptionAggregateStore (EPIC-PRR-I PRR-300)
//
// Production Marten-backed implementation of ISubscriptionAggregateStore.
// Pattern mirrors StudentAdvancementService (the canonical aggregate-stream
// pattern in this repo): AggregateStreamAsync to read, StartStream/Append
// for writes, SaveChangesAsync to commit.
//
// Stream identity: string-typed, keyed by `subscription-{parentSubjectId}`.
// Per MartenConfiguration StreamIdentity = AsString.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten-backed event store for <see cref="SubscriptionAggregate"/>.
/// Thread-safety delegated to Marten's session-per-unit-of-work model
/// (a fresh <c>LightweightSession</c> per call).
/// </summary>
public sealed class MartenSubscriptionAggregateStore
    : ISubscriptionAggregateStore, ISubscriptionStreamEnumerator
{
    private readonly IDocumentStore _store;

    public MartenSubscriptionAggregateStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateParentIdsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        // Marten doesn't expose a dedicated "list distinct stream keys" API;
        // we walk the event log filtered to subscription-* streams. Pilot
        // scale (<1k streams in flight) means a single materialise-and-
        // distinct is fine. At larger scale a dedicated index document
        // (TrialingSubscriptionIndex) replaces this scan — that swap lives
        // behind this same interface.
        var keys = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .Select(e => e.StreamKey!)
            .Distinct()
            .ToListAsync(ct);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            // Strip the "subscription-" prefix so the caller works in
            // parent-subject-id space, not stream-key space.
            yield return key.Substring(SubscriptionAggregate.StreamKeyPrefix.Length);
        }
    }

    /// <inheritdoc/>
    public async Task<SubscriptionAggregate> LoadAsync(string parentSubjectId, CancellationToken ct)
    {
        var streamKey = SubscriptionAggregate.StreamKey(parentSubjectId);
        await using var session = _store.LightweightSession();
        var events = await session.Events.FetchStreamAsync(streamKey, token: ct);
        var aggregate = new SubscriptionAggregate();
        foreach (var e in events)
        {
            aggregate.Apply(e.Data);
        }
        return aggregate;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(string parentSubjectId, object @event, CancellationToken ct)
    {
        if (@event is null) throw new ArgumentNullException(nameof(@event));

        var streamKey = SubscriptionAggregate.StreamKey(parentSubjectId);
        await using var session = _store.LightweightSession();

        // First event opens the stream; subsequent events append.
        var existing = await session.Events.FetchStreamStateAsync(streamKey, token: ct);
        if (existing is null)
        {
            session.Events.StartStream<SubscriptionAggregate>(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }
        await session.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<object>> ReadEventsAsync(string parentSubjectId, CancellationToken ct)
    {
        var streamKey = SubscriptionAggregate.StreamKey(parentSubjectId);
        await using var session = _store.QuerySession();
        var events = await session.Events.FetchStreamAsync(streamKey, token: ct);
        return events.Select(e => e.Data).ToArray();
    }
}
