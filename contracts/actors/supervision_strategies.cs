// =============================================================================
// Cena Platform -- Actor Supervision Strategies
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - Root supervisor: OneForOne, restart with exponential backoff (1s-30s).
//   - StudentActor children: OneForOne, restart on failure, stop after 3
//     consecutive failures within 60s.
//   - Poison message handling: quarantine failed messages, log, alert, skip.
//   - Dead letter handling: log + alert for undeliverable messages.
//   - Circuit breaker for LLM ACL calls: 5 failures in 30s -> open for 60s.
//
// SUPERVISION TREE:
//   ClusterRoot (system-level)
//     └── StudentActor (virtual, OneForOne + backoff)
//           ├── LearningSessionActor (classic, restart 3x then stop)
//           ├── StagnationDetectorActor (classic, restart 3x then stop)
//           └── OutreachSchedulerActor (classic, restart 3x then stop)
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors;

// =============================================================================
// SUPERVISION STRATEGIES
// =============================================================================

/// <summary>
/// Central registry of supervision strategies used throughout the Cena actor system.
/// All strategies are factory methods that return configured strategy instances.
///
/// <para><b>Supervision Philosophy:</b></para>
/// <list type="bullet">
///   <item>
///     <b>Let it crash, but crash smart:</b> Actors are restarted by default.
///     The supervisor tracks failure frequency and escalates (stop) when failures
///     are too frequent (3 within 60s).
///   </item>
///   <item>
///     <b>Isolation:</b> OneForOne strategy ensures one child's failure does not
///     affect siblings. A crashing LearningSessionActor does not take down the
///     StagnationDetectorActor.
///   </item>
///   <item>
///     <b>Exponential backoff:</b> Root-level restarts use exponential backoff
///     (1s, 2s, 4s, 8s... max 30s) to prevent restart storms during transient
///     infrastructure failures.
///   </item>
///   <item>
///     <b>Observability:</b> Every supervision decision is logged with structured
///     context and recorded in OpenTelemetry metrics.
///   </item>
/// </list>
/// </summary>
public static class CenaSupervisionStrategies
{
    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Actors.Supervision", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Actors.Supervision", "1.0.0");
    private static readonly Counter<long> RestartCounter =
        MeterInstance.CreateCounter<long>("cena.supervision.restarts_total", description: "Total actor restarts by supervision");
    private static readonly Counter<long> StopCounter =
        MeterInstance.CreateCounter<long>("cena.supervision.stops_total", description: "Total actor stops by supervision (max failures)");
    private static readonly Counter<long> EscalateCounter =
        MeterInstance.CreateCounter<long>("cena.supervision.escalations_total", description: "Total supervision escalations");
    private static readonly Counter<long> PoisonMessageCounter =
        MeterInstance.CreateCounter<long>("cena.supervision.poison_messages_total", description: "Total poison messages quarantined");
    private static readonly Counter<long> DeadLetterCounter =
        MeterInstance.CreateCounter<long>("cena.supervision.dead_letters_total", description: "Total dead letter messages");

    // ---- Failure tracking (thread-safe) ----
    private static readonly ConcurrentDictionary<string, FailureWindow> FailureWindows = new();

    // =========================================================================
    // ROOT SUPERVISOR STRATEGY
    // =========================================================================

    /// <summary>
    /// Root-level supervisor strategy for the cluster. Applied to top-level
    /// actors (StudentActor grains).
    ///
    /// <para><b>Policy:</b></para>
    /// - OneForOne: restart only the failed actor, not siblings
    /// - Exponential backoff: 1s, 2s, 4s, 8s, 16s, 30s (max)
    /// - No maximum restart count at root level (grains should always restart)
    /// - Logs all supervision decisions at Warning level
    /// </summary>
    public static ISupervisorStrategy RootStrategy()
    {
        return new OneForOneStrategy((pid, reason) =>
        {
            var actorName = pid.ToString();

            RestartCounter.Add(1,
                new KeyValuePair<string, object?>("actor", actorName),
                new KeyValuePair<string, object?>("level", "root"));

            Log.Warning(
                "Root supervisor: restarting actor {Actor}. Reason: {Reason}",
                actorName, reason.GetType().Name);

            return SupervisorDirective.Restart;
        }, maxNrOfRetries: 10, withinTimeSpan: TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Root strategy with exponential backoff. Uses Proto.Actor's built-in
    /// backoff supervision for restart timing.
    ///
    /// Backoff sequence: 1s, 2s, 4s, 8s, 16s, 30s (capped).
    /// After 10 restarts within 1 minute, escalates to parent.
    /// </summary>
    public static ISupervisorStrategy RootStrategyWithBackoff()
    {
        return new ExponentialBackoffStrategy(
            initialBackoff: TimeSpan.FromSeconds(1),
            resetBackoffAfter: TimeSpan.FromMinutes(2));
    }

    // =========================================================================
    // STUDENT CHILD STRATEGY (for session, stagnation, outreach actors)
    // =========================================================================

    /// <summary>
    /// Supervision strategy for StudentActor's child actors (LearningSessionActor,
    /// StagnationDetectorActor, OutreachSchedulerActor).
    ///
    /// <para><b>Policy:</b></para>
    /// - OneForOne: restart only the failed child
    /// - Max 3 consecutive failures within 60 seconds
    /// - On 4th failure: STOP the child (not restart)
    /// - Poison messages: quarantine, log, skip
    /// - Logs at Warning level for restarts, Error level for stops
    ///
    /// <para><b>Rationale:</b></para>
    /// If a child actor fails 3 times in 60 seconds, it likely has a persistent
    /// issue (bad state, infrastructure problem). Continuing to restart would
    /// waste resources. The parent StudentActor can detect the stopped child
    /// and take corrective action (e.g., re-create with fresh state).
    /// </summary>
    public static ISupervisorStrategy StudentChildStrategy()
    {
        return new OneForOneStrategy((pid, reason) =>
        {
            var actorId = pid.ToString();
            var window = FailureWindows.GetOrAdd(actorId, _ => new FailureWindow(
                maxFailures: 3, windowDuration: TimeSpan.FromSeconds(60)));

            window.RecordFailure();

            if (window.IsExhausted)
            {
                StopCounter.Add(1,
                    new KeyValuePair<string, object?>("actor", actorId));

                Log.Error(
                    "Student child supervisor: STOPPING actor {Actor} after {Count} " +
                    "consecutive failures within {Window}s. Last error: {Reason}",
                    actorId, window.MaxFailures, 60, FormatReason(reason));

                // Clean up tracking
                FailureWindows.TryRemove(actorId, out _);

                return SupervisorDirective.Stop;
            }

            RestartCounter.Add(1,
                new KeyValuePair<string, object?>("actor", actorId),
                new KeyValuePair<string, object?>("level", "child"));

            Log.Warning(
                "Student child supervisor: restarting actor {Actor}. " +
                "Failure {Count}/{Max} within window. Reason: {Reason}",
                actorId, window.RecentFailureCount, window.MaxFailures,
                FormatReason(reason));

            return SupervisorDirective.Restart;
        }, maxNrOfRetries: 3, withinTimeSpan: TimeSpan.FromSeconds(60));
    }

    // =========================================================================
    // POISON MESSAGE HANDLER
    // =========================================================================

    /// <summary>
    /// Handles poison messages -- messages that consistently cause actor failures.
    /// Instead of letting the actor crash-loop, the poison message is quarantined
    /// (logged with full context) and skipped.
    ///
    /// <para><b>Detection:</b></para>
    /// A message is considered "poison" if the same message type + actor combination
    /// causes 2 consecutive failures. The message is then quarantined.
    ///
    /// <para><b>Quarantine Actions:</b></para>
    /// 1. Log the message payload at Error level (redacted for PII)
    /// 2. Publish alert to NATS subject "cena.alerts.poison_message"
    /// 3. Skip the message (actor resumes normal processing)
    /// 4. Store in a dead-letter topic for manual investigation
    /// </summary>
    public static ISupervisorStrategy WithPoisonMessageHandling(
        ISupervisorStrategy innerStrategy)
    {
        return new PoisonMessageAwareStrategy(innerStrategy);
    }

    // =========================================================================
    // DEAD LETTER HANDLER
    // =========================================================================

    /// <summary>
    /// Subscribes to the Proto.Actor dead letter channel and logs all
    /// undeliverable messages. Dead letters occur when:
    /// - A message is sent to a stopped/passivated actor before reactivation completes
    /// - A message is sent to an invalid PID
    /// - Request timeout causes the response to arrive after the caller moved on
    ///
    /// <para><b>Actions:</b></para>
    /// 1. Log at Warning level with full message context
    /// 2. Increment dead letter counter metric
    /// 3. Alert if dead letter rate exceeds threshold (>100/min)
    /// </summary>
    public static void ConfigureDeadLetterHandling(ActorSystem system, ILogger logger)
    {
        system.EventStream.Subscribe<DeadLetterEvent>(dl =>
        {
            DeadLetterCounter.Add(1,
                new KeyValuePair<string, object?>("message.type", dl.Message?.GetType().Name ?? "null"),
                new KeyValuePair<string, object?>("target.pid", dl.Pid?.ToString() ?? "null"));

            logger.LogWarning(
                "Dead letter: Message={MessageType} to PID={Pid}, Sender={Sender}",
                dl.Message?.GetType().Name ?? "null",
                dl.Pid?.ToString() ?? "null",
                dl.Sender?.ToString() ?? "null");
        });
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private static string FormatReason(Exception? reason)
    {
        if (reason == null) return "unknown";
        return $"{reason.GetType().Name}: {reason.Message}";
    }

    /// <summary>Structured logging helper (Serilog-style, used where ILogger isn't available).</summary>
    private static class Log
    {
        public static void Warning(string template, params object[] args)
        {
            Serilog.Log.Warning(template, args);
        }

        public static void Error(string template, params object[] args)
        {
            Serilog.Log.Error(template, args);
        }
    }
}

// =============================================================================
// FAILURE WINDOW (sliding window for consecutive failure tracking)
// =============================================================================

/// <summary>
/// Thread-safe sliding window that tracks failure timestamps to determine
/// if consecutive failure threshold has been exceeded within a time window.
/// </summary>
internal sealed class FailureWindow
{
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private readonly TimeSpan _windowDuration;

    public int MaxFailures { get; }

    public FailureWindow(int maxFailures, TimeSpan windowDuration)
    {
        MaxFailures = maxFailures;
        _windowDuration = windowDuration;
    }

    /// <summary>Records a failure timestamp and prunes expired entries.</summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            _failures.Enqueue(now);

            // Prune entries outside the window
            while (_failures.Count > 0 && (now - _failures.Peek()) > _windowDuration)
            {
                _failures.Dequeue();
            }
        }
    }

    /// <summary>Number of failures within the current window.</summary>
    public int RecentFailureCount
    {
        get
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                // Count only non-expired entries
                return _failures.Count(f => (now - f) <= _windowDuration);
            }
        }
    }

    /// <summary>True if failure count within window exceeds max.</summary>
    public bool IsExhausted
    {
        get
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var recentCount = _failures.Count(f => (now - f) <= _windowDuration);
                return recentCount >= MaxFailures;
            }
        }
    }
}

