# Task 02: ErrorType-Based L2 Explanation Cache

**Effort**: 3-5 days | **Track**: C | **Depends on**: Task 00 (real LLM calls) | **Blocks**: 03, 04

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor, Marten, Redis, SignalR. After Task 00, the system has real Anthropic SDK calls. After Task 01a, published questions carry L1 static explanations.

This task builds the **L2 explanation cache**: when a student answers incorrectly, classify the error type, then serve a cached explanation for that `(questionId, errorType, language)` tuple. On cache miss, generate via LLM and cache. On cache hit, return immediately — no LLM call.

**Why this matters**: Most students make the same 3-5 categories of mistakes per question. After initial generation, ~80-90% of explanations are cache hits. This makes personalized explanations affordable at scale (~$0.20/student/month).

The existing `ConceptMasteryState.RecentErrors[]` already tracks a 10-element circular buffer of `ErrorType` enum per concept. The routing config maps `error_classification` → Kimi K2 (6.7× cheaper than Sonnet) with Haiku fallback.

---

## Objective

Build `ExplanationCacheService` with Redis-backed L2 cache. Classify errors, generate methodology-aware explanations, cache by (questionId, errorType, language).

---

## Files to Read First (MANDATORY)

| File | Path | Why |
|------|------|-----|
| AiGenerationService | `src/api/Cena.Admin.Api/AiGenerationService.cs` | Now has real LLM calls (Task 00). Understand the Anthropic integration pattern. |
| signalr-messages.ts | `contracts/frontend/signalr-messages.ts` | `AnswerEvaluated.explanation` at ~line 339. Where the explanation is delivered. |
| LearningSessionActor | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Where answer evaluation happens — integration point. |
| routing-config.yaml | `contracts/llm/routing-config.yaml` | `error_classification` task type → Kimi K2 primary, Haiku fallback. `answer_evaluation` → Sonnet 4.6. |
| LlmCircuitBreakerActor | `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | All LLM calls flow through this. |

---

## Implementation

### 1. Error Classification

Before generating an explanation, classify the error. The routing config maps `error_classification` to Kimi K2 (cheaper). Until Kimi is implemented, use Haiku fallback.

Domain-specific error types:
```csharp
public enum ExplanationErrorType
{
    ConceptualMisunderstanding,  // Wrong mental model
    ProceduralError,             // Right concept, wrong execution
    CarelessMistake,             // Understood but sloppy
    Guessing,                    // No evidence of understanding
    PartialUnderstanding         // Close but missing key step
}
```

Classification input: question stem + correct answer + student answer + distractor rationales.

### 2. Cache Architecture

**Key format**: `explain:{questionId}:{errorType}:{language}` in Redis.
**TTL**: 30 days (explanations don't expire quickly).
**Value**: JSON-serialized explanation text (Markdown+LaTeX).

```csharp
public interface IExplanationCacheService
{
    Task<string?> GetCachedExplanation(string questionId, ExplanationErrorType errorType, string language);
    Task CacheExplanation(string questionId, ExplanationErrorType errorType, string language, string explanation);
    Task<string> GetOrGenerateExplanation(ExplanationContext context);
}
```

### 3. Cache Miss Flow

1. Student answers incorrectly
2. Classify error type (LLM call via Haiku — cheap, fast)
3. Check Redis: `explain:{questionId}:{errorType}:{language}`
4. Cache miss → Generate explanation via Sonnet 4.6 (`answer_evaluation` task type)
5. Store in Redis with 30-day TTL
6. Return to student via `AnswerEvaluated.explanation`

### 4. Cache Hit Flow

1. Student answers incorrectly
2. Classify error type
3. Check Redis → **HIT**
4. Return cached explanation immediately — zero LLM cost

### 5. File Location

`src/actors/Cena.Actors/Services/ExplanationCacheService.cs` — it's a domain service, not an API concern.

### 6. LearningSessionActor Integration

In `LearningSessionActor`, after answer evaluation:
```
if (incorrect) {
    errorType = await _errorClassifier.Classify(question, studentAnswer, correctAnswer);
    explanation = await _explanationCache.GetOrGenerateExplanation(new ExplanationContext(
        QuestionId, errorType, Language, Question, StudentAnswer, CorrectAnswer));
    // Deliver via AnswerEvaluated SignalR event
}
```

---

## What NOT to Do

- Do NOT implement Kimi API client — use Haiku fallback for error classification
- Do NOT use in-memory cache — Redis is correct for shared state across actor instances
- Do NOT cache personalized (L3) explanations — only generic per-errortype. L3 is Task 03.
- Do NOT create a new actor for caching — this is a stateless service that uses Redis
- Do NOT bypass the circuit breaker — all LLM calls go through `LlmCircuitBreakerActor`

---

## Verification Checklist

- [ ] Submit wrong answer → error classified → explanation generated → cached in Redis
- [ ] Submit same wrong-answer-type for same question → cache hit (verify no LLM call via metrics)
- [ ] Different error types for same question → different cached explanations
- [ ] Redis key format matches `explain:{qId}:{errorType}:{lang}`
- [ ] TTL is 30 days
- [ ] Circuit breaker respected for all LLM calls
- [ ] Explanation includes Markdown+LaTeX formatting
- [ ] Hebrew and Arabic explanations cached separately
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
