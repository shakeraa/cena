# Task 00: Implement Real LLM API Calls (Anthropic SDK)

**Effort**: 2-3 days | **Track**: A | **Depends on**: Nothing | **Blocks**: 02, 03, 07

---

## Context

You are working on the **Cena Platform** — an adaptive learning system for Israeli Bagrut exam preparation. The platform is event-sourced .NET 8, using Proto.Actor virtual actors, Marten (PostgreSQL event store), NATS JetStream, and SignalR for real-time student communication.

The `AiGenerationService` currently has **4 provider stubs** (Anthropic, OpenAI, Google, Azure) that all call `GenerateMockResponse()` — returning fake data. No real LLM API calls exist in the codebase. The only production LLM integration is `GeminiOcrClient` for OCR.

A comprehensive routing config (`contracts/llm/routing-config.yaml`, 552 lines) maps Claude Sonnet 4.6 as primary tutoring model and Kimi K2.5 for structured tasks. An `LlmCircuitBreakerActor` (396 lines) provides per-model circuit breaking. None of this is wired to real API calls yet.

Three quality gate dimensions in `QualityGateService` are stubbed at default scores (FactualAccuracy=80, LanguageQuality=80, PedagogicalQuality=75) awaiting real LLM evaluation.

---

## Objective

Replace the Anthropic provider stub in `AiGenerationService` with real Anthropic SDK calls using Claude Sonnet 4.6. Unblock the 3 stubbed quality gate dimensions. Leave other provider stubs as `NotImplementedException`.

---

## Files to Read First (MANDATORY — understand before changing)

| File | Path | Why | Lines |
|------|------|-----|-------|
| AiGenerationService | `src/api/Cena.Admin.Api/AiGenerationService.cs` | 4 stubs at ~line 327-362. `GenerateMockResponse()` at line 364. `AiGeneratedQuestion` DTO at line 85 with `Explanation` at line 91 | 395 |
| QualityGateService | `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 3 dimensions stubbed: FactualAccuracy, LanguageQuality, PedagogicalQuality | 117 |
| LLM Routing Config | `contracts/llm/routing-config.yaml` | Full config: models, rate limits, cost caps, circuit breaker thresholds, prompt caching, task type routing | 552 |
| LlmCircuitBreakerActor | `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Per-model circuit breaker (Sonnet: 3 failures/90s). All LLM calls MUST flow through this | 396 |

---

## Architectural Requirements

### 1. Anthropic SDK Only

Use the official `Anthropic` NuGet package. Do NOT hand-roll HTTP calls. Do NOT implement Kimi/OpenAI/Azure — replace those stubs with `throw new NotImplementedException("Provider not yet implemented — use Anthropic")`.

### 2. Respect the Routing Config Contract

The routing config is the contract. Code conforms to it, not the other way around:
- Temperature and max_tokens come from `routing-config.yaml` per task type
- Implement prompt caching: system prompts use `cache_control: { type: "ephemeral" }` (config section 6)
- API key from `IConfiguration["Anthropic:ApiKey"]` — NEVER hardcoded

### 3. Wire Into Existing Circuit Breaker

`LlmCircuitBreakerActor` already manages per-model state with thresholds:
- Sonnet: 3 failures in 90s → open circuit
- Kimi: 5 failures in 60s
- Opus: 2 failures in 120s

New SDK calls MUST flow through this actor. Pattern:
```csharp
// Request through circuit breaker
var permitted = await _circuitBreaker.RequestPermission("claude-sonnet-4-6");
if (!permitted) throw new CircuitOpenException("Sonnet circuit open");
try {
    var result = await _anthropicClient.Messages.CreateAsync(...);
    await _circuitBreaker.RecordSuccess("claude-sonnet-4-6");
    return result;
} catch (Exception ex) {
    await _circuitBreaker.RecordFailure("claude-sonnet-4-6");
    throw;
}
```

### 4. Structured Output

Response must deserialize into `AiGeneratedQuestion` (with `Explanation`). Use Anthropic's tool_use or JSON mode for reliable structured output. The existing DTO shape must not change.

### 5. PII Handling

Anthropic is `is_trusted_provider: true` per routing config — no PII stripping. But design the interface so PII stripping can be added when Kimi (untrusted) is implemented later.

### 6. Quality Gate Integration

After real LLM calls work, update `QualityGateService` to use LLM for the 3 stubbed dimensions:
- Send the generated question text to Claude with a rubric prompt
- Parse structured scores for FactualAccuracy, LanguageQuality, PedagogicalQuality
- Replace the hardcoded defaults (80, 80, 75)

### 7. Observability

Every call emits metrics defined in routing-config section 9:
- `llm_request_duration_ms` (histogram)
- `llm_tokens_total` (counter, labels: model, task_type, direction)
- `llm_cost_usd` (counter, labels: model)

---

## What NOT to Do

- Do NOT add a new abstraction layer, "LLM provider factory", or strategy pattern — `AiGenerationService` with its switch on `AiProvider` is correct
- Do NOT implement streaming — question generation is batch, not interactive
- Do NOT modify `routing-config.yaml` — it's the contract
- Do NOT add retry logic inside the service — the circuit breaker handles retries
- Do NOT create new files for the Anthropic client — keep it in `AiGenerationService`

---

## Verification Checklist

- [ ] Generate 3 questions via Admin API with a real API key — `Explanation` field populated
- [ ] Quality gate runs with real LLM scores (not defaults 80/80/75)
- [ ] Circuit breaker trips after simulated failures (per Sonnet threshold: 3/90s)
- [ ] Metrics emitted: `llm_request_duration_ms`, `llm_tokens_total`, `llm_cost_usd`
- [ ] Other 3 provider stubs throw `NotImplementedException` (not silent mock)
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
- [ ] No API keys committed to source
