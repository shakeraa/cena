// =============================================================================
// Cena Platform — Anthropic LLM Runtime (shared across question-generation,
// OCR-enhance, and any future Anthropic-backed admin feature).
//
// This is the single home of:
//   1) The Anthropic SDK client cache (keyed on plaintext API key).
//   2) The in-process circuit breaker (3 failures / 90s open) shared by every
//      caller, so a flaky-model trip in question-generation also gates
//      OCR-enhance and vice versa.
//   3) The legacy per-service OpenTelemetry meters
//      (Cena.Admin.LlmMetrics: llm_request_duration_ms, llm_tokens_total,
//      llm_cost_usd) — registered exactly once instead of being constructed
//      per-consumer.
//
// Registered as a Singleton (see CenaAdminServiceRegistration.cs) so the
// breaker state, client cache, and meter counters are shared across every
// HTTP request that touches Anthropic.
//
// Extracted 2026-05-03 to unify what were previously two parallel breakers
// (AiGenerationService + OcrTextEnhancer). The cost-counter side
// (cena_llm_call_cost_usd_total via ILlmCostMetric) stays at the call site
// because per-feature cost requires per-feature tags; the legacy
// per-service meters live here.
// =============================================================================

using System.Text.Json;
using Anthropic;

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Shared Anthropic SDK runtime — owns the client cache, circuit breaker, and
/// legacy per-service meters used by every admin LLM call site.
/// </summary>
public interface IAnthropicLlmRuntime
{
    /// <summary>
    /// Return the Anthropic client for the given plaintext API key. The
    /// client is cached so that consecutive calls with the same key reuse
    /// one HTTP pipeline; rotating the persisted cipher (and therefore the
    /// resolved plaintext) automatically returns a fresh client.
    /// </summary>
    AnthropicClient GetOrCreateClient(string apiKey);

    /// <summary>
    /// Throws <see cref="CircuitOpenException"/> if the breaker for
    /// <paramref name="modelName"/> is currently open. Callers MUST call
    /// this before issuing the outbound Anthropic request and call
    /// <see cref="RecordSuccess"/> on the happy path / <see cref="RecordFailure"/>
    /// on the unhappy path so the breaker reflects reality.
    /// </summary>
    void RequestCircuitPermission(string modelName);

    /// <summary>Closes the breaker on a successful Anthropic round-trip.</summary>
    void RecordSuccess(string modelName);

    /// <summary>
    /// Records a failed round-trip and opens the breaker once the failure
    /// count crosses the threshold. The breaker auto-recovers after the
    /// open-duration window via the next <see cref="RequestCircuitPermission"/>.
    /// </summary>
    void RecordFailure(string modelName);

    /// <summary>
    /// Emit the legacy per-service meters
    /// (llm_request_duration_ms, llm_tokens_total, llm_cost_usd) tagged with
    /// the model_id and task_type so dashboards can split by feature.
    /// </summary>
    void EmitMetrics(string model, string taskType, long durationMs,
        long inputTokens, long outputTokens);

    /// <summary>
    /// Shared <see cref="JsonSerializerOptions"/> used to deserialize
    /// Anthropic tool_use responses; exposed so call sites that parse
    /// tool_use Input dictionaries don't each construct their own copy.
    /// </summary>
    JsonSerializerOptions JsonOpts { get; }
}
