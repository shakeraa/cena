// =============================================================================
// Cena Platform -- LlmCircuitBreakerActor (Per-Model Circuit Breaker)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Per-model circuit breaker: Closed -> Open -> HalfOpen.
// Configurable maxFailures and openDuration per LLM model.
// Default configs: Kimi: 5/60s, Sonnet: 3/90s, Opus: 2/120s.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Gateway;

// =============================================================================
// CIRCUIT BREAKER STATE
// =============================================================================

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

// =============================================================================
// CONFIGURATION
// =============================================================================

/// <summary>
/// Configuration for a single LLM model's circuit breaker.
/// </summary>
public sealed record CircuitBreakerConfig(
    string ModelName,
    int MaxFailures,
    TimeSpan OpenDuration,
    int HalfOpenSuccessesRequired = 3)
{
    /// <summary>Pre-configured defaults for known LLM models.</summary>
    public static CircuitBreakerConfig Kimi => new("kimi", 5, TimeSpan.FromSeconds(60));
    public static CircuitBreakerConfig Sonnet => new("sonnet", 3, TimeSpan.FromSeconds(90));
    public static CircuitBreakerConfig Opus => new("opus", 2, TimeSpan.FromSeconds(120));

    public static CircuitBreakerConfig ForModel(string modelName)
    {
        return modelName.ToLowerInvariant() switch
        {
            "kimi"   => Kimi,
            "sonnet" => Sonnet,
            "opus"   => Opus,
            _        => new CircuitBreakerConfig(modelName, 3, TimeSpan.FromSeconds(90))
        };
    }
}

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>Request permission to call the LLM. Returns AllowRequest or RejectRequest.</summary>
public sealed record RequestPermission(string RequestId);

/// <summary>Report a successful LLM call.</summary>
public sealed record ReportSuccess(string RequestId);

/// <summary>Report a failed LLM call.</summary>
public sealed record ReportFailure(string RequestId, string Reason);

/// <summary>Query circuit breaker status.</summary>
public sealed record GetCircuitStatus;

/// <summary>Force circuit breaker reset (admin operation).</summary>
public sealed record ForceReset;

// ── Responses ──

public sealed record AllowRequest(string RequestId);
public sealed record RejectRequest(string RequestId, string Reason, TimeSpan RetryAfter);

public sealed record CircuitStatusResponse(
    string ModelName,
    CircuitState State,
    int FailureCount,
    int HalfOpenSuccessCount,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? EstimatedCloseAt,
    long TotalCircuitOpened,
    long TotalRequestsRejected);

// ── Internal timer ──
internal sealed record CircuitTimerTick;

// =============================================================================
// ACTOR
// =============================================================================

/// <summary>
/// Per-model circuit breaker actor. Manages state transitions:
/// Closed (normal) -> Open (tripped after maxFailures) -> HalfOpen (probe).
/// In HalfOpen, allows 1 request at a time. 3 consecutive successes -> Closed.
/// </summary>
public sealed class LlmCircuitBreakerActor : IActor
{
    private readonly CircuitBreakerConfig _config;
    private readonly ILogger<LlmCircuitBreakerActor> _logger;

    // ── State ──
    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private int _halfOpenSuccessCount;
    private DateTimeOffset _openedAt;
    private bool _halfOpenProbeInFlight;
    private CancellationTokenSource? _timerCts;

    // ── Metrics ──
    private static readonly Meter Meter = new("Cena.Actors.LlmCircuitBreaker", "1.0.0");
    private static readonly Counter<long> CircuitOpenedCounter =
        Meter.CreateCounter<long>("cena.llm.circuit_opened_total", description: "Times circuit breaker opened");
    private static readonly Counter<long> RequestsRejectedCounter =
        Meter.CreateCounter<long>("cena.llm.requests_rejected_total", description: "Requests rejected by open circuit");

    private long _totalCircuitOpened;
    private long _totalRequestsRejected;

    public LlmCircuitBreakerActor(CircuitBreakerConfig config, ILogger<LlmCircuitBreakerActor> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started             => OnStarted(),
            Stopping            => OnStopping(),
            RequestPermission q => HandleRequestPermission(context, q),
            ReportSuccess cmd   => HandleReportSuccess(context, cmd),
            ReportFailure cmd   => HandleReportFailure(context, cmd),
            GetCircuitStatus    => HandleGetStatus(context),
            ForceReset          => HandleForceReset(context),
            CircuitTimerTick    => HandleTimerTick(context),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted()
    {
        _logger.LogInformation(
            "LlmCircuitBreakerActor started for model {Model}. " +
            "MaxFailures={MaxFailures}, OpenDuration={Duration}s, HalfOpenSuccesses={Successes}",
            _config.ModelName, _config.MaxFailures,
            _config.OpenDuration.TotalSeconds, _config.HalfOpenSuccessesRequired);
        return Task.CompletedTask;
    }

    private Task OnStopping()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        return Task.CompletedTask;
    }

    // ── Request Permission ──

    private Task HandleRequestPermission(IContext context, RequestPermission q)
    {
        switch (_state)
        {
            case CircuitState.Closed:
                context.Respond(new AllowRequest(q.RequestId));
                break;

            case CircuitState.Open:
                _totalRequestsRejected++;
                RequestsRejectedCounter.Add(1,
                    new KeyValuePair<string, object?>("model", _config.ModelName));

                var retryAfter = _config.OpenDuration - (DateTimeOffset.UtcNow - _openedAt);
                if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;

                context.Respond(new RejectRequest(
                    q.RequestId,
                    $"Circuit breaker OPEN for model {_config.ModelName}. Retry after {retryAfter.TotalSeconds:F0}s.",
                    retryAfter));
                break;

            case CircuitState.HalfOpen:
                if (_halfOpenProbeInFlight)
                {
                    // Only 1 probe at a time in HalfOpen
                    _totalRequestsRejected++;
                    RequestsRejectedCounter.Add(1,
                        new KeyValuePair<string, object?>("model", _config.ModelName));

                    context.Respond(new RejectRequest(
                        q.RequestId,
                        $"Circuit breaker HALF_OPEN for model {_config.ModelName}. Probe in flight.",
                        TimeSpan.FromSeconds(5)));
                }
                else
                {
                    _halfOpenProbeInFlight = true;
                    context.Respond(new AllowRequest(q.RequestId));
                }
                break;
        }

        return Task.CompletedTask;
    }

