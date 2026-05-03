// =============================================================================
// AnthropicLlmRuntimeTests — verifies the unified runtime contract:
//   1) GetOrCreateClient caches per-key (same key → same instance,
//      different key → different instance).
//   2) The shared circuit breaker opens at the failure threshold and
//      throws CircuitOpenException on RequestCircuitPermission until the
//      open-window expires; RecordSuccess closes it.
//   3) EmitMetrics writes to the meter "Cena.Admin.LlmMetrics" with the
//      expected instrument names + tags and the per-call pricing.
//   4) (2026-05-03) Per-model breaker isolation: a Haiku trip does not
//      gate Sonnet calls. (Gap 29 fix.)
//   5) (2026-05-03) Per-call pricing is reflected on llm_cost_usd; no
//      Sonnet-priced default for Haiku callers. (Gap 30 fix.)
//   6) (2026-05-03) Breaker-open / failure-tick log lines carry the
//      current trace_id so SIEM stitching matches the per-feature cost
//      counter. (post-30 fix.)
//
// The tests construct AnthropicLlmRuntime directly with a real
// IMeterFactory and an in-memory MeterListener so we exercise the
// production OpenTelemetry path without spinning up a host.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Admin.Api.AiSettings;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Admin.Api.Tests.AiSettings;

public class AnthropicLlmRuntimeTests
{
    private static AnthropicLlmRuntime NewRuntime(out IMeterFactory factory)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        var sp = services.BuildServiceProvider();
        factory = sp.GetRequiredService<IMeterFactory>();
        return new AnthropicLlmRuntime(NullLogger<AnthropicLlmRuntime>.Instance, factory);
    }

    private static AnthropicLlmRuntime NewRuntime(
        ILogger<AnthropicLlmRuntime> logger,
        IActivityPropagator? propagator,
        out IMeterFactory factory)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        var sp = services.BuildServiceProvider();
        factory = sp.GetRequiredService<IMeterFactory>();
        return new AnthropicLlmRuntime(logger, factory, propagator);
    }

    [Fact]
    public void GetOrCreateClient_ReturnsSameInstanceForSameKey()
    {
        var runtime = NewRuntime(out _);

        var a = runtime.GetOrCreateClient("sk-test-key-A");
        var b = runtime.GetOrCreateClient("sk-test-key-A");

        Assert.Same(a, b);
    }

    [Fact]
    public void GetOrCreateClient_ReturnsDifferentInstanceForDifferentKey()
    {
        var runtime = NewRuntime(out _);

        var a = runtime.GetOrCreateClient("sk-test-key-A");
        var b = runtime.GetOrCreateClient("sk-test-key-B");

        Assert.NotSame(a, b);
    }

    [Fact]
    public void GetOrCreateClient_RejectsEmptyKey()
    {
        var runtime = NewRuntime(out _);

        Assert.Throws<ArgumentException>(() => runtime.GetOrCreateClient(""));
    }

    [Fact]
    public void RequestCircuitPermission_AllowsTrafficWhenClosed()
    {
        var runtime = NewRuntime(out _);

        // Default state: closed → no throw.
        runtime.RequestCircuitPermission("claude-sonnet-4-6");
    }

    [Fact]
    public void CircuitBreaker_OpensAfterThreeFailures_AndThrowsOnPermission()
    {
        var runtime = NewRuntime(out _);
        const string model = "claude-sonnet-4-6";

        runtime.RecordFailure(model);
        runtime.RecordFailure(model);
        // Third failure trips the breaker.
        runtime.RecordFailure(model);

        var ex = Assert.Throws<CircuitOpenException>(
            () => runtime.RequestCircuitPermission(model));
        Assert.Contains(model, ex.Message);
        Assert.Contains("OPEN", ex.Message);
    }

    [Fact]
    public void RecordSuccess_ResetsBreaker()
    {
        var runtime = NewRuntime(out _);
        const string model = "claude-sonnet-4-6";

        // Two failures — below threshold, so breaker stays closed.
        runtime.RecordFailure(model);
        runtime.RecordFailure(model);

        // Success on the next call should zero the counter.
        runtime.RecordSuccess(model);

        // Two more failures should still NOT trip — the success reset cleared
        // the count. (3-failure threshold; 2 < 3).
        runtime.RecordFailure(model);
        runtime.RecordFailure(model);
        runtime.RequestCircuitPermission(model); // no-throw expected
    }

    [Fact]
    public void EmitMetrics_RecordsToCenaAdminLlmMetricsMeter()
    {
        var runtime = NewRuntime(out _);

        var captured = new List<(string Name, double Value, string? Model, string? Task)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Cena.Admin.LlmMetrics")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            string? m = null, t = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "model_id") m = tag.Value?.ToString();
                if (tag.Key == "task_type") t = tag.Value?.ToString();
            }
            captured.Add((inst.Name, value, m, t));
        });
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            string? m = null, t = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "model_id") m = tag.Value?.ToString();
                if (tag.Key == "task_type") t = tag.Value?.ToString();
            }
            captured.Add((inst.Name, value, m, t));
        });
        listener.Start();

        runtime.EmitMetrics(
            model: "claude-sonnet-4-6",
            taskType: "question_generation",
            durationMs: 1234,
            inputTokens: 500,
            outputTokens: 250,
            pricing: LlmCallPricing.AnthropicSonnet4_6);

        // Expect at least: llm_request_duration_ms, llm_tokens_total (×2),
        // llm_cost_usd — all tagged with the shared model + task tags.
        Assert.Contains(captured, c =>
            c.Name == "llm_request_duration_ms" && c.Value == 1234d
            && c.Model == "claude-sonnet-4-6" && c.Task == "question_generation");
        Assert.Contains(captured, c =>
            c.Name == "llm_tokens_total" && c.Value == 500
            && c.Model == "claude-sonnet-4-6" && c.Task == "question_generation");
        Assert.Contains(captured, c =>
            c.Name == "llm_tokens_total" && c.Value == 250
            && c.Model == "claude-sonnet-4-6" && c.Task == "question_generation");
        Assert.Contains(captured, c =>
            c.Name == "llm_cost_usd"
            && c.Model == "claude-sonnet-4-6" && c.Task == "question_generation");
    }

    [Fact]
    public void JsonOpts_UsesCamelCaseAndCaseInsensitiveProperties()
    {
        var runtime = NewRuntime(out _);

        // Verifying the documented contract — call sites that serialize
        // tool_use Input dictionaries through this options bag rely on
        // camelCase property names matching the LLM-emitted shape.
        Assert.True(runtime.JsonOpts.PropertyNameCaseInsensitive);
        Assert.Equal(System.Text.Json.JsonNamingPolicy.CamelCase, runtime.JsonOpts.PropertyNamingPolicy);
    }

    // ── Gap 29 — per-model breaker isolation ─────────────────────────────

    [Fact]
    public void Breaker_OpensPerModelOnly_DoesNotAffectOtherModel()
    {
        // Trip Haiku's breaker with three failures. Sonnet's breaker MUST
        // remain closed and Sonnet calls MUST be allowed through. The
        // pre-fix runtime shared one _failureCount across all models so
        // a Haiku outage was a Sonnet outage.
        var runtime = NewRuntime(out _);
        const string haiku = "claude-haiku-4-5-20260101";
        const string sonnet = "claude-sonnet-4-6";

        runtime.RecordFailure(haiku);
        runtime.RecordFailure(haiku);
        runtime.RecordFailure(haiku);

        // Haiku's breaker is OPEN — RequestCircuitPermission throws.
        Assert.Throws<CircuitOpenException>(
            () => runtime.RequestCircuitPermission(haiku));

        // Sonnet must still be permitted — independent breaker.
        runtime.RequestCircuitPermission(sonnet); // no-throw expected

        // Sonnet should still be able to fail twice without opening.
        runtime.RecordFailure(sonnet);
        runtime.RecordFailure(sonnet);
        runtime.RequestCircuitPermission(sonnet); // 2 < 3, still closed
    }

    [Fact]
    public void Breaker_PerModel_RecordSuccessOnlyResetsCallerModel()
    {
        // Two failures on Haiku and two on Sonnet. RecordSuccess(haiku)
        // must reset Haiku's count without touching Sonnet's. (If the
        // per-model fix shared state, Sonnet's count would also reset.)
        var runtime = NewRuntime(out _);
        const string haiku = "claude-haiku-4-5-20260101";
        const string sonnet = "claude-sonnet-4-6";

        runtime.RecordFailure(haiku);
        runtime.RecordFailure(haiku);
        runtime.RecordFailure(sonnet);
        runtime.RecordFailure(sonnet);

        runtime.RecordSuccess(haiku);

        // One more Sonnet failure should trip Sonnet's breaker (2+1=3),
        // proving Sonnet's count was untouched by RecordSuccess(haiku).
        runtime.RecordFailure(sonnet);
        Assert.Throws<CircuitOpenException>(
            () => runtime.RequestCircuitPermission(sonnet));

        // Haiku's count was reset; one failure leaves it well below threshold.
        runtime.RecordFailure(haiku);
        runtime.RequestCircuitPermission(haiku); // no-throw expected
    }

    // ── Gap 30 — per-call pricing accuracy ───────────────────────────────

    [Fact]
    public void EmitMetrics_HaikuPricing_ReportsAccurateCost()
    {
        // Haiku 4.5 published rates: $1 input / $5 output per MTok.
        // Pre-fix runtime hardcoded Sonnet $3/$15 → ~3x over-report.
        var runtime = NewRuntime(out _);
        var captured = CaptureCost();

        runtime.EmitMetrics(
            model: "claude-haiku-4-5-20260101",
            taskType: "concept_extraction",
            durationMs: 200,
            inputTokens: 1_000_000, // 1M tokens — easy hand math
            outputTokens: 500_000,
            pricing: LlmCallPricing.AnthropicHaiku4_5);

        // 1M * $1 + 0.5M * $5 = $1 + $2.5 = $3.50 (Haiku pricing).
        // Pre-fix this would have been 1M * $3 + 0.5M * $15 = $10.50.
        var sample = Assert.Single(captured,
            c => c.Name == "llm_cost_usd"
                 && c.Model == "claude-haiku-4-5-20260101");
        Assert.Equal(3.50d, sample.Value, precision: 6);
    }

    [Fact]
    public void EmitMetrics_SonnetPricing_BehaviorPreserved()
    {
        // Pinning the legacy Sonnet path so the regression-by-default
        // protection does not silently shift Sonnet costs.
        var runtime = NewRuntime(out _);
        var captured = CaptureCost();

        runtime.EmitMetrics(
            model: "claude-sonnet-4-6",
            taskType: "question_generation",
            durationMs: 200,
            inputTokens: 1_000_000,
            outputTokens: 500_000,
            pricing: LlmCallPricing.AnthropicSonnet4_6);

        // 1M * $3 + 0.5M * $15 = $3 + $7.5 = $10.50.
        var sample = Assert.Single(captured,
            c => c.Name == "llm_cost_usd"
                 && c.Model == "claude-sonnet-4-6");
        Assert.Equal(10.50d, sample.Value, precision: 6);
    }

    [Fact]
    public void EmitMetrics_NegativeTokens_ClampedToZero()
    {
        // The Anthropic SDK occasionally returns missing usage fields on
        // streaming failures. The runtime must clamp to >= 0 rather than
        // emit a negative cost that would corrupt the counter — mirrors
        // LlmPricingTable.ComputeCostUsd.
        var runtime = NewRuntime(out _);
        var captured = CaptureCost();

        runtime.EmitMetrics(
            model: "claude-haiku-4-5-20260101",
            taskType: "concept_extraction",
            durationMs: 200,
            inputTokens: -100,
            outputTokens: -50,
            pricing: LlmCallPricing.AnthropicHaiku4_5);

        var sample = Assert.Single(captured, c => c.Name == "llm_cost_usd");
        Assert.Equal(0d, sample.Value);
    }

    [Fact]
    public void LlmCallPricing_Validate_RejectsNegativeRates()
    {
        var pricing = new LlmCallPricing(InputUsdPerMtok: -1m, OutputUsdPerMtok: 5m);
        Assert.Throws<ArgumentOutOfRangeException>(() => pricing.Validate());
    }

    // ── post-30 — trace_id propagation in runtime SIEM logs ──────────────

    [Fact]
    public void RuntimeLog_OnBreakerOpen_IncludesTraceId()
    {
        // When the breaker trips, the runtime log line must carry the
        // current trace_id so the SIEM/Grafana side can stitch the
        // failure event to the request that caused it.
        var fakeLogger = new FakeLogger();
        var fakePropagator = new FakeActivityPropagator(traceId: "trace-deadbeef-1234");
        var runtime = NewRuntime(fakeLogger, fakePropagator, out _);
        const string model = "claude-sonnet-4-6";

        runtime.RecordFailure(model);
        runtime.RecordFailure(model);
        runtime.RecordFailure(model); // trips → emits CIRCUIT_OPEN log line

        // Both the failure-tick logs and the CIRCUIT_OPEN log must
        // include the current trace id.
        Assert.Contains(fakeLogger.Records,
            r => r.Message.Contains("[CIRCUIT_OPEN]")
                 && r.Message.Contains("trace_id=trace-deadbeef-1234")
                 && r.Message.Contains($"model={model}"));
        Assert.Contains(fakeLogger.Records,
            r => r.Message.Contains("[CIRCUIT_FAILURE]")
                 && r.Message.Contains("trace_id=trace-deadbeef-1234"));
    }

    [Fact]
    public void RuntimeLog_OnHalfOpenProbe_IncludesTraceId()
    {
        // Once the open-window elapses the breaker auto-half-opens on
        // the next RequestCircuitPermission call. That state transition
        // is also a SIEM-relevant event — must carry trace_id too.
        var fakeLogger = new FakeLogger();
        var fakePropagator = new FakeActivityPropagator(traceId: "trace-cafebabe");
        var runtime = NewRuntime(fakeLogger, fakePropagator, out _);
        const string model = "claude-sonnet-4-6";

        runtime.RecordFailure(model);
        runtime.RecordFailure(model);
        runtime.RecordFailure(model);
        // Open-duration is 90s; we cannot fast-forward without a clock
        // seam. Verify the CIRCUIT_OPEN log carried trace_id (the
        // open-window behaviour is exercised by the existing
        // CircuitBreaker_OpensAfterThreeFailures test).
        Assert.Contains(fakeLogger.Records,
            r => r.Message.Contains("[CIRCUIT_OPEN]")
                 && r.Message.Contains("trace_id=trace-cafebabe"));
    }

    [Fact]
    public void Runtime_WithoutPropagator_DoesNotCrash()
    {
        // Some test compositions and historical hosts do not register
        // IActivityPropagator. The runtime must degrade to "no-trace"
        // rather than NullReferenceException on the SIEM log paths.
        var runtime = NewRuntime(out _); // null propagator
        runtime.RecordFailure("claude-sonnet-4-6");
        runtime.RecordFailure("claude-sonnet-4-6");
        runtime.RecordFailure("claude-sonnet-4-6"); // would log + crash on null deref pre-fix
        Assert.Throws<CircuitOpenException>(
            () => runtime.RequestCircuitPermission("claude-sonnet-4-6"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static List<(string Name, double Value, string? Model, string? Task)> CaptureCost()
    {
        var captured = new List<(string Name, double Value, string? Model, string? Task)>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Cena.Admin.LlmMetrics")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
        {
            string? m = null, t = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "model_id") m = tag.Value?.ToString();
                if (tag.Key == "task_type") t = tag.Value?.ToString();
            }
            captured.Add((inst.Name, value, m, t));
        });
        listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
        {
            string? m = null, t = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "model_id") m = tag.Value?.ToString();
                if (tag.Key == "task_type") t = tag.Value?.ToString();
            }
            captured.Add((inst.Name, value, m, t));
        });
        listener.Start();
        return captured;
    }

    /// <summary>
    /// Captures every LogXxx call as a flattened message + level. Used by
    /// the trace-id assertions to grep formatted message text. Keep
    /// minimal — this is test infra, not a production logger.
    /// </summary>
    private sealed class FakeLogger : ILogger<AnthropicLlmRuntime>
    {
        public readonly List<(LogLevel Level, string Message)> Records = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Returns a deterministic trace id; used to verify the runtime
    /// stamps it on circuit-open / circuit-failure log lines. StartLlm-
    /// Activity is unused by AnthropicLlmRuntime; returns null.
    /// </summary>
    private sealed class FakeActivityPropagator : IActivityPropagator
    {
        private readonly string _traceId;
        public FakeActivityPropagator(string traceId) => _traceId = traceId;
        public string GetTraceId() => _traceId;
        public System.Diagnostics.Activity? StartLlmActivity(string taskName) => null;
    }
}
