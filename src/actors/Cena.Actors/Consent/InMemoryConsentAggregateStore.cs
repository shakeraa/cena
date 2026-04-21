// =============================================================================
// Cena Platform — In-memory ConsentAggregate store (prr-155)
//
// Test-friendly in-memory implementation of IConsentAggregateStore.
// Thread-safe via ConcurrentDictionary; events are held in append order
// per stream.
//
// This implementation also serves as the Phase 1 production fallback for
// environments where the Marten-backed store has not yet been wired.
// A follow-up task (EPIC-PRR-A Sprint 2) will introduce MartenConsentStore
// behind the same interface; at that point DI will prefer the Marten
// variant and this one drops to tests-only.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Consent;

/// <summary>
/// In-memory implementation of <see cref="IConsentAggregateStore"/>.
/// </summary>
public sealed class InMemoryConsentAggregateStore : IConsentAggregateStore
{
    private readonly ConcurrentDictionary<string, List<object>> _streams = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task AppendAsync(string subjectId, object @event, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("subjectId must be non-empty.", nameof(subjectId));
        }
        ArgumentNullException.ThrowIfNull(@event);

        var list = _streams.GetOrAdd(subjectId, _ => new List<object>());
        lock (list)
        {
            list.Add(@event);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ConsentAggregate> LoadAsync(string subjectId, CancellationToken ct = default)
    {
        if (!_streams.TryGetValue(subjectId, out var list))
        {
            return Task.FromResult(new ConsentAggregate());
        }
        List<object> snapshot;
        lock (list)
        {
            snapshot = new List<object>(list);
        }
        return Task.FromResult(ConsentAggregate.ReplayFrom(snapshot));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<object>> ReadEventsAsync(
        string subjectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new ArgumentException("subjectId must be non-empty.", nameof(subjectId));
        }
        if (!_streams.TryGetValue(subjectId, out var list))
        {
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        }
        List<object> snapshot;
        lock (list)
        {
            snapshot = new List<object>(list);
        }
        return Task.FromResult<IReadOnlyList<object>>(snapshot);
    }

    /// <summary>
    /// Test/introspection helper: enumerate all subject ids with recorded
    /// events. Not part of the <see cref="IConsentAggregateStore"/> contract.
    /// </summary>
    public IEnumerable<string> KnownSubjects() => _streams.Keys.ToList();
}
