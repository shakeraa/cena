// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Messaging Marten Publisher
// Layer: Infrastructure | Runtime: .NET 9
// Persists messaging events to Marten event store for read model projection.
// FIND-data-012: Composite with NATS for dual-write pattern.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Events;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Messaging;

/// <summary>
/// Decorator that adds Marten persistence alongside NATS publishing.
/// Events are written to both stores; Marten drives the ThreadSummaryProjection,
/// NATS drives cross-context integration.
/// </summary>
public sealed class MessagingMartenPublisher : IMessagingEventPublisher
{
    private readonly IMessagingEventPublisher _inner;
    private readonly IDocumentStore _store;
    private readonly ILogger<MessagingMartenPublisher> _logger;

    public MessagingMartenPublisher(
        IMessagingEventPublisher inner,
        IDocumentStore store,
        ILogger<MessagingMartenPublisher> logger)
    {
        _inner = inner;
        _store = store;
        _logger = logger;
    }

    public async Task PublishMessageSentAsync(MessageSent_V1 evt)
    {
        // Persist to Marten for ThreadSummaryProjection
        await PersistEvent(evt.ThreadId, evt);
        // Also publish to NATS
        await _inner.PublishMessageSentAsync(evt);
    }

    public async Task PublishMessageReadAsync(MessageRead_V1 evt)
    {
        await PersistEvent(evt.ThreadId, evt);
        await _inner.PublishMessageReadAsync(evt);
    }

    public async Task PublishThreadCreatedAsync(ThreadCreated_V1 evt)
    {
        await PersistEvent(evt.ThreadId, evt);
        await _inner.PublishThreadCreatedAsync(evt);
    }

    public async Task PublishThreadMutedAsync(ThreadMuted_V1 evt)
    {
        await PersistEvent(evt.ThreadId, evt);
        await _inner.PublishThreadMutedAsync(evt);
    }

    public async Task PublishMessageBlockedAsync(MessageBlocked_V1 evt)
    {
        await PersistEvent(evt.ThreadId, evt);
        await _inner.PublishMessageBlockedAsync(evt);
    }

    public Task PublishInboundReceivedAsync(string source, string externalId, string text)
    {
        // Inbound received is for external routing only — no persistence needed
        return _inner.PublishInboundReceivedAsync(source, externalId, text);
    }

    private async Task PersistEvent<T>(string streamId, T evt) where T : class
    {
        try
        {
            await using var session = _store.LightweightSession();
            session.Events.Append(streamId, evt);
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log but don't throw — NATS is the primary transport,
            // Marten is for read model convenience
            _logger.LogError(ex, "Marten persist failed for event to stream {StreamId}", streamId);
        }
    }
}
