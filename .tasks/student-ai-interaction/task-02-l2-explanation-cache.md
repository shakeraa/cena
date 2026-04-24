# Task 02: ErrorType-Based L2 Explanation Cache

**Track**: C
**Effort**: 3-5 days
**Depends on**: Task 00 (real LLM calls)
**Blocks**: Task 03, Task 04

---

## System Context

Cena is an event-sourced .NET platform using Proto.Actor, Marten/PostgreSQL, Redis, and NATS JetStream. Students receive questions via SignalR WebSocket. When a student answers incorrectly, `AnswerEvaluated.explanation` is sent back — currently a placeholder.

After Task 00, the system has real Anthropic SDK calls. After Task 01a, L1 static explanations exist per question. This task builds the L2 layer: a Redis-cached explanation keyed by `(questionId, errorType, language)`. When a student gets a question wrong, the system classifies the error type, checks the cache, and either serves a cached explanation or generates one via LLM and caches it.

The system already classifies errors into 6 types stored in `ConceptMasteryState.RecentErrors[]`: Procedural, Conceptual, Motivational, Careless, Systematic, Transfer. These are recorded per attempt via `BusConceptAttempt`. The `AnswerEvaluatedPayload.errorType` field in the SignalR contract (line 328 of `signalr-messages.ts`) already transmits `ErrorType | null`.

The routing config maps `error_classification` task type to Kimi K2 (6.7x cheaper than Sonnet). Since Kimi isn't implemented yet, use Haiku as fallback.

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 327-335 | `CallAnthropicAsync()` — now has real SDK call (from Task 00) |
| `contracts/llm/routing-config.yaml` | 111-201 | Task type mappings. Find `error_classification` and `answer_evaluation` — note model, temperature, max_tokens |
| `contracts/frontend/signalr-messages.ts` | 320-339 | `AnswerEvaluatedPayload` — `explanation: string`, `errorType: ErrorType | null` |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Full | Where answer evaluation happens — find the code path that sends `AnswerEvaluated` to the student |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | Find `RecentErrors` | Circular buffer of last 10 ErrorType values per concept |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | 34-59 | Circuit breaker config — all LLM calls must flow through this |
| Redis connection setup | Find in `Program.cs` or service registration | Verify Redis is already configured and available |

---

## Implementation Requirements

### 1. Create `ExplanationCacheService`

**Location**: `src/actors/Cena.Actors/Services/ExplanationCacheService.cs`

This is a domain service, not an API concern. It manages the L2 cache layer.

```csharp
public interface IExplanationCacheService
{
    /// Returns cached explanation or generates + caches one.
    /// Falls back to L1 (static explanation) if LLM fails.
    Task<string> GetOrGenerateExplanationAsync(
        ExplanationRequest request,
        CancellationToken ct = default);
}

public sealed record ExplanationRequest(
    string QuestionId,
    string QuestionStem,        // for prompt context
    string CorrectAnswer,
    string StudentAnswer,
    string ErrorType,           // Procedural, Conceptual, Motivational, Careless, Systematic, Transfer
    string Language,            // he, ar, en
    string? L1Explanation       // fallback from Task 01a
);
```

### 2. Cache Key and TTL

- **Key**: `explain:{questionId}:{errorType}:{language}` in Redis
- **TTL**: 30 days (`TimeSpan.FromDays(30)`)
- **Value**: plain string (Markdown + LaTeX)
- **Invalidation**: when question is re-versioned (question version event clears all keys matching `explain:{questionId}:*`)

### 3. Cache Hit Flow (Hot Path)

```
1. Receive wrong answer from LearningSessionActor
2. Determine ErrorType (from existing classification logic)
3. Build cache key: explain:{questionId}:{errorType}:{language}
4. Redis GET → if hit, return immediately (no LLM call)
```

This is the cost optimization that makes the system affordable. Expected hit rate: 80-90% after warm-up.

### 4. Cache Miss Flow

```
1. Redis GET → miss
2. Build LLM prompt:
   - Question stem + correct answer + student answer + error type
   - Instruction: "Generate an explanation for a student who made a {errorType} error on this question."
   - Language instruction: "Respond in {language}."
   - Output format: Markdown with LaTeX math ($$...$$ for display, $...$ for inline)
3. Call Anthropic via circuit breaker (use `answer_evaluation` task config from routing-config.yaml)
4. On success: store in Redis, return to student
5. On LLM failure: fall back to L1 explanation (static). If L1 is null, return generic message.
```

### 5. Error Type Classification

The system already classifies errors into 6 types. Verify where this happens in the answer evaluation flow. The classification must happen BEFORE the explanation lookup. If the existing classification is insufficient for prompt quality, enhance it — but do NOT change the ErrorType enum without understanding all consumers.

The routing config maps `error_classification` to Kimi K2 (not yet implemented). For now, use the existing rule-based classification. If no rule-based classification exists and only LLM classification was planned, use Haiku as a cheap classifier.

### 6. Integration with `LearningSessionActor`

Wire `ExplanationCacheService` into the answer evaluation flow:

```
LearningSessionActor receives SubmitAnswer
  → Evaluates correctness
  → If incorrect:
      → Classify error type (existing or new)
      → Call ExplanationCacheService.GetOrGenerateExplanationAsync()
      → Send AnswerEvaluated with real explanation
  → If correct:
      → Send AnswerEvaluated with brief confirmation (no L2 needed)
```

### 7. Resilience

- All LLM calls through `LlmCircuitBreakerActor` — RequestPermission before, ReportSuccess/ReportFailure after
- Redis failure: fall through to L1, then generic message. Log the Redis error. Do NOT let Redis failure block the student.
- LLM timeout: 10 seconds max (from routing config). Fall through to L1.

---

## What NOT to Do

- Do NOT implement Kimi API client — use Haiku fallback for classification until Kimi is done
- Do NOT use in-memory cache — Redis is correct for shared state across actor instances
- Do NOT cache personalized explanations — only cache generic per-errortype. L3 personalization is Task 03.
- Do NOT change the `ErrorType` enum without reading all consumers
- Do NOT add a new SignalR event — `AnswerEvaluated.explanation` is the delivery channel
- Do NOT create a separate actor for caching — this is a service, injected into the actor

---

## Verification Checklist

- [ ] Submit wrong answer → error classification runs → explanation generated and cached in Redis
- [ ] Submit same error type for same question → cache hit (verify NO LLM call via logs)
- [ ] Different error types for same question → different cached explanations
- [ ] Redis down → graceful fallback to L1 explanation (no crash, no hang)
- [ ] LLM circuit breaker open → fallback to L1 explanation
- [ ] Explanation in Hebrew → RTL Markdown with correct LaTeX rendering
- [ ] Explanation in Arabic → same RTL handling
- [ ] Cache key matches pattern `explain:{questionId}:{errorType}:{language}`
- [ ] TTL is 30 days on cached entries
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
