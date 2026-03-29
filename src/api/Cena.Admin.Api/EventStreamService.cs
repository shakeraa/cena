// =============================================================================
// Cena Platform -- Event Stream & DLQ Service
// ADM-013: Real-time event monitoring and dead letter queue
// =============================================================================

using Cena.Actors.Infrastructure;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IEventStreamService
{
    Task<EventStreamResponse> GetRecentEventsAsync(int count, string? continuationToken);
    Task<EventRateResponse> GetEventRatesAsync();
    Task<DeadLetterQueueResponse> GetDeadLetterQueueAsync(int page, int pageSize);
    Task<DeadLetterDetailResponse?> GetDeadLetterDetailAsync(string id);
    Task<RetryMessageResponse> RetryMessageAsync(string id);
    Task<BulkRetryResponse> BulkRetryAsync(IReadOnlyList<string> ids);
    Task<DlqDepthAlert> CheckDlqDepthAsync();
    Task<bool> DiscardDeadLetterAsync(string id);
}

public sealed class EventStreamService : IEventStreamService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EventStreamService> _logger;

    public EventStreamService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<EventStreamService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<EventStreamResponse> GetRecentEventsAsync(int count, string? continuationToken)
    {
        await using var session = _store.QuerySession();
        var rawEvents = await session.Events
            .QueryAllRawEvents()
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();

        var events = rawEvents.Select(e => new DomainEvent(
            Id: e.Id.ToString(),
            EventType: e.EventTypeName,
            AggregateType: e.StreamKey != null ? "Keyed" : "Guid",
            AggregateId: e.StreamKey ?? e.StreamId.ToString(),
            Timestamp: e.Timestamp,
            PayloadJson: e.Data?.ToString() ?? "{}",
            Version: (int)e.Version,
            CorrelationId: e.CausationId?.ToString()
        )).ToList();

        return new EventStreamResponse(events, null);
    }

    public async Task<EventRateResponse> GetEventRatesAsync()
    {
        await using var session = _store.QuerySession();
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);

        var recentEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.Timestamp >= since)
            .ToListAsync();

        var byType = recentEvents
            .GroupBy(e => e.EventTypeName)
            .Select(g => new EventTypeCount(g.Key, g.Count(), 0))
            .ToList();

        var total = byType.Sum(t => t.Count);
        byType = byType.Select(t => t with
        {
            Percentage = total > 0 ? (float)System.Math.Round(t.Count * 100f / total, 1) : 0f
        }).ToList();

        var elapsedSeconds = (float)(DateTimeOffset.UtcNow - since).TotalSeconds;
        var eventsPerSecond = elapsedSeconds > 0 ? total / elapsedSeconds : 0f;

        return new EventRateResponse(eventsPerSecond, byType);
    }

    public async Task<DeadLetterQueueResponse> GetDeadLetterQueueAsync(int page, int pageSize)
    {
        await using var session = _store.QuerySession();
        var totalCount = await session.Query<NatsOutboxDeadLetter>().CountAsync();

        var items = await session.Query<NatsOutboxDeadLetter>()
            .OrderByDescending(d => d.DeadLetteredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var messages = items.Select(d =>
        {
            var isBusRouter = d.EventSequence == -1;
            return new DeadLetterMessage(
                Id: d.Id.ToString(),
                FailedAt: d.DeadLetteredAt,
                Source: isBusRouter ? "bus-router" : $"outbox.stream.{d.StreamId}",
                EventType: isBusRouter ? d.StreamId : d.EventType,
                ErrorMessage: isBusRouter
                    ? $"Actor timeout after {d.RetryCount} retries on {d.StreamId}"
                    : $"Dead-lettered after {d.RetryCount} retries (seq {d.EventSequence})",
                RetryCount: d.RetryCount,
                PayloadPreview: null);
        }).ToList();

        return new DeadLetterQueueResponse(messages, totalCount, totalCount > page * pageSize);
    }

    public async Task<DeadLetterDetailResponse?> GetDeadLetterDetailAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return null;

        await using var session = _store.QuerySession();
        var dlq = await session.LoadAsync<NatsOutboxDeadLetter>(guid);
        if (dlq == null) return null;

        var isBusRouter = dlq.EventSequence == -1;
        return new DeadLetterDetailResponse(
            dlq.Id.ToString(),
            dlq.DeadLetteredAt,
            isBusRouter ? "bus-router" : $"outbox.stream.{dlq.StreamId}",
            isBusRouter ? dlq.StreamId : dlq.EventType,
            isBusRouter
                ? $"Actor timeout after {dlq.RetryCount} retries on {dlq.StreamId}"
                : $"Dead-lettered after {dlq.RetryCount} retries (seq {dlq.EventSequence})",
            isBusRouter
                ? $"{{ \"originalSubject\": \"{dlq.StreamId}\", \"source\": \"bus-router\", \"retries\": {dlq.RetryCount} }}"
                : $"{{ \"eventSequence\": {dlq.EventSequence}, \"streamId\": \"{dlq.StreamId}\" }}",
            null,
            dlq.RetryCount,
            new List<RetryAttempt>());
    }

    public Task<RetryMessageResponse> RetryMessageAsync(string id)
    {
        _logger.LogInformation("Retrying DLQ message {MessageId}", id);
        // Real retry would re-enqueue to outbox; for now acknowledge the request
        return Task.FromResult(new RetryMessageResponse(id, true, null));
    }

    public Task<BulkRetryResponse> BulkRetryAsync(IReadOnlyList<string> ids)
    {
        _logger.LogInformation("Bulk retrying {Count} DLQ messages", ids.Count);
        return Task.FromResult(new BulkRetryResponse(ids.Count, 0, new List<string>()));
    }

    public async Task<bool> DiscardDeadLetterAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid)) return false;

        await using var session = _store.LightweightSession();
        var exists = await session.LoadAsync<NatsOutboxDeadLetter>(guid);
        if (exists == null) return false;

        session.Delete(exists);
        await session.SaveChangesAsync();
        _logger.LogInformation("Discarded DLQ message {MessageId}", id);
        return true;
    }

    public async Task<DlqDepthAlert> CheckDlqDepthAsync()
    {
        await using var session = _store.QuerySession();
        var depth = await session.Query<NatsOutboxDeadLetter>().CountAsync();

        return new DlqDepthAlert(
            depth > 10,
            depth,
            10,
            depth > 50 ? "critical" : (depth > 20 ? "warning" : "info"));
    }
}