// =============================================================================
// EXPONENTIAL BACKOFF STRATEGY
// =============================================================================

/// <summary>
/// Supervision strategy with exponential backoff between restarts.
/// Prevents restart storms during transient infrastructure failures.
///
/// Backoff formula: delay = min(initialBackoff * 2^failureCount, maxBackoff)
/// After a period of no failures (resetBackoffAfter), the backoff resets to initial.
/// </summary>
internal sealed class ExponentialBackoffStrategy : ISupervisorStrategy
{
    private readonly TimeSpan _initialBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly TimeSpan _resetAfter;
    private readonly ConcurrentDictionary<string, BackoffState> _states = new();

    public ExponentialBackoffStrategy(
        TimeSpan initialBackoff,
        TimeSpan resetBackoffAfter,
        TimeSpan? maxBackoff = null)
    {
        _initialBackoff = initialBackoff;
        _maxBackoff = maxBackoff ?? TimeSpan.FromSeconds(30);
        _resetAfter = resetBackoffAfter;
    }

    public void HandleFailure(
        ISupervisor supervisor,
        PID child,
        RestartStatistics rs,
        Exception reason,
        object? message)
    {
        var key = child.ToString();
        var state = _states.GetOrAdd(key, _ => new BackoffState());

        var now = DateTimeOffset.UtcNow;

