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
    private const string SubjectPrefix = "cena.events.";

    // ── Telemetry ──
    private static readonly ActivitySource ActivitySrc = new("Cena.Infrastructure.NatsOutbox", "1.0.0");
    private static readonly Meter Meter = new("Cena.Infrastructure.NatsOutbox", "1.0.0");
    private static readonly Counter<long> PublishedCounter =
        Meter.CreateCounter<long>("cena.outbox.published_total", description: "Events published to NATS");
    private static readonly Counter<long> CycleCounter =
        Meter.CreateCounter<long>("cena.outbox.cycles_total", description: "Outbox poll cycles");
    private static readonly Counter<long> ErrorCounter =
        Meter.CreateCounter<long>("cena.outbox.errors_total", description: "Outbox publish errors");
    private static readonly Histogram<double> CycleDurationMs =
        Meter.CreateHistogram<double>("cena.outbox.cycle_duration_ms", description: "Outbox cycle duration");

    public NatsOutboxPublisher(
        IDocumentStore documentStore,
        INatsConnection nats,
        ILogger<NatsOutboxPublisher> logger)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                ErrorCounter.Add(1);
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
        using var activity = ActivitySrc.StartActivity("NatsOutbox.PollAndPublish");
        var sw = Stopwatch.StartNew();
        CycleCounter.Add(1);

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
            CycleDurationMs.Record(sw.ElapsedMilliseconds);
            return;
        }

        int published = 0;
        long highestSequence = lastPublished;

        foreach (var eventWrapper in pendingEvents)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Determine NATS subject from event type
                string subject = $"{SubjectPrefix}{eventWrapper.EventTypeName}";

                // Serialize event data to JSON
                var payload = JsonSerializer.SerializeToUtf8Bytes(
                    eventWrapper.Data,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                // Publish to NATS and wait for confirmation
                await _nats.PublishAsync(subject, payload, cancellationToken: ct);

                highestSequence = eventWrapper.Sequence;
                published++;
                PublishedCounter.Add(1);
            }
            catch (Exception ex)
            {
                ErrorCounter.Add(1);
                _logger.LogError(ex,
                    "Failed to publish event {Sequence} ({Type}) to NATS. Will retry next cycle.",
                    eventWrapper.Sequence, eventWrapper.EventTypeName);
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
        CycleDurationMs.Record(sw.ElapsedMilliseconds);

        activity?.SetTag("outbox.published", published);
        activity?.SetTag("outbox.hwm", highestSequence);
        activity?.SetTag("outbox.duration_ms", sw.ElapsedMilliseconds);
    }
}
