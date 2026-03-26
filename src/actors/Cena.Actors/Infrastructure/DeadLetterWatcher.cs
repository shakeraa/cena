// =============================================================================
// Cena Platform -- DeadLetterWatcher (Infrastructure)
// Layer: Infrastructure | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Subscribes to Proto.Actor's DeadLetterEvent stream.
// Tracks dead letter counts per message type, detects poison messages
// (same type fails 3+ times -> quarantine), and provides alerting
// when dead letter rate exceeds 10/minute.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Infrastructure;

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>Query current dead letter statistics.</summary>
public sealed record GetDeadLetterStats;

/// <summary>Dead letter statistics response.</summary>
public sealed record DeadLetterStatsResponse(
    long TotalDeadLetters,
    IReadOnlyDictionary<string, long> CountsByType,
    IReadOnlyList<string> QuarantinedTypes,
    double CurrentRatePerMinute);

// =============================================================================
// DEAD LETTER WATCHER
// =============================================================================

/// <summary>
/// Infrastructure component that monitors Proto.Actor's dead letter stream.
///
/// Responsibilities:
///   1. Count dead letters per message type
///   2. Detect poison messages (same type fails 3+ times -> quarantine)
///   3. Track dead letter rate and alert when exceeding 10/minute
///   4. Expose metrics via OpenTelemetry
///
/// Usage: instantiate and call Subscribe() to attach to an ActorSystem's EventStream.
/// Can also be spawned as an actor for query support.
/// </summary>
public sealed class DeadLetterWatcher : IActor, IDisposable
{
    private readonly ILogger<DeadLetterWatcher> _logger;

    // ── Configuration ──
    private const int PoisonThreshold = 3;
    private const double AlertRatePerMinute = 10.0;
    private const int RateWindowSeconds = 60;

    // ── State ──
    private readonly ConcurrentDictionary<string, long> _countsByType = new();
    private readonly ConcurrentDictionary<string, int> _consecutiveFailsByType = new();
    private readonly ConcurrentDictionary<string, bool> _quarantinedTypes = new();
    private long _totalDeadLetters;

    // ── Rate tracking (sliding window) ──
    private readonly ConcurrentQueue<long> _recentTimestamps = new();

    // ── Subscription ──
    private EventStreamSubscription<object>? _subscription;

    // ── Telemetry ──
    private static readonly Meter Meter = new("Cena.Actors.DeadLetterWatcher", "1.0.0");
    private static readonly Counter<long> DeadLetterTotalCounter =
        Meter.CreateCounter<long>("cena.dead_letter_total", description: "Total dead letters received");
    private static readonly Histogram<int> PoisonMessagesGauge =
        Meter.CreateHistogram<int>("cena.poison_messages", description: "Current quarantined poison message type count");

