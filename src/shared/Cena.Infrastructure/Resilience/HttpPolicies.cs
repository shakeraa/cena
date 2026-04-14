// =============================================================================
// Cena Platform — HTTP Client Resilience Policies (RDY-012)
// Shared Polly policy factory: timeout + retry + circuit breaker + fallback
// for all external HTTP clients (LLM, OCR, Mathpix, embeddings, etc.).
// =============================================================================

using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace Cena.Infrastructure.Resilience;

/// <summary>
/// RDY-012: Configurable resilience settings per HTTP client.
/// </summary>
public sealed record HttpResilienceOptions
{
    /// <summary>Per-request timeout. Default 10s.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Number of retry attempts. Default 2.</summary>
    public int RetryCount { get; init; } = 2;

    /// <summary>Base delay for exponential backoff. Default 1s.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Failures before the circuit opens. Default 3.</summary>
    public int CircuitBreakerThreshold { get; init; } = 3;

    /// <summary>Sampling window for failure counting. Default 30s.</summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Time before circuit transitions from open to half-open. Default 60s.</summary>
    public TimeSpan CircuitBreakerDurationOfBreak { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// RDY-012: Factory for Polly policies applied to external HTTP clients.
/// Provides timeout → retry → circuit breaker layering with OTel metrics
/// and structured logging on circuit state transitions.
/// </summary>
public static class HttpPolicies
{
    private static readonly Meter s_meter = new("Cena.HttpCircuitBreaker", "1.0");

    // Observable gauges are registered per-client via the registry below.
    private static readonly Dictionary<string, int> s_circuitStates = new();

    static HttpPolicies()
    {
        s_meter.CreateObservableGauge(
            "cena.http.circuitbreaker.state",
            () =>
            {
                lock (s_circuitStates)
                {
                    return s_circuitStates
                        .Select(kv => new Measurement<int>(
                            kv.Value,
                            new KeyValuePair<string, object?>("client", kv.Key)));
                }
            },
            description: "HTTP circuit breaker state: 0=closed, 1=half-open, 2=open");
    }

    /// <summary>
    /// Returns the combined resilience policy (timeout + retry + circuit breaker)
    /// for an HTTP client identified by <paramref name="clientName"/>.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> CreateResiliencePolicy(
        string clientName,
        ILogger logger,
        HttpResilienceOptions? options = null)
    {
        var opts = options ?? new HttpResilienceOptions();

        SetState(clientName, 0); // closed

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
            opts.Timeout,
            TimeoutStrategy.Optimistic);

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                opts.RetryCount,
                attempt => TimeSpan.FromSeconds(opts.RetryBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        "[CIRCUIT_BREAKER] {ClientName} retry {Attempt} after {Delay}s — {Reason}",
                        clientName, attempt, delay.TotalSeconds,
                        outcome.Exception?.Message ?? $"HTTP {(int)(outcome.Result?.StatusCode ?? 0)}");
                });

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5,
                samplingDuration: opts.CircuitBreakerSamplingDuration,
                minimumThroughput: opts.CircuitBreakerThreshold,
                durationOfBreak: opts.CircuitBreakerDurationOfBreak,
                onBreak: (outcome, breakDuration) =>
                {
                    SetState(clientName, 2); // open
                    logger.LogError(
                        "[CIRCUIT_BREAKER] {ClientName} OPENED for {Duration}s — {Reason}",
                        clientName, breakDuration.TotalSeconds,
                        outcome.Exception?.Message ?? $"HTTP {(int)(outcome.Result?.StatusCode ?? 0)}");
                },
                onReset: () =>
                {
                    SetState(clientName, 0); // closed
                    logger.LogInformation(
                        "[CIRCUIT_BREAKER] {ClientName} CLOSED — requests flowing normally",
                        clientName);
                },
                onHalfOpen: () =>
                {
                    SetState(clientName, 1); // half-open
                    logger.LogInformation(
                        "[CIRCUIT_BREAKER] {ClientName} HALF-OPEN — testing single request",
                        clientName);
                });

        // Fallback wraps the whole stack: returns 503 with structured body instead of throwing.
        var fallbackPolicy = Policy<HttpResponseMessage>
            .Handle<BrokenCircuitException>()
            .Or<TimeoutRejectedException>()
            .FallbackAsync(
                fallbackAction: (_, _) =>
                {
                    logger.LogWarning(
                        "[CIRCUIT_BREAKER] {ClientName} fallback — returning 503",
                        clientName);

                    var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            $$"""{"error":"circuit_open","client":"{{clientName}}","message":"Service temporarily unavailable. Circuit breaker is open."}""",
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                    return Task.FromResult(response);
                },
                onFallbackAsync: (_, _) => Task.CompletedTask);

        // Order: fallback → retry → circuit breaker → timeout
        return fallbackPolicy
            .WrapAsync(retryPolicy)
            .WrapAsync(circuitBreakerPolicy)
            .WrapAsync(timeoutPolicy);
    }

    /// <summary>
    /// Gets the current circuit breaker state for a named client.
    /// 0 = closed, 1 = half-open, 2 = open, -1 = unknown.
    /// </summary>
    public static int GetState(string clientName)
    {
        lock (s_circuitStates)
        {
            return s_circuitStates.TryGetValue(clientName, out var state) ? state : -1;
        }
    }

    /// <summary>Returns all tracked client names and their circuit states.</summary>
    public static IReadOnlyDictionary<string, int> GetAllStates()
    {
        lock (s_circuitStates)
        {
            return new Dictionary<string, int>(s_circuitStates);
        }
    }

    private static void SetState(string clientName, int state)
    {
        lock (s_circuitStates)
        {
            s_circuitStates[clientName] = state;
        }
    }
}

/// <summary>
/// RDY-012: Extension methods to wire Polly resilience policies onto IHttpClientBuilder.
/// </summary>
public static class HttpClientResilienceExtensions
{
    /// <summary>
    /// Adds the standard Cena resilience policy (timeout + retry + CB + fallback) to an HTTP client.
    /// </summary>
    public static IHttpClientBuilder AddCenaResilience(
        this IHttpClientBuilder builder,
        string clientName,
        HttpResilienceOptions? options = null)
    {
        return builder.AddPolicyHandler((services, _) =>
        {
            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger($"Cena.HttpCircuitBreaker.{clientName}");
            return HttpPolicies.CreateResiliencePolicy(clientName, logger, options);
        });
    }
}
