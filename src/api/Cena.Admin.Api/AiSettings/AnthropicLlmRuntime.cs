// =============================================================================
// Cena Platform — Anthropic LLM Runtime implementation.
// See IAnthropicLlmRuntime.cs for the rationale and contract.
//
// Single Singleton instance: AiGenerationService and OcrTextEnhancer (and any
// future Anthropic-backed admin feature) share one breaker registry, one
// client cache per (apiKey,) tuple, and the legacy per-service meter triple.
//
// 2026-05-03 upgrades (claude-subagent-runtime-upgrades):
//   - Per-model circuit breaker: a Haiku trip no longer blocks Sonnet calls.
//   - Per-call pricing: EmitMetrics requires the caller to pass the actual
//     $/MTok rates so legacy llm_cost_usd is no longer Sonnet-priced for
//     Haiku callers (concept-extraction, ocr-text-enhance under Haiku, etc.)
//   - Trace-id propagation: breaker-open / breaker-trip log lines carry the
//     current trace_id so SIEM/Grafana stitching matches the per-feature
//     cost-counter trace_id label.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Default <see cref="IAnthropicLlmRuntime"/> implementation. Singleton-scoped:
/// the breaker registry, client cache, and meter counters must be shared
/// across every admin LLM call site so dashboards see one cohesive series
/// per model and the breaker reflects per-model health independently.
/// </summary>
public sealed class AnthropicLlmRuntime : IAnthropicLlmRuntime
{
    private readonly ILogger<AnthropicLlmRuntime> _logger;

    // Optional — IActivityPropagator is registered by the LLM-cost-metric
    // host bootstrap (AddLlmActivityPropagator) but not every host that uses
    // the runtime registers it (some test compositions do not). Nullable so
    // the runtime degrades to "no trace_id label" rather than crashing on
    // resolve. Production hosts (Cena.Admin.Api.Host) MUST register it; the
    // missing-registration path is logged at startup elsewhere.
    private readonly IActivityPropagator? _activityPropagator;

    // Anthropic SDK client cache — keyed on the plaintext API key. Rotating
    // the persisted cipher (and therefore the resolved plaintext) yields a
    // brand-new client; old clients are eligible for GC.
    private readonly ConcurrentDictionary<string, AnthropicClient> _clientsByKey =
        new(StringComparer.Ordinal);

    // Per-model breaker state — keyed on model id. A Haiku trip MUST NOT
    // block Sonnet calls (Gap 29 fix, 2026-05-03). Each entry carries its
    // own failureCount + circuitOpen flag + opened-at timestamp under its
    // own lock so writers serialize per-model.
    private readonly ConcurrentDictionary<string, BreakerState> _breakers =
        new(StringComparer.Ordinal);

