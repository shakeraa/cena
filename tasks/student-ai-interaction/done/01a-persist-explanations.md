# Task 01a: Persist L1 Explanations

**Effort**: 1 day | **Track**: A | **Depends on**: Nothing | **Blocks**: 02 (L2 cache reads L1 as fallback)

---

## Context

You are working on the **Cena Platform** — an event-sourced .NET 8 adaptive learning system using Proto.Actor, Marten (PostgreSQL), and SignalR. Questions are created through 3 paths: authored, ingested, and AI-generated. Each path emits a domain event stored in Marten.

The AI generation path produces an `AiGeneratedQuestion` DTO that includes an `Explanation` field (line 91 of `AiGenerationService.cs`). This explanation is **generated but then discarded** — the `QuestionAiGenerated_V1` event does not carry it, so it never reaches the aggregate state or the serving layer.

The SignalR response `AnswerEvaluated.explanation` is currently a placeholder string. Persisting L1 explanations is the single cheapest win in the entire student-AI interaction plan.

---

## Objective

Thread the `Explanation` from `AiGeneratedQuestion` through the event → aggregate → serving pipeline so it reaches students via SignalR.

---

## Files to Read First (MANDATORY)

| File | Path | Lines | Key Structure |
|------|------|-------|---------------|
| QuestionEvents.cs | `src/actors/Cena.Actors/Events/QuestionEvents.cs` | 158 | `QuestionAiGenerated_V1` — sealed record at line 57. Currently has 15 fields. NO `Explanation` field. |
| QuestionState.cs | `src/actors/Cena.Actors/Questions/QuestionState.cs` | 283 | `AiGenerationState` record at line 28. 6 fields: PromptText, ModelId, ModelTemperature, RawModelOutput, RequestedBy, GeneratedAt. NO `Explanation`. |
| QuestionPoolActor.cs | `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | 167 | `PublishedQuestion` record at line 157. 10 fields. NO `Explanation`. Used for in-memory question selection. |
| QuestionBankService.cs | `src/api/Cena.Admin.Api/QuestionBankService.cs` | ? | `CreateQuestionAsync()` — where explanation is currently dropped |
| AiGenerationService.cs | `src/api/Cena.Admin.Api/AiGenerationService.cs` | 395 | `AiGeneratedQuestion` DTO at line 85 — ALREADY HAS `Explanation` at line 91 |
| signalr-messages.ts | `contracts/frontend/signalr-messages.ts` | 749 | `AnswerEvaluated.explanation` at ~line 339 — already a placeholder field |

---

## Exact 6-Step Implementation

### Step 1: Add `string? Explanation` to `QuestionAiGenerated_V1`

In `QuestionEvents.cs`, add `string? Explanation` as the **last parameter before `DateTimeOffset Timestamp`**. This maintains positional record compatibility.

```csharp
public sealed record QuestionAiGenerated_V1(
    string QuestionId,
    string Stem,
    string StemHtml,
    IReadOnlyList<QuestionOptionData> Options,
    string Subject,
    string Topic,
    string Grade,
    int BloomsLevel,
    float Difficulty,
    IReadOnlyList<string> ConceptIds,
    string Language,
    string PromptText,
    string ModelId,
    float ModelTemperature,
    string RawModelOutput,
    string RequestedBy,
    string? Explanation,           // <── NEW: AI-generated explanation text
    DateTimeOffset Timestamp);
```

**Why nullable**: Existing events in the Marten store won't have this field. Marten deserializes missing JSON fields as `null`. This is safe, standard, and documented in Marten's event versioning docs.

### Step 2: Add `string? Explanation` to `AiGenerationState`

In `QuestionState.cs`:

```csharp
public sealed record AiGenerationState(
    string PromptText,
    string ModelId,
    float ModelTemperature,
    string RawModelOutput,
    string RequestedBy,
    DateTimeOffset GeneratedAt,
    string? Explanation);          // <── NEW
```

### Step 3: Update `QuestionState.Apply(QuestionAiGenerated_V1)`

Find the `Apply` method that handles `QuestionAiGenerated_V1` and include `Explanation` in the `AiGenerationState` construction.

### Step 4: Add `string? Explanation` to `PublishedQuestion`

In `QuestionPoolActor.cs`:

```csharp
public sealed record PublishedQuestion(
    string ItemId,
    string Subject,
    IReadOnlyList<string> ConceptIds,
    int BloomLevel,
    float Difficulty,
    int QualityScore,
    string Language,
    string StemPreview,
    string SourceType,
    DateTimeOffset PublishedAt,
    string? Explanation);          // <── NEW
```

### Step 5: Hydrate in `QuestionPoolActor.InitializeAsync()`

The Marten read-model query that populates `PublishedQuestion` must include the explanation. Check how existing fields are mapped from the aggregate and add `Explanation` to the projection.

### Step 6: Update `QuestionBankService.CreateQuestionAsync()`

Pass `Explanation` from `AiGeneratedQuestion` DTO through to the `QuestionAiGenerated_V1` event constructor. This is where the explanation is currently dropped.

---

## Architectural Requirements

- **Event versioning**: This is an additive change (new nullable field). Do NOT create `QuestionAiGenerated_V2`. Marten handles missing fields as null.
- **Backward compatibility**: All existing questions have `Explanation = null`. UI must handle gracefully.
- **No UI changes**: Backend only. SignalR contract already has the placeholder field.
- **No new files**: This modifies existing records and methods only.

---

## What NOT to Do

- Do NOT create a V2 event — additive nullable on records is safe in Marten
- Do NOT modify `QuestionAuthored_V1` or `QuestionIngested_V1` — those paths don't have AI explanations
- Do NOT add explanation to Admin API response DTOs yet — separate concern
- Do NOT add tests for the explanation content quality — that's Task 00's quality gate

---

## Verification Checklist

- [ ] Create a question via AI generation → `QuestionAiGenerated_V1` event in PostgreSQL contains explanation text
- [ ] Query aggregate → `QuestionState.AiGeneration.Explanation` is populated
- [ ] `PublishedQuestion` in `QuestionPoolActor` carries the explanation
- [ ] Load an existing (pre-change) question → `Explanation = null`, no crash
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
