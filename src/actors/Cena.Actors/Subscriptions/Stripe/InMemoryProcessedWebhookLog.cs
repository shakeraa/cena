// =============================================================================
// Cena Platform — InMemoryProcessedWebhookLog (EPIC-PRR-I PRR-301)
//
// Tracks processed Stripe event IDs in memory. Production swap-out: a Marten
// doc or Redis set keyed by Stripe event id with TTL = Stripe's 7-day replay
// window. Interface is narrow; swap is mechanical.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>In-memory dedup log for Stripe event ids. Single-instance only.</summary>
public sealed class InMemoryProcessedWebhookLog : IProcessedWebhookLog
{
    private readonly ConcurrentDictionary<string, byte> _seen = new();

    /// <inheritdoc/>
    public Task<bool> TryRegisterAsync(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Event id required.", nameof(eventId));
        }
        var isNew = _seen.TryAdd(eventId, 0);
        return Task.FromResult(isNew);
    }
}