    // ── Report Success ──

    private Task HandleReportSuccess(IContext context, ReportSuccess cmd)
    {
        switch (_state)
        {
            case CircuitState.Closed:
                // Reset failure count on success
                _failureCount = 0;
                break;

            case CircuitState.HalfOpen:
                _halfOpenProbeInFlight = false;
                _halfOpenSuccessCount++;

                _logger.LogDebug(
                    "HalfOpen probe success for {Model}: {Count}/{Required}",
                    _config.ModelName, _halfOpenSuccessCount, _config.HalfOpenSuccessesRequired);

                if (_halfOpenSuccessCount >= _config.HalfOpenSuccessesRequired)
                {
                    TransitionTo(CircuitState.Closed);
                    _logger.LogInformation(
                        "Circuit breaker CLOSED for model {Model} after {Count} successful probes",
                        _config.ModelName, _halfOpenSuccessCount);
                }
                break;

            case CircuitState.Open:
                // Ignore success reports while open (should not happen normally)
                break;
        }

        return Task.CompletedTask;
    }

    // ── Report Failure ──

    private Task HandleReportFailure(IContext context, ReportFailure cmd)
    {
        switch (_state)
        {
            case CircuitState.Closed:
                _failureCount++;
                _logger.LogWarning(
                    "LLM failure for model {Model}: {Reason}. Count={Count}/{Max}",
                    _config.ModelName, cmd.Reason, _failureCount, _config.MaxFailures);

                if (_failureCount >= _config.MaxFailures)
                {
                    TransitionTo(CircuitState.Open);
                    ScheduleTransitionToHalfOpen(context);

                    _logger.LogWarning(
                        "Circuit breaker OPENED for model {Model}. " +
                        "Failures={Count}, OpenDuration={Duration}s",
                        _config.ModelName, _failureCount, _config.OpenDuration.TotalSeconds);
                }
                break;

            case CircuitState.HalfOpen:
                _halfOpenProbeInFlight = false;
                // Probe failed -- go back to Open
                TransitionTo(CircuitState.Open);
                ScheduleTransitionToHalfOpen(context);

                _logger.LogWarning(
                    "HalfOpen probe failed for model {Model}: {Reason}. Returning to OPEN.",
                    _config.ModelName, cmd.Reason);
                break;

            case CircuitState.Open:
                // Already open, ignore
                break;
        }

        return Task.CompletedTask;
    }

    // ── Timer: Open -> HalfOpen transition ──

    private void ScheduleTransitionToHalfOpen(IContext context)
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = new CancellationTokenSource();

        var self = context.Self;
        var system = context.System;
        var token = _timerCts.Token;

        _ = Task.Delay(_config.OpenDuration, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                system.Root.Send(self, new CircuitTimerTick());
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private Task HandleTimerTick(IContext context)
    {
        if (_state == CircuitState.Open)
        {
            TransitionTo(CircuitState.HalfOpen);
            _logger.LogInformation(
                "Circuit breaker transitioned to HALF_OPEN for model {Model}. " +
                "Allowing probe requests.",
                _config.ModelName);
        }

        return Task.CompletedTask;
    }

    // ── State Transitions ──

    private void TransitionTo(CircuitState newState)
    {
        var oldState = _state;
        _state = newState;

        switch (newState)
        {
            case CircuitState.Open:
                _openedAt = DateTimeOffset.UtcNow;
                _halfOpenSuccessCount = 0;
                _halfOpenProbeInFlight = false;
                _totalCircuitOpened++;
                CircuitOpenedCounter.Add(1,
                    new KeyValuePair<string, object?>("model", _config.ModelName));
                break;

            case CircuitState.HalfOpen:
                _halfOpenSuccessCount = 0;
                _halfOpenProbeInFlight = false;
                break;

            case CircuitState.Closed:
                _failureCount = 0;
                _halfOpenSuccessCount = 0;
                _halfOpenProbeInFlight = false;
                _timerCts?.Cancel();
                break;
        }
    }

    // ── Status Query ──

    private Task HandleGetStatus(IContext context)
    {
        DateTimeOffset? estimatedClose = _state == CircuitState.Open
            ? _openedAt + _config.OpenDuration
            : null;

        context.Respond(new CircuitStatusResponse(
            ModelName: _config.ModelName,
            State: _state,
            FailureCount: _failureCount,
            HalfOpenSuccessCount: _halfOpenSuccessCount,
            OpenedAt: _state == CircuitState.Open ? _openedAt : null,
            EstimatedCloseAt: estimatedClose,
            TotalCircuitOpened: _totalCircuitOpened,
            TotalRequestsRejected: _totalRequestsRejected));

        return Task.CompletedTask;
    }

    // ── Force Reset (Admin) ──

    private Task HandleForceReset(IContext context)
    {
        _logger.LogWarning(
            "Force reset of circuit breaker for model {Model}. Previous state={State}",
            _config.ModelName, _state);

        TransitionTo(CircuitState.Closed);
        return Task.CompletedTask;
    }
}
