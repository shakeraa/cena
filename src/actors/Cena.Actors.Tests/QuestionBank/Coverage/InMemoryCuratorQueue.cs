// =============================================================================
// Cena Platform — In-memory curator queue (prr-201 tests)
//
// Deterministic ICuratorQueueEmitter for tests. Stores enqueued items in a
// dictionary keyed by the item's idempotency id; re-enqueueing the same id
// is a no-op (returns the existing id), matching the production contract.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.QuestionBank.Coverage;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

internal sealed class InMemoryCuratorQueue : ICuratorQueueEmitter
{
    private readonly ConcurrentDictionary<string, CuratorQueueItem> _items = new();

    public IReadOnlyCollection<CuratorQueueItem> Items => _items.Values.ToList();
    public int EnqueueCount { get; private set; }

    public Task<string> EnqueueAsync(CuratorQueueItem item, CancellationToken ct = default)
    {
        EnqueueCount++;
        _items.TryAdd(item.Id, item);
        return Task.FromResult(item.Id);
    }
}
