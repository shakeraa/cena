// =============================================================================
// Cena Platform -- HealthAggregatorActor (RES-005)
// Layer: Infrastructure | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Singleton actor that polls all circuit breakers and infrastructure health.
// Computes a SystemHealthLevel and publishes SystemHealthChanged on transitions.
// Origin: Fortnite had no coordinated response when multiple systems failed.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Gateway;
using Cena.Actors.Management;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Infrastructure;

// =============================================================================
// HEALTH LEVEL
// =============================================================================

public enum SystemHealthLevel
{
    Healthy   = 0,
    Degraded  = 1,
    Critical  = 2,
    Emergency = 3
}

// =============================================================================
// MESSAGES
// =============================================================================

public sealed record GetSystemHealth;

public sealed record SystemHealthResponse(
    SystemHealthLevel Level,
    Dictionary<string, CircuitState> CircuitBreakers,
    int ActiveActors,
    int MaxActors,
    double PoolUtilizationPercent);

public sealed record SystemHealthChanged(
    SystemHealthLevel OldLevel,
    SystemHealthLevel NewLevel,
    DateTimeOffset Timestamp);

internal sealed record HealthPollTick;

// =============================================================================
// ACTOR
// =============================================================================

public sealed class HealthAggregatorActor : IActor
{
    private readonly ILogger<HealthAggregatorActor> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // ── CB PIDs to poll (set via SetCircuitBreakerPids message) ──
    private readonly Dictionary<string, PID> _circuitBreakerPids = new();
    private PID? _managerPid;

    // ── Cached state ──
    private SystemHealthLevel _currentLevel = SystemHealthLevel.Healthy;
    private readonly Dictionary<string, CircuitState> _cbStates = new();
    private int _activeActors;
    private int _maxActors = 10_000;
    private CancellationTokenSource? _timerCts;

    // ── Telemetry ──
    private readonly Counter<long> _healthTransitionCounter;

    public HealthAggregatorActor(ILogger<HealthAggregatorActor> logger, IMeterFactory meterFactory)
    {
        _logger = logger;
        var meter = meterFactory.Create("Cena.Actors.HealthAggregator", "1.0.0");
        _healthTransitionCounter = meter.CreateCounter<long>("cena.health.transitions_total",
            description: "System health level transitions");
    }

    /// <summary>
    /// Registers circuit breaker PIDs and the manager PID for polling.
    /// Send this after spawning the actor.
    /// </summary>
    public sealed record RegisterHealthSources(
        Dictionary<string, PID> CircuitBreakers,
        PID? ManagerPid);

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started                  => OnStarted(context),
            Stopping                 => OnStopping(),
            HealthPollTick           => HandlePollTick(context),
            GetSystemHealth          => HandleGetHealth(context),
            RegisterHealthSources r  => HandleRegister(r),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted(IContext context)
    {
        _logger.LogInformation("HealthAggregatorActor started. Polling every {Interval}s", PollInterval.TotalSeconds);
        ScheduleNextPoll(context);
        return Task.CompletedTask;
    }

    private Task OnStopping()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        return Task.CompletedTask;
    }

    private Task HandleRegister(RegisterHealthSources r)
    {
        foreach (var (name, pid) in r.CircuitBreakers)
            _circuitBreakerPids[name] = pid;
        _managerPid = r.ManagerPid;
        _logger.LogInformation("Registered {Count} circuit breakers + manager for health polling",
            _circuitBreakerPids.Count);
        return Task.CompletedTask;
    }

    private async Task HandlePollTick(IContext context)
    {
        // Poll all circuit breakers
        int openCount = 0;
        foreach (var (name, pid) in _circuitBreakerPids)
        {
            try
            {
                var status = await context.RequestAsync<CircuitStatusResponse>(
                    pid, new GetCircuitStatus(), TimeSpan.FromSeconds(2));
                _cbStates[name] = status.State;
                if (status.State == CircuitState.Open)
                    openCount++;
            }
            catch
            {
                _cbStates[name] = CircuitState.Open; // Unreachable = assume open
                openCount++;
            }
        }

        // Poll manager metrics
        double poolUtilization = 0;
        if (_managerPid != null)
        {
            try
            {
                var metrics = await context.RequestAsync<ManagerMetricsResponse>(
                    _managerPid, new GetManagerMetrics(), TimeSpan.FromSeconds(2));
                _activeActors = metrics.ActiveCount;
                _maxActors = metrics.MaxConcurrentActors;
                poolUtilization = _maxActors > 0
                    ? (double)_activeActors / _maxActors * 100.0
                    : 0;
            }
            catch
            {
                // Manager unreachable — keep last known values
            }
        }

        // Compute health level
        var newLevel = ComputeHealthLevel(openCount, poolUtilization);

        if (newLevel != _currentLevel)
        {
            var oldLevel = _currentLevel;
            _currentLevel = newLevel;

            _healthTransitionCounter.Add(1,
                new KeyValuePair<string, object?>("from", oldLevel.ToString()),
                new KeyValuePair<string, object?>("to", newLevel.ToString()));

            _logger.LogWarning(
                "System health changed: {Old} -> {New}. OpenCBs={OpenCBs}, PoolUtil={Pool:F1}%",
                oldLevel, newLevel, openCount, poolUtilization);
        }

        ScheduleNextPoll(context);
    }

    internal static SystemHealthLevel ComputeHealthLevel(int openCircuitBreakers, double poolUtilizationPercent)
    {
        if (openCircuitBreakers >= 3 || poolUtilizationPercent >= 95)
            return SystemHealthLevel.Emergency;
        if (openCircuitBreakers >= 2 || poolUtilizationPercent >= 90)
            return SystemHealthLevel.Critical;
        if (openCircuitBreakers >= 1 || poolUtilizationPercent >= 70)
            return SystemHealthLevel.Degraded;
        return SystemHealthLevel.Healthy;
    }

    private Task HandleGetHealth(IContext context)
    {
        context.Respond(new SystemHealthResponse(
            Level: _currentLevel,
            CircuitBreakers: new Dictionary<string, CircuitState>(_cbStates),
            ActiveActors: _activeActors,
            MaxActors: _maxActors,
            PoolUtilizationPercent: _maxActors > 0
                ? (double)_activeActors / _maxActors * 100.0
                : 0));
        return Task.CompletedTask;
    }

    private void ScheduleNextPoll(IContext context)
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = new CancellationTokenSource();

        var self = context.Self;
        var system = context.System;
        var token = _timerCts.Token;

        _ = Task.Delay(PollInterval, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                system.Root.Send(self, new HealthPollTick());
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }
}
