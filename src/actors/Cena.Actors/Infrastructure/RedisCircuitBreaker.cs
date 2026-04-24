// =============================================================================
// Cena Platform -- Redis Circuit Breaker (INF-019)
// Lightweight state machine: Closed → Open (after N failures) → HalfOpen → Closed
//
// When Open: Redis calls skip immediately (no timeout waiting).
// Protects explanation cache path from cascading into 10x LLM token costs.
// Protects messaging from hanging on dead Redis.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Infrastructure;

public interface IRedisCircuitBreaker
{
    /// <summary>True when Redis is considered available (Closed or HalfOpen state).</summary>
    bool IsAvailable { get; }

    /// <summary>Current state name for observability.</summary>
    string State { get; }

    /// <summary>Call after a successful Redis operation.</summary>
    void RecordSuccess();

    /// <summary>Call after a failed Redis operation.</summary>
    void RecordFailure();
}

public sealed class RedisCircuitBreaker : IRedisCircuitBreaker
{
    private enum CbState { Closed, Open, HalfOpen }

    private readonly ILogger<RedisCircuitBreaker> _logger;
    private readonly Gauge<int> _stateGauge;
    private readonly Counter<long> _tripCounter;

    // Configuration
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    // State (all access via Interlocked / volatile for lock-free thread safety)
    private volatile CbState _state = CbState.Closed;
    private int _failureCount;
    private DateTimeOffset _openedAt;
    private readonly object _lock = new();

    public RedisCircuitBreaker(
        ILogger<RedisCircuitBreaker> logger,
        IMeterFactory meterFactory,
        int failureThreshold = 3,
        int openDurationSeconds = 60)
    {
        _logger = logger;
        _failureThreshold = failureThreshold;
        _openDuration = TimeSpan.FromSeconds(openDurationSeconds);

        var meter = meterFactory.Create("Cena.Infrastructure.Redis", "1.0.0");
        _stateGauge = meter.CreateGauge<int>(
            "cena.redis.circuit_breaker_state",
            description: "Redis circuit breaker state: 0=Closed, 1=Open, 2=HalfOpen");
        _tripCounter = meter.CreateCounter<long>(
            "cena.redis.circuit_breaker_trips_total",
            description: "Number of times the Redis circuit breaker tripped open");

        _stateGauge.Record(0);
    }

    public bool IsAvailable
    {
        get
        {
            if (_state == CbState.Closed)
                return true;

            if (_state == CbState.Open)
            {
                // Check if open duration has elapsed → transition to HalfOpen
                if (DateTimeOffset.UtcNow - _openedAt >= _openDuration)
                {
                    lock (_lock)
                    {
                        if (_state == CbState.Open)
                        {
                            _state = CbState.HalfOpen;
                            _stateGauge.Record(2);
                            _logger.LogInformation(
                                "Redis circuit breaker transitioning to HalfOpen after {Duration}s",
                                _openDuration.TotalSeconds);
                        }
                    }

                    return true; // HalfOpen allows one probe
                }

                return false; // Still Open
            }

            // HalfOpen: allow the probe request
            return true;
        }
    }

    public string State => _state.ToString();

    public void RecordSuccess()
    {
        if (_state == CbState.Closed)
        {
            // Reset failure count on success in Closed state
            Interlocked.Exchange(ref _failureCount, 0);
            return;
        }

        // HalfOpen + success → close the circuit
        lock (_lock)
        {
            if (_state is CbState.HalfOpen or CbState.Open)
            {
                _state = CbState.Closed;
                Interlocked.Exchange(ref _failureCount, 0);
                _stateGauge.Record(0);
                _logger.LogInformation("Redis circuit breaker closed — Redis recovered");
            }
        }
    }

    public void RecordFailure()
    {
        var count = Interlocked.Increment(ref _failureCount);

        if (_state == CbState.HalfOpen)
        {
            // HalfOpen + failure → reopen
            lock (_lock)
            {
                if (_state == CbState.HalfOpen)
                {
                    _state = CbState.Open;
                    _openedAt = DateTimeOffset.UtcNow;
                    _stateGauge.Record(1);
                    _tripCounter.Add(1);
                    _logger.LogWarning(
                        "Redis circuit breaker re-opened from HalfOpen after probe failure");
                }
            }

            return;
        }

        if (count >= _failureThreshold && _state == CbState.Closed)
        {
            lock (_lock)
            {
                if (_state == CbState.Closed)
                {
                    _state = CbState.Open;
                    _openedAt = DateTimeOffset.UtcNow;
                    _stateGauge.Record(1);
                    _tripCounter.Add(1);
                    _logger.LogWarning(
                        "Redis circuit breaker opened after {Count} failures in Closed state",
                        count);
                }
            }
        }
    }
}