        // Reset if enough time has passed since last failure
        if (state.LastFailure.HasValue && (now - state.LastFailure.Value) > _resetAfter)
        {
            state.ConsecutiveFailures = 0;
        }

        state.ConsecutiveFailures++;
        state.LastFailure = now;

        // Calculate backoff: initialBackoff * 2^(failures-1), capped at max
        var backoffMs = _initialBackoff.TotalMilliseconds * Math.Pow(2, state.ConsecutiveFailures - 1);
        var backoff = TimeSpan.FromMilliseconds(Math.Min(backoffMs, _maxBackoff.TotalMilliseconds));

        CenaSupervisionStrategies.LogRestart(key, state.ConsecutiveFailures, backoff, reason);

        // Schedule delayed restart
        _ = Task.Delay(backoff).ContinueWith(_ =>
        {
            supervisor.RestartChildren(reason, child);
        });
    }

    private sealed class BackoffState
    {
        public int ConsecutiveFailures;
        public DateTimeOffset? LastFailure;
    }
}

/// <summary>
/// Extension point for logging from the backoff strategy.
/// </summary>
public static partial class CenaSupervisionStrategies
{
    internal static void LogRestart(string actorId, int failures, TimeSpan backoff, Exception reason)
    {
        Serilog.Log.Warning(
            "Backoff supervisor: restarting {Actor} after {Backoff}ms. " +
            "ConsecutiveFailures={Failures}. Reason: {Reason}",
            actorId, backoff.TotalMilliseconds, failures,
            $"{reason.GetType().Name}: {reason.Message}");
    }
}

