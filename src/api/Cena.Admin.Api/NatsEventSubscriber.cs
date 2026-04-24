// =============================================================================
// Cena Platform -- Admin API NATS Event Subscriber
// Subscribes to cena.events.> and maintains an in-memory buffer of recent events
// for the dashboard's real-time event stream and metrics.
// =============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Cena.Actors.Bus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Admin.Api;

public sealed class NatsEventSubscriber : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly ILogger<NatsEventSubscriber> _logger;
    private readonly ConcurrentQueue<NatsLiveEvent> _recentEvents = new();
    private long _totalEventsReceived;

    public NatsEventSubscriber(INatsConnection nats, ILogger<NatsEventSubscriber> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    /// <summary>Recent events buffer (last 200). Thread-safe.</summary>
    public IReadOnlyList<NatsLiveEvent> RecentEvents =>
        _recentEvents.ToArray().TakeLast(200).ToList();

    public long TotalEventsReceived => Interlocked.Read(ref _totalEventsReceived);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Admin NATS subscriber starting — listening on {Subject}", NatsSubjects.AllEvents);

        try
        {
            await foreach (var msg in _nats.SubscribeAsync<string>(NatsSubjects.AllEvents, cancellationToken: stoppingToken))
            {
                Interlocked.Increment(ref _totalEventsReceived);

                try
                {
                    var doc = JsonDocument.Parse(msg.Data!);
                    var root = doc.RootElement;

                    var liveEvent = new NatsLiveEvent(
                        Id: root.TryGetProperty("messageId", out var mid) ? mid.GetString() ?? "" : Guid.NewGuid().ToString("N"),
                        Subject: msg.Subject,
                        Source: root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
                        Timestamp: root.TryGetProperty("timestamp", out var ts) ? ts.GetDateTimeOffset() : DateTimeOffset.UtcNow,
                        PayloadJson: root.TryGetProperty("payload", out var pl) ? pl.GetRawText() : "{}");

                    _recentEvents.Enqueue(liveEvent);
                    while (_recentEvents.Count > 500)
                        _recentEvents.TryDequeue(out _);
                }
                catch (JsonException)
                {
                    // Ignore malformed messages
                }
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("Admin NATS subscriber stopped. Total events: {Count}", _totalEventsReceived);
    }
}

public sealed record NatsLiveEvent(
    string Id,
    string Subject,
    string Source,
    DateTimeOffset Timestamp,
    string PayloadJson);
