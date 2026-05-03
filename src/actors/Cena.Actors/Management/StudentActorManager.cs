// =============================================================================
// Cena Platform -- StudentActorManager (Singleton, Actor Pool)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Singleton managing the pool of virtual StudentActors.
// Enforces: max 10,000 concurrent actors, back-pressure queue (depth > 1000),
// rate limiter at 200 activations/second (leaky bucket), DrainAll for shutdown.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;

namespace Cena.Actors.Management;

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>Request activation of a StudentActor by student ID.</summary>
public sealed record ActivateStudent(string StudentId, string CorrelationId = "");

/// <summary>Notification that a StudentActor has been passivated/deactivated.</summary>
public sealed record StudentDeactivated(string StudentId);

/// <summary>Stop accepting new activations (graceful shutdown phase 1).</summary>
public sealed record StopNewActivations;

/// <summary>Resume accepting new activations.</summary>
public sealed record ResumeActivations;

/// <summary>Drain all active actors (graceful shutdown phase 2).</summary>
public sealed record DrainAll(TimeSpan Timeout);

/// <summary>Query current manager metrics.</summary>
public sealed record GetManagerMetrics;

/// <summary>Internal tick message for non-blocking drain polling.</summary>
internal sealed record DrainCheckTick;

// ── Responses ──

