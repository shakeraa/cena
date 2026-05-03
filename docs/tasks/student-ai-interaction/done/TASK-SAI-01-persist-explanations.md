# TASK-SAI-01: Persist L1 Explanations (Stop Discarding AI-Generated Content)

**Priority**: HIGH — cheapest win, unlocks explanation delivery
**Effort**: 1 day
**Depends on**: Nothing
**Track**: A (parallel with TASK-SAI-00, TASK-SAI-02)

---

## Context

When questions are AI-generated, the `AiGeneratedQuestion` DTO includes an `Explanation` field returned from the LLM. However, in `QuestionBankService.CreateQuestionAsync()` (`src/api/Cena.Admin.Api/QuestionBankService.cs`), this field is **silently discarded** — never persisted to the `QuestionAiGenerated_V1` event or the `QuestionState` aggregate.

The `QuestionState` aggregate (`src/actors/Cena.Actors/Questions/QuestionState.cs`) has 22+ properties but NO `Explanation` field.

The SignalR contract (`contracts/frontend/signalr-messages.ts`) already has an `explanation: string` field on `AnswerEvaluated` — currently a placeholder empty string.

**This is a 6-step fix. The data already exists; we just need to stop throwing it away.**

---

## Implementation Steps

### Step 1: Add `Explanation` to `QuestionAiGenerated_V1`

**File**: `src/actors/Cena.Actors/Events/QuestionEvents.cs`

Add a new field to the existing record. Because Marten uses JSON serialization, adding a nullable field is backwards-compatible with existing events (they'll deserialize as `null`).

```csharp
public sealed record QuestionAiGenerated_V1(
    // ... all existing fields ...
    string? Explanation,     // ADD — AI-generated explanation text (Markdown+LaTeX)
    // ... Timestamp last ...
    DateTimeOffset Timestamp);
```

**WARNING**: Do NOT change the order of existing fields. Add `Explanation` before `Timestamp` to maintain readability, but Marten serializes by name, not position — order doesn't affect deserialization.

### Step 2: Add `Explanation` to `QuestionState` + Apply method

**File**: `src/actors/Cena.Actors/Questions/QuestionState.cs`

Add property to aggregate:
```csharp
public string? Explanation { get; private set; }
```

Update the `Apply(QuestionAiGenerated_V1 e)` method to set it:
```csharp
Explanation = e.Explanation;
```

Also add an `ExplanationEdited_V1` event for manually editing explanations on any question (not just AI-generated):

**File**: `src/actors/Cena.Actors/Events/QuestionEvents.cs`
```csharp
public sealed record ExplanationEdited_V1(
    string QuestionId,
    string? PreviousExplanation,
    string NewExplanation,
    string EditedBy,
    DateTimeOffset Timestamp);
```

### Step 3: Add `Explanation` to `PublishedQuestion` and `QuestionReadModel`

**File**: `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs`

Add to the `PublishedQuestion` record:
```csharp
public sealed record PublishedQuestion(
    // ... existing fields ...
    string? Explanation);     // ADD
```

**File**: `src/actors/Cena.Actors/Questions/QuestionReadModel.cs`

Add to read model:
```csharp
public string? Explanation { get; set; }
```

**File**: `src/actors/Cena.Actors/Questions/QuestionListProjection.cs`

Update projection `Create` and `Apply` methods to populate `Explanation`.

### Step 4: Hydrate in `QuestionPoolActor.InitializeAsync()`

**File**: `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs`

In the query that loads published questions into the in-memory pool, include the `Explanation` field from the read model. Map it into `PublishedQuestion`.

### Step 5: Wire `CreateQuestionRequest` to propagate `Explanation`

**File**: `src/api/Cena.Admin.Api/QuestionBankDtos.cs`

Add to `CreateQuestionRequest`:
```csharp
public string? Explanation { get; init; }
```

**File**: `src/api/Cena.Admin.Api/QuestionBankService.cs`

In `CreateQuestionAsync()`, when the source type is `ai-generated`, pass `request.Explanation` into the `QuestionAiGenerated_V1` event constructor.

For authored/ingested questions, also accept and store `Explanation` via a separate event or inline field.

### Step 6: Wire explanation delivery through SignalR

**File**: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` (or wherever `AnswerEvaluated` is constructed)

When constructing the answer evaluation response, include the explanation from the question:
```csharp
// In the response that gets serialized to SignalR AnswerEvaluated:
explanation = publishedQuestion.Explanation ?? ""
```

---

## What NOT To Do

- Do NOT add explanation fields to `QuestionAuthored_V1` or `QuestionIngested_V1`. Use the separate `ExplanationEdited_V1` event instead — it applies to any question regardless of creation path.
- Do NOT change the `AiGenerationService` — it already returns explanations. The fix is in the consumer (`QuestionBankService`), not the producer.
- Do NOT make `Explanation` a required field anywhere. It is always nullable. Hundreds of existing questions have no explanation and that's fine.

---

## Coding Standards

- Event records are immutable `sealed record` types with `_V1` suffix.
- Projection updates must be synchronous (Marten inline projection — existing pattern).
- The `PublishedQuestion` record must remain a flat, allocation-minimal type — it lives in a hot in-memory pool.
- Test: write a unit test that creates a question via `QuestionAiGenerated_V1` with an explanation, rebuilds the aggregate, and asserts `Explanation` is non-null.

---

## Acceptance Criteria

1. AI-generated questions persist their `Explanation` through the event stream
2. `QuestionState.Explanation` is populated after aggregate replay
3. `PublishedQuestion.Explanation` is available in the in-memory pool
4. `QuestionReadModel.Explanation` is queryable for Admin UI
5. `ExplanationEdited_V1` allows manually adding/editing explanations on any question
6. Existing questions without explanations continue to work (null is fine)
7. SignalR `AnswerEvaluated` message includes the explanation when available