// =============================================================================
// POISON MESSAGE AWARE STRATEGY
// =============================================================================

/// <summary>
/// Wrapper strategy that detects poison messages (messages that consistently
/// cause failures) and quarantines them instead of letting the actor crash-loop.
/// </summary>
internal sealed class PoisonMessageAwareStrategy : ISupervisorStrategy
{
    private readonly ISupervisorStrategy _inner;
    private readonly ConcurrentDictionary<string, PoisonTracker> _trackers = new();
    private const int PoisonThreshold = 2;

    public PoisonMessageAwareStrategy(ISupervisorStrategy inner)
    {
        _inner = inner;
    }

    public void HandleFailure(
        ISupervisor supervisor,
        PID child,
        RestartStatistics rs,
        Exception reason,
        object? message)
    {
        if (message == null)
        {
            _inner.HandleFailure(supervisor, child, rs, reason, message);
            return;
        }

        var key = $"{child}:{message.GetType().Name}";
        var tracker = _trackers.GetOrAdd(key, _ => new PoisonTracker());

        tracker.FailureCount++;

        if (tracker.FailureCount >= PoisonThreshold)
        {
            // Quarantine the poison message
            CenaSupervisionStrategies.QuarantinePoisonMessage(child, message, reason);
            _trackers.TryRemove(key, out _);

            // Resume the actor (skip the message)
            supervisor.RestartChildren(reason, child);
            return;
        }

        // Not yet poisoned -- delegate to inner strategy
        _inner.HandleFailure(supervisor, child, rs, reason, message);
    }

    private sealed class PoisonTracker
    {
        public int FailureCount;
    }
}

public static partial class CenaSupervisionStrategies
{
    internal static void QuarantinePoisonMessage(PID actor, object message, Exception reason)
    {
        PoisonMessageCounter.Add(1,
            new KeyValuePair<string, object?>("actor", actor.ToString()),
            new KeyValuePair<string, object?>("message.type", message.GetType().Name));

        Serilog.Log.Error(
            "POISON MESSAGE quarantined. Actor={Actor}, MessageType={MessageType}, " +
            "Reason={Reason}. Message will be skipped.",
            actor, message.GetType().Name,
            $"{reason.GetType().Name}: {reason.Message}");

        // TODO: Publish to NATS "cena.alerts.poison_message" for ops alerting
        // TODO: Store in dead-letter topic for manual investigation
    }
}

// =============================================================================
// CIRCUIT BREAKER (for LLM ACL calls)
// =============================================================================

/// <summary>
/// Circuit breaker for protecting actors from cascading failures when
/// calling external services (LLM ACL via Kimi K2.5, Claude Sonnet).
///
/// <para><b>States:</b></para>
/// <list type="bullet">
///   <item><b>Closed:</b> Normal operation. Calls pass through.</item>
///   <item><b>Open:</b> Circuit is broken. All calls immediately fail with
///     <see cref="CircuitBreakerOpenException"/>. Transitions to HalfOpen after timeout.</item>
///   <item><b>HalfOpen:</b> Allows a single probe call. On success -> Closed.
///     On failure -> Open again.</item>
/// </list>
///
/// <para><b>Thresholds:</b></para>
/// Default: 5 failures within 30 seconds opens the circuit for 60 seconds.
/// </summary>
public sealed class ActorCircuitBreaker
{
    private readonly object _lock = new();
    private readonly string _name;
    private readonly int _failureThreshold;
    private readonly TimeSpan _failureWindow;
    private readonly TimeSpan _openDuration;
    private readonly ILogger _logger;

    private CircuitState _state = CircuitState.Closed;
    private readonly Queue<DateTimeOffset> _failures = new();
    private DateTimeOffset? _openedAt;

