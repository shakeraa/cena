// =============================================================================
// AnthropicLlmRuntimeTests — verifies the unified runtime contract:
//   1) GetOrCreateClient caches per-key (same key → same instance,
//      different key → different instance).
//   2) The shared circuit breaker opens at the failure threshold and
//      throws CircuitOpenException on RequestCircuitPermission until the
//      open-window expires; RecordSuccess closes it.
//   3) EmitMetrics writes to the meter "Cena.Admin.LlmMetrics" with the
//      expected instrument names + tags.
//
// The tests construct AnthropicLlmRuntime directly with a real
// IMeterFactory and an in-memory MeterListener so we exercise the
// production OpenTelemetry path without spinning up a host.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Admin.Api.AiSettings;
using Microsoft.Extensions.DependencyInjection;
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
            outputTokens: 250);

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
}
