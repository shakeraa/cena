# TASK-SAI-00: Implement Real LLM Provider Calls (Anthropic SDK)

**Priority**: CRITICAL — blocks Tasks 2, 3, 4, 7
**Effort**: 2-3 days
**Depends on**: Nothing
**Track**: A (parallel with TASK-SAI-01)
**Status**: Not Started

---

## You Are

A senior .NET architect implementing a production LLM integration into an event-sourced educational platform. You are pedantic about correctness, you trace every data flow end-to-end, and you refuse to ship code that "works but isn't right." You write code that a future engineer will thank you for — not curse.

## The Problem

`AiGenerationService` has 4 provider stubs (Anthropic, OpenAI, Google, Azure). **ALL return mock data via `GenerateMockResponse()`**. The platform has no real LLM integration despite having:
- A complete routing config (`contracts/llm/routing-config.yaml`) mapping 10 task types to specific models
- A per-model circuit breaker (`LlmCircuitBreakerActor`)
- Cost caps, rate limits, prompt caching strategies — all configured, none wired

The only real LLM call is `GeminiOcrClient` for OCR. Everything else is fake.

Completing this also unblocks 3 stubbed quality gate dimensions in `QualityGateService` that return hardcoded scores (80, 80, 75).

---

## Files You MUST Read Before Writing Any Code

Read these files completely. Do not skim. Understand every method, every field, every integration point.

| File | What You're Looking For |
|------|------------------------|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | Full file. Understand `AiProvider` enum, `AiProviderConfig`, `AiGenerateRequest/Response`, `AiGeneratedQuestion` (line 85 — note `Explanation` field at line 91). Find the 4 `Call*Async` methods (lines 327-362) — ALL return `GenerateMockResponse()`. Find `BuildPrompt()` to understand prompt construction. |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | Find the 3 dimensions that return default scores: FactualAccuracy=80, LanguageQuality=80, PedagogicalQuality=75. Understand how they should use LLM evaluation. |
| `contracts/llm/routing-config.yaml` | Full file. This is your contract. Section 1: model definitions (IDs, providers, costs). Section 2: task-to-model mapping with fallback chains. Section 5: circuit breaker thresholds. Section 6: prompt caching strategies (system prompt = 1hr TTL, student context = 5min TTL). Section 8: PII handling (Anthropic = trusted, Moonshot = untrusted). Section 9: observability metrics. |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Understand the per-model circuit breaker pattern. Your SDK calls MUST flow through this. Do not bypass it. |

---

## Architecture Requirements

### 1. Anthropic SDK Only — No Other Providers

Install the **official Anthropic .NET SDK** (`Anthropic` NuGet package). Implement `CallAnthropicAsync()` with real API calls.

For the other 3 stubs (`CallOpenAiAsync`, `CallGoogleAsync`, `CallAzureOpenAiAsync`):
- Replace mock responses with `throw new NotImplementedException($"{provider} provider not yet implemented. Configure Anthropic as your active provider.");`
- Do NOT leave them returning fake data silently — silent lies are worse than loud failures

### 2. Respect the Routing Config Contract

The routing config is LAW. Your code conforms to it — you do not modify it.

```yaml
# From routing-config.yaml — your primary model:
claude_sonnet_4_6:
  model_id: "claude-sonnet-4-6-20260215"
  provider: "anthropic"
  api_base_url: "https://api.anthropic.com/v1"
  context_window: 1000000
  max_output_tokens: 8192
  cost_per_input_mtok: 3.00
  cost_per_output_mtok: 15.00
```

- Use the `model_id` from config, not a hardcoded string
- Respect `temperature` and `max_tokens` per task type
- API key from `IConfiguration["Anthropic:ApiKey"]` — NEVER hardcoded

### 3. Structured Output

The LLM must return data that deserializes into `AiGeneratedQuestion`:
```csharp
public sealed record AiGeneratedQuestion(
    string Stem,
    IReadOnlyList<AiGeneratedOption> Options,
    string? Topic,
    int BloomsLevel,
    float Difficulty,
    string Explanation);  // ← THIS FIELD EXISTS but is discarded downstream (Task 1a fixes that)
```

