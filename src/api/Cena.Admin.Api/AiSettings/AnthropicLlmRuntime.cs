// =============================================================================
// Cena Platform — Anthropic LLM Runtime implementation.
// See IAnthropicLlmRuntime.cs for the rationale and contract.
//
// Single Singleton instance: AiGenerationService and OcrTextEnhancer (and any
// future Anthropic-backed admin feature) share one breaker, one client cache
// per (apiKey,) tuple, and the legacy per-service meter triple.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Default <see cref="IAnthropicLlmRuntime"/> implementation. Singleton-scoped:
/// the breaker, client cache, and meter counters must be shared across every
/// admin LLM call site so a flaky-model trip on question-generation also
/// gates OCR-enhance and so dashboards see one cohesive series per model.
/// </summary>
public sealed class AnthropicLlmRuntime : IAnthropicLlmRuntime
{
    private readonly ILogger<AnthropicLlmRuntime> _logger;

    // Anthropic SDK client cache — keyed on the plaintext API key. Rotating
    // the persisted cipher (and therefore the resolved plaintext) yields a
    // brand-new client; old clients are eligible for GC.
    private readonly ConcurrentDictionary<string, AnthropicClient> _clientsByKey =
        new(StringComparer.Ordinal);

    // Breaker state — mirrors the LlmCircuitBreakerActor thresholds the rest
    // of the platform uses (Sonnet: 3 failures within OpenDuration → open).
    // Locked under _cbLock; readers must hold the lock too.
    private int _failureCount;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private static readonly int MaxFailures = 3;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(90);
    private readonly object _cbLock = new();

    // Cost constants per million tokens (routing-config.yaml § claude_sonnet_4_6).
    // Local copies because every legacy meter call needs them; the canonical
    // per-feature cost still goes through ILlmCostMetric at the call site.
    private const double CostPerInputMTok = 3.00;
    private const double CostPerOutputMTok = 15.00;

    // Legacy per-service meters (Cena.Admin.LlmMetrics 1.0.0). Owned by the
    // runtime so meter factories don't build duplicate counters per consumer.
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _tokensTotal;
    private readonly Counter<double> _costUsd;

    /// <inheritdoc/>
    public JsonSerializerOptions JsonOpts { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AnthropicLlmRuntime(
        ILogger<AnthropicLlmRuntime> logger,
        IMeterFactory meterFactory)
    {
        _logger = logger;

        var meter = meterFactory.Create("Cena.Admin.LlmMetrics", "1.0.0");
        _requestDuration = meter.CreateHistogram<double>(
            "llm_request_duration_ms",
            unit: "ms",
            description: "LLM request duration in milliseconds");
        _tokensTotal = meter.CreateCounter<long>(
            "llm_tokens_total",
            description: "Total LLM tokens consumed");
        _costUsd = meter.CreateCounter<double>(
            "llm_cost_usd",
            unit: "USD",
            description: "LLM cost in USD");
    }

    /// <inheritdoc/>
    public AnthropicClient GetOrCreateClient(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("apiKey must be non-empty", nameof(apiKey));

        return _clientsByKey.GetOrAdd(apiKey, static key => new AnthropicClient(new ClientOptions
        {
            ApiKey = key,
            MaxRetries = 0, // Circuit breaker handles retries
        }));
    }

    /// <inheritdoc/>
    public void RequestCircuitPermission(string modelName)
    {
        lock (_cbLock)
        {
            if (!_circuitOpen) return;

            if (DateTimeOffset.UtcNow - _circuitOpenedAt >= OpenDuration)
            {
                _logger.LogInformation(
                    "Circuit breaker half-open for {Model}, allowing probe", modelName);
                _circuitOpen = false;
                _failureCount = 0;
                return;
            }

            var retryAfter = OpenDuration - (DateTimeOffset.UtcNow - _circuitOpenedAt);
            throw new CircuitOpenException(
                $"Circuit breaker OPEN for model {modelName}. Retry after {retryAfter.TotalSeconds:F0}s.");
        }
    }

    /// <inheritdoc/>
    public void RecordSuccess(string modelName)
    {
        lock (_cbLock)
        {
            _failureCount = 0;
            _circuitOpen = false;
        }
    }

    /// <inheritdoc/>
    public void RecordFailure(string modelName)
    {
        lock (_cbLock)
        {
            _failureCount++;
            _logger.LogWarning(
                "LLM failure for {Model}. Count={Count}/{Max}",
                modelName, _failureCount, MaxFailures);

            if (_failureCount >= MaxFailures)
            {
                _circuitOpen = true;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "Circuit breaker OPENED for {Model}. Failures={Count}, OpenDuration={Duration}s",
                    modelName, _failureCount, OpenDuration.TotalSeconds);
            }
        }
    }

    /// <inheritdoc/>
    public void EmitMetrics(string model, string taskType, long durationMs,
        long inputTokens, long outputTokens)
    {
        var modelTag = new KeyValuePair<string, object?>("model_id", model);
        var taskTag = new KeyValuePair<string, object?>("task_type", taskType);

        _requestDuration.Record(durationMs, modelTag, taskTag,
            new KeyValuePair<string, object?>("status", "success"));

        _tokensTotal.Add(inputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "input"));
        _tokensTotal.Add(outputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "output"));

        var cost = (inputTokens * CostPerInputMTok + outputTokens * CostPerOutputMTok) / 1_000_000.0;
        _costUsd.Add(cost, modelTag, taskTag);

        _logger.LogInformation(
            "LLM call completed: model={Model}, task={Task}, duration={DurationMs}ms, " +
            "input_tokens={InputTokens}, output_tokens={OutputTokens}, cost_usd={Cost:F6}",
            model, taskType, durationMs, inputTokens, outputTokens, cost);
    }
}
