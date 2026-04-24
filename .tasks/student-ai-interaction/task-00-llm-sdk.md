# Task 00: Implement Real Anthropic SDK Calls

**Track**: A (parallel with Task 01a)
**Effort**: 2-3 days
**Depends on**: Nothing
**Blocks**: Task 02, Task 03, Task 04

---

## System Context

Cena is an event-sourced .NET educational platform using Proto.Actor virtual actors, Marten/PostgreSQL event store, NATS JetStream, and SignalR for real-time student sessions. Students receive AI-generated questions through a learning pipeline. The AI generation service currently has 4 provider stubs — all return mock data. No real LLM calls exist except `GeminiOcrClient` for OCR.

A routing config at `contracts/llm/routing-config.yaml` defines Claude Sonnet 4.6 as the primary tutoring model, Kimi K2.5 for structured tasks, and Haiku 4.5 as degraded fallback. The circuit breaker infrastructure (`LlmCircuitBreakerActor`) already manages per-model failure thresholds.

This task replaces the Anthropic mock stub with real SDK calls. It is the foundational unblock for all AI-powered student interactions.

---

## Mandatory Pre-Read (Understand Before Changing)

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 327-362 | 4 provider stubs: `CallAnthropicAsync()` (327), `CallOpenAiAsync()` (337), `CallGoogleAsync()` (346), `CallAzureOpenAiAsync()` (355) — all call `GenerateMockResponse()` (364-394) |
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 85-91 | `AiGeneratedQuestion` DTO — line 91 has `Explanation` field already |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 27-73 | `Evaluate()` method. Lines 48-51: 3 stubbed dimensions (FactualAccuracy=80, LanguageQuality=80, PedagogicalQuality=75) awaiting real LLM |
| `contracts/llm/routing-config.yaml` | 54-104 | Claude model configs (Sonnet 4.6, Haiku 4.5). Lines 111-201: task-to-model mappings. Lines 343-383: prompt caching config with `cache_control: { type: "ephemeral" }` |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | 34-59 | `CircuitBreakerConfig` record. Line 42: Sonnet config (3 failures / 90s). Lines 174-297: state machine transitions (Closed/Open/HalfOpen) |

---

## Implementation Requirements

### 1. Anthropic SDK Integration

- Install `Anthropic` NuGet package (official .NET SDK). Do NOT hand-roll HTTP calls.
- Replace `CallAnthropicAsync()` (line 327) with real SDK call using `AnthropicClient`.
- API key from `IConfiguration["Anthropic:ApiKey"]` — never hardcoded.
- Model ID from routing config: `claude-sonnet-4-6-20250514` (or latest).

### 2. Respect the Routing Config Contract

The routing config at `contracts/llm/routing-config.yaml` is the **contract**. Code conforms to it.

- **Temperature per task type** — `question_generation` task (lines 111-130): temp 0.7, max_tokens 4096.
- **Prompt caching** — system prompts use `cache_control: { type: "ephemeral" }` (lines 370-382). The Anthropic SDK supports this via the `CacheControl` property on message blocks.
- **Structured output** — use Anthropic tool_use/JSON mode to get reliable structured `AiGeneratedQuestion` deserialization. The DTO (line 85) is the target schema.

### 3. Wire Into Existing Circuit Breaker

All calls must flow through `LlmCircuitBreakerActor`:
1. Before each call: `RequestPermission(modelName: "sonnet")` — if circuit is Open, fail fast.
2. On success: `ReportSuccess(modelName: "sonnet")`.
3. On failure: `ReportFailure(modelName: "sonnet", exception)` — circuit opens after 3 failures in 90s.

Do NOT add retry logic inside the service. The circuit breaker handles that.

### 4. Other Provider Stubs

- `CallOpenAiAsync()`, `CallGoogleAsync()`, `CallAzureOpenAiAsync()` — change from returning mock data to throwing `NotImplementedException("Provider not yet implemented. Use Anthropic.")`. Silent mocks in production paths are a defect.

### 5. Quality Gate LLM Integration

After real LLM calls work, update `QualityGateService.Evaluate()` (line 27):
- Lines 48-51: Replace the 3 hardcoded defaults with LLM-evaluated scores.
- Use Haiku (cheaper model) for quality evaluation — it's an assessment task, not generation.
- The LLM evaluates: factual accuracy, language quality, pedagogical quality of the generated question.
- Keep `culturalSensitivity` at default 80 for now (requires specialized evaluation).

### 6. Observability

Every LLM call must emit metrics defined in routing-config.yaml section 9:
- `llm_request_duration_ms` — wall-clock time of the SDK call
- `llm_tokens_total` — input + output tokens from the response
- `llm_cost_usd` — computed from token counts and per-model pricing in the config

Use the existing `ILogger` pattern in the service. Structured logging with these fields.

---

## What NOT to Do

- Do NOT add a new abstraction layer, "LLM provider factory", or "ILlmClient" interface — the existing `AiGenerationService` with switch-on-`AiProvider` is correct for now
- Do NOT implement streaming — question generation is batch, not interactive
- Do NOT modify `routing-config.yaml` — it's the contract
- Do NOT add retry logic inside the service — circuit breaker handles it
- Do NOT implement Kimi/OpenAI/Azure — Anthropic only in this task
- Do NOT create new files unless strictly necessary — prefer editing existing

---

## Verification Checklist

- [ ] Generate 3 questions via Admin API with a real API key — `Explanation` field populated
- [ ] Quality gate returns real LLM scores (not hardcoded 80/80/75) for generated questions
- [ ] Circuit breaker trips after 3 simulated failures within 90s window
- [ ] Non-Anthropic provider stubs throw `NotImplementedException`
- [ ] Structured logging emits `llm_request_duration_ms`, `llm_tokens_total`, `llm_cost_usd`
- [ ] Prompt caching configured with `cache_control: { type: "ephemeral" }` on system prompt
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
- [ ] No secrets in source (API key from config only)
