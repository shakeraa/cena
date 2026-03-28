# TASK-SAI-03: L2 ErrorType-Based Explanation Cache

**Priority**: HIGH — biggest learning impact, ~80-90% cache hit rate
**Effort**: 3-5 days
**Depends on**: TASK-SAI-00 (real LLM calls)
**Track**: C (after Track A completes)

---

## Context

When a student answers incorrectly, they currently see either nothing or a static explanation that doesn't account for their specific misconception. The L2 cache generates and stores **misconception-specific explanations** keyed by `(questionId, errorType)`.

The `ErrorType` classification already exists in the contracts (`contracts/backend/actor-contracts.cs`). `BusConceptAttempt` already transmits `ErrorType` from the client. Most wrong answers for a given question cluster into 3-5 misconception types, so a small cache covers the vast majority of students.

### Existing Infrastructure

| Component | File | Status |
|-----------|------|--------|
| `ErrorType` field on `AttemptConcept` | `contracts/backend/actor-contracts.cs` | Exists |
| `BusConceptAttempt.ErrorType` | `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | Exists — transmitted |
| `ClassifiedErrorType` enum | `contracts/backend/actor-contracts.cs` | Exists — values defined |
| `AnswerEvaluated.explanation` SignalR field | `contracts/frontend/signalr-messages.ts` | Exists — placeholder |
| `ConfusionDetector` | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | Exists |
| `ScaffoldingService` | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Exists |
| `MethodologyResolver` (active methodology per concept) | Exists | Determines explanation tone |
| Redis (existing infra) | `src/infra/docker/` | Configured |

---

## Architecture

### 3-Layer Explanation Resolution

```text
Student answers wrong
  |
  v
L1: PublishedQuestion.Explanation (static, per-question)
  |-- if exists and no ErrorType available -> return L1
  v
L2: Redis explain:{questionId}:{errorType} (cached, per-misconception)
  |-- if cache hit -> return L2
  v
L3: On-demand LLM generation with full student context (TASK-SAI-04)
  |-- generate -> cache back to L2 -> return
```

This task implements L2. L3 is TASK-SAI-04.

### Redis Key Schema

```
explain:{questionId}:{errorType}:{methodology}
```

Example: `explain:q-abc123:sign_error:socratic`

- TTL: 30 days (misconception explanations don't stale quickly)
- Invalidation: when question version changes (new `QuestionStemEdited_V1` event)
- Value: JSON `{ text: string, generatedAt: string, modelId: string, tokenCount: number }`

### Methodology-Aware Tone

The explanation tone varies by the student's active methodology:

| Methodology | Tone | Example |
|-------------|------|---------|
| Socratic | Ask guiding questions, don't give answer | "What happens to the sign when you multiply two negative numbers? How does that apply here?" |
| WorkedExample | Show step-by-step solution | "Let's solve a similar problem step by step: First, identify..." |
| Feynman | Prompt student to find the gap | "Try explaining the relationship between these variables in your own words. Where does your reasoning break down?" |
| DrillAndPractice | Brief, direct correction | "The sign is negative because (-3)×(-2) = +6, not -6." |
| Default | Standard pedagogical explanation | "The correct approach is... The common mistake here is..." |

---

## Implementation

### Create: `src/actors/Cena.Actors/Services/ExplanationCacheService.cs`

```csharp
public interface IExplanationCacheService
{
    Task<CachedExplanation?> GetAsync(string questionId, string errorType, string methodology, CancellationToken ct);
    Task SetAsync(string questionId, string errorType, string methodology, CachedExplanation explanation, CancellationToken ct);
    Task InvalidateQuestionAsync(string questionId, CancellationToken ct);
}

public sealed record CachedExplanation(
    string Text,
    string ModelId,
    int TokenCount,
    DateTimeOffset GeneratedAt);
```

Use `IConnectionMultiplexer` (StackExchange.Redis — already in the solution) for cache operations.

### Create: `src/actors/Cena.Actors/Services/ExplanationGenerator.cs`

```csharp
public interface IExplanationGenerator
{
    Task<GeneratedExplanation> GenerateL2Async(ExplanationContext context, CancellationToken ct);
}

public sealed record ExplanationContext(
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    string ErrorType,
    string Methodology,
    string? DistractorRationale,   // for the chosen wrong option
    int BloomsLevel,
    string Subject,
    string Language);               // he/ar/en — generate in student's language