    // Mirrors the LlmCircuitBreakerActor thresholds the rest of the platform
    // uses (3 failures within OpenDuration → open). Keep in lockstep.
    private const int MaxFailures = 3;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(90);

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
        IMeterFactory meterFactory,
        IActivityPropagator? activityPropagator = null)
    {
        _logger = logger;
        _activityPropagator = activityPropagator;

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
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        var state = GetOrCreateBreaker(modelName);
        lock (state.Lock)
        {
            if (!state.CircuitOpen) return;

            if (DateTimeOffset.UtcNow - state.CircuitOpenedAt >= OpenDuration)
            {
                _logger.LogInformation(
                    "[CIRCUIT_HALFOPEN] trace_id={TraceId} model={Model} action=allow_probe",
                    SafeTraceId(), modelName);
                state.CircuitOpen = false;
                state.FailureCount = 0;
                return;
            }

            var retryAfter = OpenDuration - (DateTimeOffset.UtcNow - state.CircuitOpenedAt);
            throw new CircuitOpenException(
                $"Circuit breaker OPEN for model {modelName}. Retry after {retryAfter.TotalSeconds:F0}s.");
        }
    }

    /// <inheritdoc/>
    public void RecordSuccess(string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        var state = GetOrCreateBreaker(modelName);
        lock (state.Lock)
        {
            state.FailureCount = 0;
            state.CircuitOpen = false;
        }
    }

    /// <inheritdoc/>
    public void RecordFailure(string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        var state = GetOrCreateBreaker(modelName);
        lock (state.Lock)
        {
            state.FailureCount++;
            _logger.LogWarning(
                "[CIRCUIT_FAILURE] trace_id={TraceId} model={Model} count={Count}/{Max}",
                SafeTraceId(), modelName, state.FailureCount, MaxFailures);

            if (state.FailureCount >= MaxFailures && !state.CircuitOpen)
            {
                state.CircuitOpen = true;
                state.CircuitOpenedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "[CIRCUIT_OPEN] trace_id={TraceId} model={Model} reason=failure_threshold_reached failures={Count} open_duration_s={Duration}",
                    SafeTraceId(), modelName, state.FailureCount, OpenDuration.TotalSeconds);
            }
        }
    }

    /// <inheritdoc/>
    public void EmitMetrics(string model, string taskType, long durationMs,
        long inputTokens, long outputTokens, LlmCallPricing pricing)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        ArgumentException.ThrowIfNullOrEmpty(taskType);
        // No default pricing — the runtime previously hardcoded Sonnet's
        // $3/$15 per Mtok which over-reported Haiku callers by ~3x on the
        // legacy llm_cost_usd meter. Forcing the caller to pass pricing
        // makes that bug class impossible to repeat (Gap 30 fix).
        pricing.Validate();

        var modelTag = new KeyValuePair<string, object?>("model_id", model);
        var taskTag = new KeyValuePair<string, object?>("task_type", taskType);

        _requestDuration.Record(durationMs, modelTag, taskTag,
            new KeyValuePair<string, object?>("status", "success"));

        _tokensTotal.Add(inputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "input"));
        _tokensTotal.Add(outputTokens, modelTag, taskTag,
            new KeyValuePair<string, object?>("direction", "output"));

        // Compute in decimal (currency-safe), emit as double for the meter.
        // Tokens are clamped to >= 0 so a misbehaving Anthropic streaming
        // failure that returns a negative usage field cannot corrupt the
        // counter — mirrors the LlmPricingTable.ComputeCostUsd contract.
        var inSafe = Math.Max(0L, inputTokens);
        var outSafe = Math.Max(0L, outputTokens);
        var costDecimal = (inSafe * pricing.InputUsdPerMtok
                           + outSafe * pricing.OutputUsdPerMtok) / 1_000_000m;
        var cost = (double)costDecimal;
        _costUsd.Add(cost, modelTag, taskTag);

        _logger.LogInformation(
            "LLM call completed: trace_id={TraceId} model={Model} task={Task} duration={DurationMs}ms " +
            "input_tokens={InputTokens} output_tokens={OutputTokens} cost_usd={Cost:F6} " +
            "input_rate_usd_per_mtok={InputRate} output_rate_usd_per_mtok={OutputRate}",
            SafeTraceId(), model, taskType, durationMs, inputTokens, outputTokens, cost,
            pricing.InputUsdPerMtok, pricing.OutputUsdPerMtok);
    }

    // ── internals ────────────────────────────────────────────────────────

    private BreakerState GetOrCreateBreaker(string modelName)
        => _breakers.GetOrAdd(modelName, static _ => new BreakerState());

    private string SafeTraceId()
    {
        // GetTraceId is documented as never returning null/empty, but the
        // runtime is also reachable from ctor paths in tests where no
        // Activity / CorrelationContext is set. Wrap defensively so a
        // misbehaving propagator can't crash a metric emission path.
        try
        {
            return _activityPropagator?.GetTraceId() ?? "no-trace";
        }
        catch
        {
            return "no-trace";
        }
    }

    /// <summary>
    /// Per-model breaker state. Mutated under <see cref="Lock"/>; readers
    /// must take the lock too. Kept private — the runtime is the sole owner
    /// of the breaker invariants (3 failures → open; OpenDuration → half-open).
    /// </summary>
    private sealed class BreakerState
    {
        public readonly object Lock = new();
        public int FailureCount;
        public DateTimeOffset CircuitOpenedAt;
        public bool CircuitOpen;
    }
}
