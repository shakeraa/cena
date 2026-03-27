// =============================================================================
// Cena Platform -- NatsOutboxPublisher (Background Hosted Service)
// Layer: Infrastructure | Runtime: .NET 9
//
// Background service that polls Marten for events not yet published to NATS.
// Tracks the last-published sequence number in a Marten document (high-water
// mark). Publishes in order, advances marker after NATS confirm.
// Runs every 5 seconds, max 100 events per cycle.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Infrastructure;

// =============================================================================
// HIGH-WATER MARK TRACKING DOCUMENT
// =============================================================================

/// <summary>
/// Marten document tracking the last event sequence published to NATS.
/// Singleton document keyed by a well-known ID.
/// </summary>
public sealed class NatsOutboxCheckpoint
{
    public const string CheckpointId = "nats-outbox-hwm";

    public string Id { get; set; } = CheckpointId;

    /// <summary>The last Marten event sequence number that was successfully published to NATS.</summary>
    public long LastPublishedSequence { get; set; }

    /// <summary>Timestamp of the last successful publish.</summary>
    public DateTimeOffset LastPublishedAt { get; set; }

    /// <summary>Total events published since service start.</summary>
    public long TotalPublished { get; set; }
}

// =============================================================================
// HOSTED SERVICE
// =============================================================================

/// <summary>
/// Background hosted service implementing the transactional outbox pattern.
/// Polls Marten event store for events beyond the last-published sequence,
/// publishes them to NATS in order, and advances the high-water mark after
/// NATS confirmation.
/// </summary>
public sealed class NatsOutboxPublisher : BackgroundService
{
    private readonly IDocumentStore _documentStore;
    private readonly INatsConnection _nats;
    private readonly ILogger<NatsOutboxPublisher> _logger;

    // ── Configuration ──
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int MaxEventsPerCycle = 100;
    private const int MaxRetries = 10; // RES-008: dead-letter after 10 retries
    private const string SubjectPrefix = "cena.events.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── RES-008: Retry tracking (in-memory, reset on process restart) ──
    private readonly Dictionary<long, int> _retryCountBySequence = new();

    // ── Telemetry (ACT-031: instance-based via IMeterFactory) ──
    private readonly ActivitySource _activitySource;
    private readonly Counter<long> _publishedCounter;
    private readonly Counter<long> _cycleCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _cycleDurationMs;
    private readonly Counter<long> _republishedCounter;
    private readonly Counter<long> _deadLetteredCounter;