```

**Prompt construction** — build from `ScaffoldingService` level + methodology + error type:

```
System: You are an expert {subject} tutor for Israeli Bagrut preparation.
Generate an explanation for a student who made a {errorType} error.
Methodology: {methodology} — {methodology_instruction}
Language: {language}
Bloom's level: {bloomsLevel}

Question: {stem}
Correct answer: {correctAnswer}
Student's answer: {studentAnswer}
Why this is wrong: {distractorRationale}

Generate a concise explanation (2-4 sentences) appropriate for the methodology.
Use LaTeX for math expressions: $inline$ or $$block$$.
```

### Create: `src/actors/Cena.Actors/Services/ExplanationOrchestrator.cs`

```csharp
public interface IExplanationOrchestrator
{
    Task<string> ResolveExplanationAsync(ExplanationRequest request, CancellationToken ct);
}

public sealed record ExplanationRequest(
    PublishedQuestion Question,
    string StudentAnswer,
    string ErrorType,
    string Methodology,
    int BloomsLevel,
    string Language);
```

Resolution logic (L1 -> L2 -> L3 fallback chain):

```csharp
public async Task<string> ResolveExplanationAsync(ExplanationRequest req, CancellationToken ct)
{
    // L1: static explanation (always available as baseline)
    var l1 = req.Question.Explanation;

    // L2: cached misconception explanation
    var l2 = await _cache.GetAsync(req.Question.ItemId, req.ErrorType, req.Methodology, ct);
    if (l2 is not null) return l2.Text;

    // L3: generate on-demand (TASK-SAI-04 — for now, fall back to L1)
    // When TASK-SAI-04 is implemented, this becomes:
    // var l3 = await _generator.GenerateL2Async(context, ct);
    // await _cache.SetAsync(..., l3, ct);
    // return l3.Text;

    return l1 ?? "Review the question and consider which concept applies here.";
}
```

### Wire Into Answer Evaluation Flow

**Modify**: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

After evaluating an answer as incorrect, resolve the explanation:

```csharp
var explanation = await _explanationOrchestrator.ResolveExplanationAsync(
    new ExplanationRequest(question, cmd.Answer, cmd.ErrorType, activeMethodology, bloomsLevel, language), ct);

// Include in AnswerEvaluated SignalR response
```

### Cache Invalidation

**Modify**: Subscribe to `QuestionStemEdited_V1` events. When a question's stem changes, invalidate all L2 cache entries for that question:

```csharp
await _cache.InvalidateQuestionAsync(questionId, ct);
```

Use Redis `SCAN` with pattern `explain:{questionId}:*` and `DEL`.

---

## Batch Pre-Generation (Optional, Recommended)

At question publish time, pre-generate L2 explanations for the 3-5 most common error types per question. This warms the cache before students encounter the question.

**When**: `QuestionPublished_V1` event
**Trigger**: Background job that generates explanations for each `ErrorType` x each common methodology
**Cost**: One-time batch LLM cost per published question (~$0.02-0.05 per question)

---

## Coding Standards

- `ExplanationCacheService` is a thin Redis wrapper — no business logic.
- `ExplanationGenerator` handles prompt construction and LLM call — single responsibility.
- `ExplanationOrchestrator` owns the L1->L2->L3 resolution chain — it's the only entry point.
- All generated content must be in the student's `Language` (he/ar/en).
- Redis TTL = 30 days. Do NOT set TTL to infinity — explanations should eventually be regenerated as models improve.
- Prompts must never include student PII. The prompt contains question content and error type only.

---

## Acceptance Criteria

1. L2 cache stores explanations keyed by `(questionId, errorType, methodology)` in Redis
2. Cache hit returns explanation in <5ms
3. Cache miss falls back to L1 (static) or generic fallback (never returns empty)
4. Explanations are methodology-aware (Socratic asks questions, WorkedExample shows steps)
5. Explanations are language-aware (generated in he/ar/en per student preference)
6. Explanations use Markdown + LaTeX formatting
7. Cache invalidates when question stem is edited
8. `ExplanationOrchestrator.ResolveExplanationAsync` is the single entry point for all explanation resolution
9. Logging: cache hit/miss ratio per question (for monitoring warm-up effectiveness)