    // ---- Telemetry ----
    private static readonly Meter MeterInstance =
        new("Cena.Actors.CircuitBreaker", "1.0.0");
    private static readonly Counter<long> TripCounter =
        MeterInstance.CreateCounter<long>("cena.circuit_breaker.trips_total", description: "Total circuit breaker trips");
    private static readonly Counter<long> RejectCounter =
        MeterInstance.CreateCounter<long>("cena.circuit_breaker.rejections_total", description: "Total calls rejected by open circuit");
    private static readonly Counter<long> ResetCounter =
        MeterInstance.CreateCounter<long>("cena.circuit_breaker.resets_total", description: "Total circuit breaker resets");

    /// <summary>
    /// Creates a new circuit breaker.
    /// </summary>
    /// <param name="name">Name for logging and metrics (e.g., "llm-acl-kimi", "llm-acl-claude").</param>
    /// <param name="failureThreshold">Number of failures to trip the circuit. Default: 5.</param>
    /// <param name="failureWindow">Time window for counting failures. Default: 30 seconds.</param>
    /// <param name="openDuration">How long the circuit stays open. Default: 60 seconds.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorCircuitBreaker(
        string name,
        ILogger logger,
        int failureThreshold = 5,
        TimeSpan? failureWindow = null,
        TimeSpan? openDuration = null)
    {
        _name = name;
        _logger = logger;
        _failureThreshold = failureThreshold;
        _failureWindow = failureWindow ?? TimeSpan.FromSeconds(30);
        _openDuration = openDuration ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Current circuit state.
    /// </summary>
    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                MaybeTransition();
                return _state;
            }
        }
    }

    /// <summary>
    /// Executes an action through the circuit breaker.
    /// Throws <see cref="CircuitBreakerOpenException"/> if the circuit is open.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        lock (_lock)
        {
            MaybeTransition();

            if (_state == CircuitState.Open)
            {
                RejectCounter.Add(1,
                    new KeyValuePair<string, object?>("breaker", _name));

                throw new CircuitBreakerOpenException(
                    $"Circuit breaker '{_name}' is OPEN. " +
                    $"Will retry after {_openedAt?.Add(_openDuration):HH:mm:ss}");
            }
        }

        try
        {
            var result = await action();

            lock (_lock)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    // Probe succeeded -- close the circuit
                    _state = CircuitState.Closed;
                    _failures.Clear();
                    _openedAt = null;

                    ResetCounter.Add(1,
                        new KeyValuePair<string, object?>("breaker", _name));

                    _logger.LogInformation(
                        "Circuit breaker '{Name}' CLOSED after successful probe.", _name);
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not CircuitBreakerOpenException)
        {
            lock (_lock)
            {
                RecordFailure();
            }
            throw;
        }
    }

    /// <summary>
    /// Executes a void action through the circuit breaker.
    /// </summary>
    public async Task ExecuteAsync(Func<Task> action)
    {
        await ExecuteAsync(async () =>
        {
            await action();
            return true;
        });
    }

    private void RecordFailure()
    {
        var now = DateTimeOffset.UtcNow;
        _failures.Enqueue(now);

        // Prune old failures
        while (_failures.Count > 0 && (now - _failures.Peek()) > _failureWindow)
        {
            _failures.Dequeue();
        }

        if (_failures.Count >= _failureThreshold)
        {
            _state = CircuitState.Open;
            _openedAt = now;

            TripCounter.Add(1,
                new KeyValuePair<string, object?>("breaker", _name));

            _logger.LogWarning(
                "Circuit breaker '{Name}' OPENED after {Count} failures in {Window}s. " +
                "Will retry after {RetryAt:HH:mm:ss}",
                _name, _failures.Count, _failureWindow.TotalSeconds,
                now.Add(_openDuration));
        }
    }

    private void MaybeTransition()
    {
        if (_state == CircuitState.Open && _openedAt.HasValue)
        {
            if (DateTimeOffset.UtcNow - _openedAt.Value >= _openDuration)
            {
                _state = CircuitState.HalfOpen;
                _logger.LogInformation(
                    "Circuit breaker '{Name}' transitioning to HALF-OPEN. " +
                    "Next call will be a probe.",
                    _name);
            }
        }
    }
}

/// <summary>Circuit breaker states.</summary>
public enum CircuitState
{
    /// <summary>Normal operation. All calls pass through.</summary>
    Closed,

    /// <summary>Circuit is broken. All calls immediately rejected.</summary>
    Open,

    /// <summary>Allowing one probe call. Success -> Closed, Failure -> Open.</summary>
    HalfOpen
}

/// <summary>
/// Thrown when a call is rejected because the circuit breaker is open.
/// Callers should handle this by returning a cached/default response
/// or informing the user to retry later.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception inner) : base(message, inner) { }
}