    public NatsOutboxPublisher(
        IDocumentStore documentStore,
        INatsConnection nats,
        ILogger<NatsOutboxPublisher> logger,
        IMeterFactory meterFactory)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _activitySource = new ActivitySource("Cena.Infrastructure.NatsOutbox", "1.0.0");
        var meter = meterFactory.Create("Cena.Infrastructure.NatsOutbox", "1.0.0");
        _publishedCounter = meter.CreateCounter<long>("cena.outbox.published_total", description: "Events published to NATS");
        _cycleCounter = meter.CreateCounter<long>("cena.outbox.cycles_total", description: "Outbox poll cycles");
        _errorCounter = meter.CreateCounter<long>("cena.outbox.errors_total", description: "Outbox publish errors");
        _cycleDurationMs = meter.CreateHistogram<double>("cena.outbox.cycle_duration_ms", description: "Outbox cycle duration");
        _republishedCounter = meter.CreateCounter<long>("cena.outbox.republished_total", description: "Events re-published after retry");
        _deadLetteredCounter = meter.CreateCounter<long>("cena.outbox.dead_lettered_total", description: "Events moved to dead letter after max retries");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NatsOutboxPublisher started. PollInterval={Interval}s, MaxPerCycle={Max}",
            PollInterval.TotalSeconds, MaxEventsPerCycle);

        // Wait briefly for the application to fully start before polling
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndPublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _errorCounter.Add(1);
                _logger.LogError(ex, "NatsOutboxPublisher cycle failed. Will retry in {Interval}s.",
                    PollInterval.TotalSeconds);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("NatsOutboxPublisher stopping.");
    }

    /// <summary>
    /// Single poll cycle: load the checkpoint, query events after that sequence,
    /// publish to NATS in order, advance the checkpoint.
    /// </summary>
    private async Task PollAndPublishAsync(CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("NatsOutbox.PollAndPublish");
        var sw = Stopwatch.StartNew();
        _cycleCounter.Add(1);

        await using var session = _documentStore.LightweightSession();

        // Load the current high-water mark
        var checkpoint = await session.LoadAsync<NatsOutboxCheckpoint>(
            NatsOutboxCheckpoint.CheckpointId, ct);

        long lastPublished = checkpoint?.LastPublishedSequence ?? 0;

        // Query events after the last published sequence, in order
        var pendingEvents = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.Sequence > lastPublished)
            .OrderBy(e => e.Sequence)
            .Take(MaxEventsPerCycle)
            .ToListAsync(ct);

        if (pendingEvents.Count == 0)
        {
            sw.Stop();
            _cycleDurationMs.Record(sw.ElapsedMilliseconds);
            return;
        }

        int published = 0;
        long highestSequence = lastPublished;

        foreach (var eventWrapper in pendingEvents)
        {
            if (ct.IsCancellationRequested) break;

            // RES-008: Check retry count — dead-letter events that exceeded max retries
            var retryCount = _retryCountBySequence.GetValueOrDefault(eventWrapper.Sequence, 0);
            if (retryCount >= MaxRetries)
            {
                _deadLetteredCounter.Add(1);
                _logger.LogError(
                    "Dead-lettering event {Sequence} ({Type}) after {MaxRetries} failed retries. " +
                    "Event will not be delivered to NATS consumers.",
                    eventWrapper.Sequence, eventWrapper.EventTypeName, MaxRetries);

                // Store dead letter record in Marten for audit
                session.Store(new NatsOutboxDeadLetter
                {
                    EventSequence = eventWrapper.Sequence,
                    StreamId = eventWrapper.StreamKey ?? "unknown",
                    EventType = eventWrapper.EventTypeName,
                    RetryCount = retryCount,
                    DeadLetteredAt = DateTimeOffset.UtcNow
                });

                // Skip past this event
                _retryCountBySequence.Remove(eventWrapper.Sequence);
                highestSequence = eventWrapper.Sequence;
                published++; // Count as processed (not delivered) to advance HWM
                continue;
            }

            try
            {
                // Determine NATS subject from event type
                string subject = $"{SubjectPrefix}{eventWrapper.EventTypeName}";

                // Serialize event data to JSON
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    eventWrapper.Data, JsonOptions);

                // Publish to NATS and wait for confirmation
                await _nats.PublishAsync(subject, payload, cancellationToken: ct);

                highestSequence = eventWrapper.Sequence;
                published++;
                _publishedCounter.Add(1);

                // RES-008: Track re-publishes (retryCount > 0 means this was a retry)
                if (retryCount > 0)
                {
                    _republishedCounter.Add(1);
                    _retryCountBySequence.Remove(eventWrapper.Sequence);
                }
            }
            catch (Exception ex)
            {
                // RES-008: Increment retry counter for this sequence
                _retryCountBySequence[eventWrapper.Sequence] = retryCount + 1;
                _errorCounter.Add(1);
                _logger.LogError(ex,
                    "Failed to publish event {Sequence} ({Type}) to NATS. " +
                    "Retry {Retry}/{MaxRetries}. Will retry next cycle.",
                    eventWrapper.Sequence, eventWrapper.EventTypeName,
                    retryCount + 1, MaxRetries);
                // Stop this cycle on first publish failure to maintain ordering
                break;
            }
        }

        // Advance the high-water mark checkpoint
        if (published > 0)
        {
            if (checkpoint == null)
            {
                checkpoint = new NatsOutboxCheckpoint
                {
                    LastPublishedSequence = highestSequence,
                    LastPublishedAt = DateTimeOffset.UtcNow,
                    TotalPublished = published
                };
                session.Store(checkpoint);
            }
            else
            {
                checkpoint.LastPublishedSequence = highestSequence;
                checkpoint.LastPublishedAt = DateTimeOffset.UtcNow;
                checkpoint.TotalPublished += published;
                session.Store(checkpoint);
            }

            await session.SaveChangesAsync(ct);

            _logger.LogDebug(
                "NatsOutbox cycle: published {Count} events in {Duration}ms. HWM={Sequence}",
                published, sw.ElapsedMilliseconds, highestSequence);
        }

        sw.Stop();
        _cycleDurationMs.Record(sw.ElapsedMilliseconds);

        activity?.SetTag("outbox.published", published);
        activity?.SetTag("outbox.hwm", highestSequence);
        activity?.SetTag("outbox.duration_ms", sw.ElapsedMilliseconds);
    }
}

// =============================================================================
// RES-008: DEAD LETTER TRACKING DOCUMENT
// =============================================================================

/// <summary>
/// Marten document recording events that failed NATS delivery after max retries.
/// Persisted for audit and manual re-processing.
/// </summary>
public sealed class NatsOutboxDeadLetter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long EventSequence { get; set; }
    public string StreamId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int RetryCount { get; set; }
    public DateTimeOffset DeadLetteredAt { get; set; }
}
