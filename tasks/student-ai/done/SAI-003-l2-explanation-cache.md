# SAI-003: L2 ErrorType-Based Explanation Cache

**Priority:** P1 — first LLM-powered student-facing feature
**Blocked by:** SAI-001 (L1 explanations as fallback), LLM-001 (ACL scaffold for real API calls)
**Estimated effort:** 3-5 days
**Stack:** .NET 9, Redis (StackExchange.Redis), Proto.Actor, LLM ACL (gRPC)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code.

## Context

When a student answers incorrectly, the system already classifies the error into 6 `ErrorType` categories (Procedural, Conceptual, Motivational, Careless, Systematic, Transfer) via the mastery pipeline. For a given question, most wrong answers cluster into 2-3 ErrorType categories.

This task builds a Redis-backed explanation cache keyed on `(questionId, ErrorType)`. On first occurrence, the LLM ACL generates an explanation tailored to that misconception cluster. Subsequent students hitting the same `(question, ErrorType)` get the cached explanation instantly.

Expected cache hit rate: 80-90% after warm-up.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Mastery/MasteryEnums.cs` | `ErrorType` enum: None, Procedural, Conceptual, Motivational, Careless, Systematic, Transfer |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | `RecentErrors[]`, `QualityQuadrant`, `BloomLevel` |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Scaffolding level determines explanation depth |
| `src/actors/Cena.Actors/Methodology/MethodologyResolver.cs` | Active methodology determines explanation tone |
| `src/actors/Cena.Actors/Messaging/MessagingRedisKeys.cs` | Redis key naming convention: `cena:` prefix |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Circuit breaker — must check before LLM calls |
| `contracts/llm/routing-config.yaml` | Model routing config — Claude Sonnet 4.6 for tutoring |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Where explanation is delivered via `AnswerEvaluated` |

## Subtasks

### SAI-003.1: Redis Explanation Cache Service

**Files to create:**
- `src/actors/Cena.Actors/Explanations/IExplanationCache.cs`
- `src/actors/Cena.Actors/Explanations/RedisExplanationCache.cs`
- `src/actors/Cena.Actors/Explanations/ExplanationRedisKeys.cs`

**Implementation:**

```csharp
public interface IExplanationCache
{
    Task<string?> GetL2Async(string questionId, ErrorType errorType, CancellationToken ct);
    Task SetL2Async(string questionId, ErrorType errorType, string explanation, CancellationToken ct);
    Task InvalidateAsync(string questionId, CancellationToken ct);
}
```

Redis key pattern: `cena:explain:{questionId}:{errorType}` (follows `MessagingRedisKeys` convention).
TTL: 30 days. Invalidate on question version change (listen for `QuestionStemEdited_V1`, `QuestionOptionChanged_V1` via NATS).

**Acceptance:**
- [ ] Redis key pattern follows `cena:` convention
- [ ] 30-day TTL set via `KeyExpireAsync`
- [ ] `GetL2Async` returns null on miss (not empty string)
- [ ] `InvalidateAsync` deletes all ErrorType variants for a question (`cena:explain:{qId}:*`)
- [ ] Circuit breaker for Redis checked before operations (fall through to L1/L3 on Redis failure)
- [ ] Registered as singleton in DI

---

### SAI-003.2: Explanation Generation via LLM ACL

**Files to create:**
- `src/actors/Cena.Actors/Explanations/IExplanationGenerator.cs`
- `src/actors/Cena.Actors/Explanations/LlmExplanationGenerator.cs`

**Implementation:**

Calls the LLM ACL (gRPC, defined in LLM-001) to generate an explanation for a specific `(question, ErrorType)` pair:

```csharp
public interface IExplanationGenerator
{
    Task<string?> GenerateL2Async(ExplanationRequest request, CancellationToken ct);
}

public sealed record ExplanationRequest(
    string QuestionId,
    string QuestionStem,
    string CorrectAnswer,
    ErrorType ErrorType,
    ScaffoldingLevel ScaffoldingLevel,   // Full, Partial, HintsOnly, None
    Methodology ActiveMethodology,        // Determines tone
    int BloomLevel);                      // Determines depth