Use Anthropic's tool_use or JSON mode for reliable structured output. Do NOT regex-parse free text.

### 4. Circuit Breaker Integration

All HTTP calls go through `LlmCircuitBreakerActor`. The circuit breaker already has per-model thresholds:
- Default: 5 failures → open for 60s, 2 successes to close
- Opus override: 3 failures → open for 120s, 3 successes to close

Your implementation wraps SDK calls so the circuit breaker can intercept failures. Do NOT add retry logic inside the service — that's the circuit breaker's job.

### 5. PII Handling

Anthropic is `is_trusted_provider: true` — no PII stripping needed. But design the interface to accept a `bool stripPii` parameter so when Kimi (untrusted) is added, the same interface works.

### 6. Prompt Caching

Implement Anthropic's prompt caching for system prompts:
```yaml
# From routing-config.yaml section 6:
system_prompt:
  ttl_seconds: 3600
  cache_control: { type: "ephemeral" }
```

Add `cache_control` blocks to system prompt messages. This reduces costs by 10x on cache hits ($0.30 vs $3.00 per MTok).

### 7. Quality Gate LLM Integration

After the SDK is working, update `QualityGateService` to call the LLM for the 3 stubbed dimensions:
- `FactualAccuracy` — LLM evaluates whether the question and correct answer are factually correct
- `LanguageQuality` — LLM evaluates grammar, clarity, age-appropriateness
- `PedagogicalQuality` — LLM evaluates Bloom's alignment, distractor quality, learning value

Use Haiku for quality gate evaluation (cheap, fast, sufficient for binary scoring).

### 8. Observability

Every LLM call MUST emit:
```
llm_request_duration_ms{model_id, task_type, status}  — histogram
llm_tokens_total{model_id, task_type, direction}       — counter (input/output)
llm_cost_usd{model_id, task_type, student_id_hash}     — counter
```

Use the existing metrics framework. If none exists, use `System.Diagnostics.Metrics`.

---

## What You Must NOT Do

| Prohibition | Why |
|-------------|-----|
| Do NOT create an "ILlmProvider" abstraction or factory pattern | The `AiGenerationService` switch on `AiProvider` is the correct abstraction. Adding a factory is premature — you're implementing ONE provider. |
| Do NOT implement streaming | Question generation is batch, not interactive. Streaming adds complexity for zero user benefit here. |
| Do NOT modify `routing-config.yaml` | It's the contract. Code conforms to config, not the reverse. |
| Do NOT add retry logic in the service | Circuit breaker handles retries. Redundant retry = exponential amplification of load during outages. |
| Do NOT hand-roll HTTP calls | Use the official SDK. Hand-rolled HTTP clients are a maintenance liability and miss SDK improvements (streaming, caching, auth rotation). |
| Do NOT log full prompts in production | `routing-config.yaml` section 9: `log_prompts: false`. Log token counts and latency, not content. |

---

## Verification Checklist

Every item must pass. No exceptions. No "we'll fix it later."

- [ ] `dotnet build` succeeds with zero warnings in the modified projects
- [ ] `dotnet test` passes — all existing tests still green
- [ ] Generate 3 questions via Admin API with a real Anthropic API key → all 3 have non-empty `Explanation` fields
- [ ] Quality gate runs with real LLM scores (verify they differ from the old defaults of 80/80/75)
- [ ] Simulate 5 consecutive API failures → verify circuit breaker opens → verify subsequent calls fail fast without hitting the API
- [ ] Verify `llm_request_duration_ms` and `llm_tokens_total` metrics are emitted (check via test or log output)
- [ ] Verify no API key appears in any log output, committed file, or test fixture
- [ ] Verify `CallOpenAiAsync`, `CallGoogleAsync`, `CallAzureOpenAiAsync` throw `NotImplementedException` (not return mock data)

---

## Definition of Done

This task is done when:
1. A real Anthropic API call generates questions with explanations
2. Quality gate uses real LLM evaluation for all 8 dimensions (no more defaults)
3. Circuit breaker is wired and tested
4. Observability metrics are emitted
5. Build and tests pass
6. No mock data is silently returned from any provider