    public DeadLetterWatcher(ILogger<DeadLetterWatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // ACTOR INTERFACE (for query support when spawned as an actor)
    // =========================================================================

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started          => OnStarted(context),
            Stopping         => OnStopping(context),
            DeadLetterEvent e => HandleDeadLetter(e),
            GetDeadLetterStats => HandleGetStats(context),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted(IContext context)
    {
        _logger.LogInformation("DeadLetterWatcher actor started. Subscribing to DeadLetterEvent stream...");
        Subscribe(context.System);
        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        _logger.LogInformation("DeadLetterWatcher actor stopping. Unsubscribing...");
        Unsubscribe(context.System);
        return Task.CompletedTask;
    }

    // =========================================================================
    // SUBSCRIPTION MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Subscribe to the DeadLetterEvent stream on the given ActorSystem.
    /// Can be called standalone (without spawning as an actor) for background monitoring.
    /// </summary>
    public void Subscribe(ActorSystem system)
    {
        if (_subscription != null)
        {
            _logger.LogDebug("Already subscribed to dead letter stream.");
            return;
        }

        _subscription = system.EventStream.Subscribe<DeadLetterEvent>(OnDeadLetterEvent);
        _logger.LogInformation("DeadLetterWatcher subscribed to EventStream<DeadLetterEvent>.");
    }

    /// <summary>
    /// Unsubscribe from the dead letter stream.
    /// </summary>
    public void Unsubscribe(ActorSystem system)
    {
        if (_subscription != null)
        {
            system.EventStream.Unsubscribe(_subscription);
            _subscription = null;
            _logger.LogInformation("DeadLetterWatcher unsubscribed from EventStream.");
        }
    }

    // =========================================================================
    // DEAD LETTER HANDLING
    // =========================================================================

    private void OnDeadLetterEvent(DeadLetterEvent evt)
    {
        // Fire-and-forget processing on the EventStream callback thread
        ProcessDeadLetter(evt);
    }

    private Task HandleDeadLetter(DeadLetterEvent evt)
    {
        ProcessDeadLetter(evt);
        return Task.CompletedTask;
    }

    private void ProcessDeadLetter(DeadLetterEvent evt)
    {
        // Extract message type name
        string messageType = evt.Message?.GetType().Name ?? "Unknown";

        // Increment total counter
        Interlocked.Increment(ref _totalDeadLetters);
        DeadLetterTotalCounter.Add(1, new KeyValuePair<string, object?>("message_type", messageType));

        // Increment per-type counter
        _countsByType.AddOrUpdate(messageType, 1, (_, count) => count + 1);

        // Track timestamp for rate calculation
        long ticksNow = Stopwatch.GetTimestamp();
        _recentTimestamps.Enqueue(ticksNow);
        PruneOldTimestamps(ticksNow);

        // ── Poison message detection ──
        int consecutiveFails = _consecutiveFailsByType.AddOrUpdate(
            messageType, 1, (_, count) => count + 1);

        if (consecutiveFails >= PoisonThreshold && !_quarantinedTypes.ContainsKey(messageType))
        {
            _quarantinedTypes[messageType] = true;
            PoisonMessagesGauge.Record(_quarantinedTypes.Count);

            _logger.LogWarning(
                "POISON MESSAGE DETECTED: type={MessageType} has failed {Count} times. " +
                "Quarantining. Target={Target}, Sender={Sender}",
                messageType, consecutiveFails,
                evt.Pid?.ToString() ?? "none",
                evt.Sender?.ToString() ?? "none");
        }
        else
        {
            _logger.LogDebug(
                "Dead letter: type={MessageType}, target={Target}, sender={Sender}, " +
                "total={Total}, typeCount={TypeCount}",
                messageType,
                evt.Pid?.ToString() ?? "none",
                evt.Sender?.ToString() ?? "none",
                Interlocked.Read(ref _totalDeadLetters),
                _countsByType.GetValueOrDefault(messageType, 0));
        }

        // ── Rate alerting ──
        double currentRate = ComputeRatePerMinute(ticksNow);
        if (currentRate > AlertRatePerMinute)
        {
            _logger.LogWarning(
                "DEAD LETTER RATE EXCEEDED: {Rate:F1}/min (threshold={Threshold}/min). " +
                "Total={Total}, quarantined={Quarantined}",
                currentRate, AlertRatePerMinute,
                Interlocked.Read(ref _totalDeadLetters),
                _quarantinedTypes.Count);
        }
    }

    // =========================================================================
    // RATE TRACKING
    // =========================================================================

    private double ComputeRatePerMinute(long currentTicks)
    {
        int count = _recentTimestamps.Count;
        if (count == 0) return 0.0;

        // Rate = count within the window / window duration in minutes
        // Window is always 60 seconds
        return count * 60.0 / RateWindowSeconds;
    }

    private void PruneOldTimestamps(long currentTicks)
    {
        long windowTicks = (long)(RateWindowSeconds * Stopwatch.Frequency);
        long cutoff = currentTicks - windowTicks;

        // Remove timestamps older than the window
        while (_recentTimestamps.TryPeek(out long oldest))
        {
            if (oldest < cutoff)
            {
                _recentTimestamps.TryDequeue(out _);
            }
            else
            {
                break;
            }
        }
    }

    // =========================================================================
    // STATS QUERY
    // =========================================================================

    private Task HandleGetStats(IContext context)
    {
        long now = Stopwatch.GetTimestamp();
        PruneOldTimestamps(now);

        context.Respond(new DeadLetterStatsResponse(
            TotalDeadLetters: Interlocked.Read(ref _totalDeadLetters),
            CountsByType: new Dictionary<string, long>(_countsByType),
            QuarantinedTypes: _quarantinedTypes.Keys.ToList(),
            CurrentRatePerMinute: ComputeRatePerMinute(now)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reset the consecutive fail counter for a message type.
    /// Called when a previously-failing type succeeds (manual intervention).
    /// </summary>
    public void ResetConsecutiveFails(string messageType)
    {
        _consecutiveFailsByType.TryRemove(messageType, out _);
        _quarantinedTypes.TryRemove(messageType, out _);
        PoisonMessagesGauge.Record(_quarantinedTypes.Count);

        _logger.LogInformation(
            "DeadLetterWatcher: reset consecutive fails for type={MessageType}. " +
            "Removed from quarantine.",
            messageType);
    }

    // =========================================================================
    // DISPOSE
    // =========================================================================

    public void Dispose()
    {
        // Subscription cleanup is handled by Unsubscribe + actor stopping.
        // This is a safety net for non-actor usage patterns.
        _subscription = null;
    }
}