```

**Prompt construction rules** (methodology-aware):
- Socratic: "Ask a guiding question that leads the student to discover the error. Do NOT give the answer."
- Feynman: "Ask the student to explain their reasoning. Point out where their logic breaks."
- WorkedExample: "Show the correct solution step by step."
- Default: "Explain why the correct answer is X, addressing the specific {ErrorType} error pattern."

**Scaffolding depth rules**:
- Full: Complete worked example with all steps
- Partial: Point out the error step, show the fix
- HintsOnly: One-sentence pointer to the misconception
- None: Empty (student doesn't need explanation at this mastery level)

**Acceptance:**
- [ ] Calls LLM ACL via gRPC (not HTTP, not direct SDK calls)
- [ ] Checks `LlmCircuitBreakerActor` before calling (RequestPermission → AllowRequest/RejectRequest)
- [ ] Reports success/failure back to circuit breaker
- [ ] Returns null on circuit breaker rejection or LLM failure (graceful degradation to L1)
- [ ] Prompt includes methodology constraint and scaffolding depth
- [ ] Response capped at 2000 chars
- [ ] OpenTelemetry span: `cena.explanation.generate_l2`
- [ ] Counter: `cena.explanation.l2_generated_total` with `error_type` and `methodology` tags

---

### SAI-003.3: L2 Explanation Resolution Pipeline

**Files to create:**
- `src/actors/Cena.Actors/Explanations/ExplanationResolver.cs`

**Implementation:**

The resolver orchestrates the 3-layer explanation lookup:

```csharp
public sealed class ExplanationResolver
{
    // Layered resolution: L2 cache → L1 static → L3 generation → fallback
    public async Task<ExplanationResult> ResolveAsync(ExplanationContext context, CancellationToken ct)
    {
        // 1. Try L2 cache (Redis)
        var l2 = await _cache.GetL2Async(context.QuestionId, context.ErrorType, ct);
        if (l2 is not null) return new ExplanationResult(l2, ExplanationSource.L2Cache);

        // 2. Try L1 static (from PublishedQuestion.Explanation)
        if (context.StaticExplanation is not null)
            return new ExplanationResult(context.StaticExplanation, ExplanationSource.L1Static);

        // 3. Generate via LLM (fire-and-cache for future students)
        var generated = await _generator.GenerateL2Async(context.ToRequest(), ct);
        if (generated is not null)
        {
            // Cache for future students (fire-and-forget, don't block response)
            _ = _cache.SetL2Async(context.QuestionId, context.ErrorType, generated, ct);
            return new ExplanationResult(generated, ExplanationSource.L2Generated);
        }

        // 4. Fallback: empty (no explanation available)
        return ExplanationResult.Empty;
    }
}

public sealed record ExplanationResult(string Text, ExplanationSource Source);
public enum ExplanationSource { L1Static, L2Cache, L2Generated, L3Personalized, None }
```

**Acceptance:**
- [ ] L2 cache checked first (sub-millisecond path)
- [ ] L1 fallback when L2 misses and no LLM call needed
- [ ] LLM generation only on L2 miss AND L1 miss
- [ ] Generated explanation cached back to L2 asynchronously (fire-and-forget)
- [ ] Circuit breaker failure degrades gracefully (no exception propagation)
- [ ] Metrics: `cena.explanation.resolved_total` with `source` tag (l1/l2_cache/l2_generated/none)

---

### SAI-003.4: Wire ExplanationResolver into LearningSessionActor

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Implementation:**

In `HandleEvaluateAnswer()`, after BKT update and error classification, replace the placeholder explanation with the resolved explanation:

1. Build `ExplanationContext` from current question, student state, scaffolding level, methodology
2. Call `ExplanationResolver.ResolveAsync(context, ct)`
3. Populate `AnswerEvaluated.explanation` with the resolved text
4. Include `ExplanationSource` in telemetry (not in client payload)

**Rate limiting:** Max 3 LLM generation calls per student per minute (tracked in-session). If exceeded, skip L3 generation and fall back to L1.

**Acceptance:**
- [ ] `AnswerEvaluated.explanation` populated from resolver (not placeholder)
- [ ] Rate limit: 3 LLM calls/student/minute enforced in session state
- [ ] Source tracked in OpenTelemetry span attributes
- [ ] No impact on answer evaluation latency when L2 hits (sub-ms Redis)
- [ ] L2 miss + LLM generation adds ~500ms-2s (acceptable — happens once per unique question×ErrorType)

---

### SAI-003.5: Cache Invalidation on Question Edit

**Files to modify:**
- `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` (or create a dedicated NATS subscriber)

**Implementation:**

When a question is edited (`QuestionStemEdited_V1` or `QuestionOptionChanged_V1` published to NATS), invalidate all L2 cache entries for that question:

```csharp
await _explanationCache.InvalidateAsync(questionId, ct);
```

This is critical — stale explanations after question edits would confuse students.

**Acceptance:**
- [ ] Question stem edit invalidates L2 cache for all ErrorTypes
- [ ] Question option change invalidates L2 cache
- [ ] Explanation-only edit (`QuestionExplanationUpdated_V1` from SAI-001) does NOT invalidate L2 (L2 is ErrorType-specific, independent of L1)
- [ ] Invalidation is idempotent (safe to call multiple times)

---

## Testing

```csharp
[Fact]
public async Task L2Cache_HitOnSecondStudent()
{
    // Student A: wrong answer, ErrorType=Conceptual → L2 miss → LLM generates → cached
    // Student B: same question, same ErrorType → L2 HIT → no LLM call
    var resolver = CreateResolver(withCachePreloaded: false);
    var ctx = CreateContext(questionId: "q-1", errorType: ErrorType.Conceptual);

    var first = await resolver.ResolveAsync(ctx, CancellationToken.None);
    Assert.Equal(ExplanationSource.L2Generated, first.Source);

    var second = await resolver.ResolveAsync(ctx, CancellationToken.None);
    Assert.Equal(ExplanationSource.L2Cache, second.Source);
}

[Fact]
public async Task CircuitBreakerOpen_FallsBackToL1()
{
    // Open circuit breaker → LLM calls rejected
    // Resolver should fall back to L1 static explanation
}

[Fact]
public async Task RateLimit_SkipsLlmAfter3Calls()
{
    // Generate 3 L2 explanations (3 different questions)
    // 4th call should skip LLM and fall back to L1
}
```