public sealed record ActivateStudentResponse(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record DrainAllResponse(
    int DrainedCount,
    int RemainingCount,
    TimeSpan Elapsed);

public sealed record ManagerMetricsResponse(
    int ActiveCount,
    int QueueDepth,
    double ActivationsPerSecond,
    bool AcceptingNewActivations,
    int MaxConcurrentActors);

// =============================================================================
// LEAKY BUCKET RATE LIMITER
// =============================================================================

/// <summary>
/// Thread-safe leaky bucket rate limiter. Allows up to maxRate tokens/second.
/// Uses a monotonic time source for consistent behavior under clock skew.
/// </summary>
internal sealed class LeakyBucketRateLimiter
{
    private readonly double _maxRate;
    private readonly double _bucketCapacity;
    private double _currentLevel;
    private long _lastDrainTicks;
    private readonly object _lock = new();

    public LeakyBucketRateLimiter(double maxRatePerSecond, double burstCapacity)
    {
        _maxRate = maxRatePerSecond;
        _bucketCapacity = burstCapacity;
        _currentLevel = 0;
        _lastDrainTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Attempt to consume one token. Returns true if allowed, false if rate exceeded.
    /// </summary>
    public bool TryConsume()
    {
        lock (_lock)
        {
            Drain();

            if (_currentLevel >= _bucketCapacity)
                return false;

            _currentLevel += 1.0;
            return true;
        }
    }

    public double CurrentRate
    {
        get
        {
            lock (_lock)
            {
                Drain();
                return _currentLevel;
            }
        }
    }

    private void Drain()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = (now - _lastDrainTicks) / (double)Stopwatch.Frequency;
        _lastDrainTicks = now;

        double drained = elapsedSeconds * _maxRate;
        _currentLevel = Math.Max(0, _currentLevel - drained);
    }
}

// =============================================================================
// ACTOR
// =============================================================================

/// <summary>
/// Singleton actor managing the pool of virtual StudentActors.
/// Enforces concurrency limits, back-pressure, and rate limiting.
/// </summary>
public sealed class StudentActorManager : IActor
{
    private readonly ILogger<StudentActorManager> _logger;

    // ── Configuration ──
    private const int MaxConcurrentActors = 10_000;
    private const int MaxQueueDepth = 1_000;
    private const double MaxActivationsPerSecond = 200.0;

    // ── State ──
    // Actor mailbox guarantees sequential processing — no need for concurrent collections
    private readonly Dictionary<string, DateTimeOffset> _activeActors = new();
    private readonly Queue<PendingActivation> _backPressureQueue = new();
    private readonly LeakyBucketRateLimiter _rateLimiter = new(MaxActivationsPerSecond, MaxActivationsPerSecond);
    private bool _acceptingNew = true;
    private int _queueDepth;

    // ── Drain state ──
    private PID? _drainRequester;
    private Stopwatch? _drainStopwatch;
    private TimeSpan _drainTimeout;
    private int _drainInitialCount;
    private CancellationTokenSource? _drainTimerCts;

    // ── Telemetry (ACT-031: instance-based via IMeterFactory) ──
    private readonly Counter<long> _activationCounter;
    private readonly Counter<long> _rejectionCounter;
    private readonly Counter<long> _rateLimitCounter;
    private readonly Histogram<int> _activeGauge;
    private readonly Histogram<int> _queueGauge;

    public StudentActorManager(ILogger<StudentActorManager> logger, IMeterFactory meterFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var meter = meterFactory.Create("Cena.Actors.StudentActorManager", "1.0.0");
        _activationCounter = meter.CreateCounter<long>("cena.manager.activations_total");
        _rejectionCounter = meter.CreateCounter<long>("cena.manager.rejections_total");
        _rateLimitCounter = meter.CreateCounter<long>("cena.manager.rate_limited_total");
        _activeGauge = meter.CreateHistogram<int>("cena.manager.active_actors");
        _queueGauge = meter.CreateHistogram<int>("cena.manager.queue_depth");
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started                 => OnStarted(),
            Stopping                => OnStopping(),
            ActivateStudent cmd     => HandleActivate(context, cmd),
            StudentDeactivated cmd  => HandleDeactivated(context, cmd),
            StopNewActivations      => HandleStopNew(context),
            ResumeActivations       => HandleResume(context),
            DrainAll cmd            => HandleDrainAll(context, cmd),
            DrainCheckTick          => HandleDrainCheckTick(context),
            GetManagerMetrics       => HandleGetMetrics(context),
            _ => Task.CompletedTask
        };
    }

    private Task OnStopping()
    {
        CancelDrainTimer();
        return Task.CompletedTask;
    }

    private Task OnStarted()
    {
        _logger.LogInformation(
            "StudentActorManager started. Max={Max}, QueueLimit={QueueMax}, Rate={Rate}/s",
            MaxConcurrentActors, MaxQueueDepth, MaxActivationsPerSecond);
        return Task.CompletedTask;
    }

    // ── Activate ──

    private Task HandleActivate(IContext context, ActivateStudent cmd)
    {
        // Check if not accepting new activations (shutdown mode)
        if (!_acceptingNew)
        {
            context.Respond(new ActivateStudentResponse(
                false, "SHUTDOWN_IN_PROGRESS", "Not accepting new activations during shutdown."));
            _rejectionCounter.Add(1, new KeyValuePair<string, object?>("reason", "shutdown"));
            return Task.CompletedTask;
        }

        // Check if student is already active (idempotent)
        if (_activeActors.ContainsKey(cmd.StudentId))
        {
            context.Respond(new ActivateStudentResponse(true));
            return Task.CompletedTask;
        }

        // Check concurrency limit
        if (_activeActors.Count >= MaxConcurrentActors)
        {
            // Try to enqueue for back-pressure
            if (_queueDepth >= MaxQueueDepth)
            {
                context.Respond(new ActivateStudentResponse(
                    false, "QUEUE_FULL",
                    $"Back-pressure queue full ({MaxQueueDepth}). Cannot accept new activations."));
                _rejectionCounter.Add(1, new KeyValuePair<string, object?>("reason", "queue_full"));
                return Task.CompletedTask;
            }

            _backPressureQueue.Enqueue(new PendingActivation(cmd.StudentId, cmd.CorrelationId, context.Sender!));
            _queueDepth++;
            _queueGauge.Record(_queueDepth);

            _logger.LogDebug(
                "StudentActor activation queued for {StudentId}. QueueDepth={Depth}",
                cmd.StudentId, _queueDepth);

            context.Respond(new ActivateStudentResponse(
                false, "QUEUED", $"Activation queued. Queue depth: {_queueDepth}"));
            return Task.CompletedTask;
        }

        // Check rate limit
        if (!_rateLimiter.TryConsume())
        {
            // Enqueue instead of rejecting hard
            if (_queueDepth < MaxQueueDepth)
            {
                _backPressureQueue.Enqueue(new PendingActivation(cmd.StudentId, cmd.CorrelationId, context.Sender!));
                _queueDepth++;
                _queueGauge.Record(_queueDepth);

                context.Respond(new ActivateStudentResponse(
                    false, "RATE_LIMITED", "Activation rate exceeded. Queued for processing."));
                _rateLimitCounter.Add(1);
            }
            else
            {
                context.Respond(new ActivateStudentResponse(
                    false, "RATE_LIMITED_QUEUE_FULL", "Rate limit exceeded and queue full."));
                _rejectionCounter.Add(1, new KeyValuePair<string, object?>("reason", "rate_queue_full"));
            }
            return Task.CompletedTask;
        }

        // Activate
        _activeActors[cmd.StudentId] = DateTimeOffset.UtcNow;
        _activationCounter.Add(1);
        _activeGauge.Record(_activeActors.Count);

        _logger.LogDebug(
            "StudentActor activated: {StudentId}. Active={Count}",
            cmd.StudentId, _activeActors.Count);

        context.Respond(new ActivateStudentResponse(true));
        return Task.CompletedTask;
    }

    // ── Deactivated ──

    private Task HandleDeactivated(IContext context, StudentDeactivated cmd)
    {
        _activeActors.Remove(cmd.StudentId);
        _activeGauge.Record(_activeActors.Count);

        // Process queue: activate next pending if available
        ProcessQueue(context);

        // If a drain is in progress, check completion immediately
        // (avoids waiting for the next tick when actors deactivate quickly)
        if (_drainRequester is not null)
            CheckDrainComplete(context);

        return Task.CompletedTask;
    }

    // ── Queue Processing ──

    private void ProcessQueue(IContext context)
    {
        while (_backPressureQueue.Count > 0)
        {
            if (_activeActors.Count >= MaxConcurrentActors)
                break;

            if (!_rateLimiter.TryConsume())
                break;

            var pending = _backPressureQueue.Dequeue();
            {
                _queueDepth--;
                _queueGauge.Record(_queueDepth);

                if (!_activeActors.ContainsKey(pending.StudentId))
                {
                    _activeActors[pending.StudentId] = DateTimeOffset.UtcNow;
                    _activationCounter.Add(1);
                    _activeGauge.Record(_activeActors.Count);

                    // Notify the original requester
                    context.Send(pending.Requester, new ActivateStudentResponse(true));
                }
            }
        }
    }

    // ── Stop/Resume ──

    private Task HandleStopNew(IContext context)
    {
        _acceptingNew = false;
        _logger.LogInformation(
            "StudentActorManager: stopped accepting new activations. Active={Count}",
            _activeActors.Count);
        return Task.CompletedTask;
    }

    private Task HandleResume(IContext context)
    {
        _acceptingNew = true;
        _logger.LogInformation("StudentActorManager: resumed accepting new activations.");
        ProcessQueue(context);
        return Task.CompletedTask;
    }

    // ── Drain All (non-blocking) ──

    private Task HandleDrainAll(IContext context, DrainAll cmd)
    {
        _acceptingNew = false;
        _drainRequester = context.Sender;
        _drainTimeout = cmd.Timeout;
        _drainInitialCount = _activeActors.Count;
        _drainStopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Draining all {Count} active StudentActors. Timeout={Timeout}s",
            _drainInitialCount, cmd.Timeout.TotalSeconds);

        // Clear the queue first
        _queueDepth -= _backPressureQueue.Count;
        _backPressureQueue.Clear();
        _queueGauge.Record(0);

        // Check immediately -- might already be empty
        CheckDrainComplete(context);

        return Task.CompletedTask;
    }

    private Task HandleDrainCheckTick(IContext context)
    {
        // Only process if a drain is actually in progress
        if (_drainRequester is not null)
            CheckDrainComplete(context);

        return Task.CompletedTask;
    }

    private void CheckDrainComplete(IContext context)
    {
        bool drained = _activeActors.Count == 0;
        bool timedOut = _drainStopwatch is not null && _drainStopwatch.Elapsed >= _drainTimeout;

        if (drained || timedOut)
        {
            // Drain finished (success or timeout)
            var elapsed = _drainStopwatch?.Elapsed ?? TimeSpan.Zero;
            _drainStopwatch?.Stop();
            int remaining = _activeActors.Count;
            int drainedCount = _drainInitialCount - remaining;

            if (timedOut && remaining > 0)
            {
                _logger.LogWarning(
                    "Drain timeout reached. {Remaining} actors still active.",
                    remaining);
            }

            _logger.LogInformation(
                "Drain complete. Drained={Drained}, Remaining={Remaining}, Elapsed={Elapsed}ms",
                drainedCount, remaining, elapsed.TotalMilliseconds);

            // Respond to the original requester
            if (_drainRequester is not null)
            {
                context.Send(_drainRequester, new DrainAllResponse(drainedCount, remaining, elapsed));
            }

            // Clean up drain state
            CancelDrainTimer();
            _drainRequester = null;
            _drainStopwatch = null;
        }
        else
        {
            // Not done yet -- schedule next check, yielding the mailbox so
            // StudentDeactivated messages can be processed in between.
            ScheduleDrainCheck(context);
        }
    }

    private void ScheduleDrainCheck(IContext context)
    {
        CancelDrainTimer();
        _drainTimerCts = new CancellationTokenSource();
        var self = context.Self;
        var system = context.System;
        var token = _drainTimerCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                if (!token.IsCancellationRequested)
                    system.Root.Send(self, new DrainCheckTick());
            }
            catch (OperationCanceledException)
            {
                // Timer was cancelled -- expected during cleanup
            }
        }, token);
    }

    private void CancelDrainTimer()
    {
        if (_drainTimerCts is not null)
        {
            _drainTimerCts.Cancel();
            _drainTimerCts.Dispose();
            _drainTimerCts = null;
        }
    }

    // ── Metrics ──

    private Task HandleGetMetrics(IContext context)
    {
        context.Respond(new ManagerMetricsResponse(
            ActiveCount: _activeActors.Count,
            QueueDepth: _queueDepth,
            ActivationsPerSecond: _rateLimiter.CurrentRate,
            AcceptingNewActivations: _acceptingNew,
            MaxConcurrentActors: MaxConcurrentActors));
        return Task.CompletedTask;
    }
}

// ── Internal ──

internal sealed record PendingActivation(string StudentId, string CorrelationId, PID Requester);
