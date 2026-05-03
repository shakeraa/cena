# SAI-001: Persist L1 Explanations — Stop Discarding AI-Generated Explanations

**Priority:** P0 — foundation for ALL student AI interaction tiers
**Blocked by:** None
**Estimated effort:** 1 day
**Stack:** .NET 9, Marten event sourcing, Proto.Actor

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The `AiGenerationService` already generates explanations for every AI-created question. The `AiGeneratedQuestion` DTO includes a `string Explanation` field (line 91 of `AiGenerationService.cs`). However, this explanation is **discarded** — `CreateQuestionAsync()` in `QuestionBankService.cs` does not propagate it to any Marten event or the `QuestionState` aggregate.

`QuestionState` has 22 properties but NO explanation field. `PublishedQuestion` (the serving model in `QuestionPoolActor`) has 11 fields — also no explanation.

This task threads the explanation through the full event-sourcing pipeline: generation → event → aggregate state → serving pool → SignalR `AnswerEvaluated.explanation` response.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | `AiGeneratedQuestion.Explanation` (line 91) — the field that exists but is discarded |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | `CreateQuestionAsync()` — where explanation is lost. `CreateQuestionRequest` has no explanation field |
| `src/api/Cena.Admin.Api/QuestionBankDtos.cs` | `CreateQuestionRequest`, `QuestionBankDetailResponse` — need explanation fields |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | `QuestionAiGenerated_V1`, `QuestionAuthored_V1`, `QuestionIngested_V1` — event records |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | Aggregate state — Apply() methods, 22 fields, NO explanation |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | `PublishedQuestion` record (line 157-167) — in-memory serving model |
| `contracts/frontend/signalr-messages.ts` | `AnswerEvaluatedPayload.explanation` — already exists as placeholder string |

## Subtasks

### SAI-001.1: Add Explanation to Domain Events

**Files to modify:**
- `src/actors/Cena.Actors/Events/QuestionEvents.cs`

**Implementation:**

Add `string? Explanation` field to these event records:
- `QuestionAiGenerated_V1` — AI-generated questions carry explanation from LLM
- `QuestionAuthored_V1` — manually authored questions may include teacher-written explanation
- `QuestionIngested_V1` — ingested questions may have explanation extracted from source

**Backward compatibility:** Nullable field. Existing events deserialize with `null`. Marten handles this via System.Text.Json default null handling — no upcaster needed for additive nullable fields.

**Acceptance:**
- [ ] `QuestionAiGenerated_V1` has `string? Explanation` field
- [ ] `QuestionAuthored_V1` has `string? Explanation` field
- [ ] `QuestionIngested_V1` has `string? Explanation` field
- [ ] Existing event deserialization is unaffected (verify by loading existing aggregate)

---

### SAI-001.2: Add Explanation to QuestionState Aggregate

**Files to modify:**
- `src/actors/Cena.Actors/Questions/QuestionState.cs`

**Implementation:**

Add `string? Explanation` property to `QuestionState`. Update all three `Apply()` overloads:

```csharp
public string? Explanation { get; set; }

// In Apply(QuestionAiGenerated_V1 e):
Explanation = e.Explanation;

// In Apply(QuestionAuthored_V1 e):
Explanation = e.Explanation;

// In Apply(QuestionIngested_V1 e):
Explanation = e.Explanation;
```

Also add a dedicated update event for editing explanations post-creation:

```csharp
// In QuestionEvents.cs:
public sealed record QuestionExplanationUpdated_V1(
    string QuestionId,
    string Explanation,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

// In QuestionState.cs:
public void Apply(QuestionExplanationUpdated_V1 e)
{
    Explanation = e.Explanation;
    UpdatedAt = e.UpdatedAt;
    EventVersion++;
}
```

Register `QuestionExplanationUpdated_V1` in `MartenConfiguration.cs` under the Question events section.

**Acceptance:**
- [ ] `QuestionState.Explanation` populated from all three creation events
- [ ] `QuestionExplanationUpdated_V1` event registered in Marten config
- [ ] `Apply(QuestionExplanationUpdated_V1)` updates explanation and audit fields
- [ ] Snapshot includes explanation (Marten auto-handles via System.Text.Json)

---

### SAI-001.3: Thread Explanation Through API DTOs and Service

**Files to modify:**
- `src/api/Cena.Admin.Api/QuestionBankDtos.cs`
- `src/api/Cena.Admin.Api/QuestionBankService.cs`
- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs`

**Implementation:**

1. Add `string? Explanation` to `CreateQuestionRequest`
2. Add `string? Explanation` to `QuestionBankDetailResponse`
3. In `CreateQuestionAsync()`: propagate `request.Explanation` into the event being staged
4. In AI generation endpoint: when the frontend sends a creation request from an AI-generated question, include the explanation from `AiGeneratedQuestion.Explanation`
5. Add `PATCH /api/admin/questions/{id}/explanation` endpoint for post-creation explanation edits (stages `QuestionExplanationUpdated_V1`)

**Acceptance:**
- [ ] `CreateQuestionRequest.Explanation` accepted and persisted
- [ ] `QuestionBankDetailResponse.Explanation` returned in admin API
- [ ] AI-generated questions automatically have explanation persisted (no user action needed)
- [ ] PATCH endpoint validates: non-empty string, max 5000 chars, ModeratorOrAbove auth
- [ ] PATCH emits `QuestionExplanationUpdated_V1` via Marten

---

### SAI-001.4: Add Explanation to PublishedQuestion Serving Model

**Files to modify:**
- `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs`

**Implementation:**

Add `string? Explanation` to the `PublishedQuestion` record. Update the hydration logic in `InitializeAsync()` that loads questions from Marten:

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
    string? Explanation);  // NEW — L1 static explanation
```

Update the NATS `item.published` handler to include explanation when hot-reloading.

**Acceptance:**
- [ ] `PublishedQuestion.Explanation` populated during pool initialization
- [ ] Hot-reload on NATS `item.published` includes explanation
- [ ] Null explanation does not break question selection or serving
- [ ] Memory impact: ~200 bytes per question average. At 10K questions = ~2MB. Acceptable.

---

### SAI-001.5: Wire Explanation into AnswerEvaluated SignalR Response

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`
- `contracts/frontend/signalr-messages.ts` (if needed — field already exists)

**Implementation:**

In `LearningSessionActor.HandleEvaluateAnswer()`, after BKT update and error classification, populate the `explanation` field of `AnswerEvaluatedPayload`:

1. Look up current question's `PublishedQuestion.Explanation` from the question pool
2. If explanation exists → send it as L1 static explanation
3. If explanation is null → send empty string (future L2/L3 will fill this gap)

Do NOT add LLM calls here — this task is purely about threading persisted data. L2/L3 are SAI-003 and SAI-004.

**Acceptance:**
- [ ] `AnswerEvaluated.explanation` returns the persisted L1 explanation when available
- [ ] Null/missing explanation results in empty string, not null (client safety)
- [ ] No LLM calls added — purely data plumbing
- [ ] SignalR contract field `explanation: string` unchanged (already exists as placeholder)

---

## Backfill Plan

After this task ships, existing AI-generated questions have no persisted explanation. Two options:

1. **Re-generate**: Batch-call AI generation for existing published questions, stage `QuestionExplanationUpdated_V1` events. Depends on LLM ACL (LLM-001) being operational.
2. **On-demand**: When L2/L3 (SAI-003/SAI-004) generate an explanation for a question that has no L1, cache it back as L1 via `QuestionExplanationUpdated_V1`.

Decision: defer to SAI-003. Both approaches work. Don't block this task on backfill.

## Testing

```csharp
[Fact]
public async Task AiGeneratedQuestion_PersistsExplanation()
{
    var service = CreateQuestionBankService();
    var request = new CreateQuestionRequest(
        Stem: "What is 2+2?",
        Options: new[] { new CreateOptionRequest("A", "3", false, null), new CreateOptionRequest("B", "4", true, null) },
        Subject: "math", Topic: "arithmetic", Grade: 3,
        BloomsLevel: 1, Difficulty: 0.2f, ConceptIds: new[] { "add-basic" },
        Language: "he", Explanation: "2+2=4 because addition combines quantities.");

    var id = await service.CreateQuestionAsync(request, "admin-1");

    var state = await LoadAggregate<QuestionState>(id);
    Assert.Equal("2+2=4 because addition combines quantities.", state.Explanation);
}

[Fact]
public async Task PublishedQuestion_IncludesExplanation()
{
    // Create + publish a question with explanation
    // Load QuestionPool
    // Assert PublishedQuestion.Explanation is populated
}

[Fact]
public async Task AnswerEvaluated_ReturnsL1Explanation()
{
    // Start session, present question with explanation, submit wrong answer
    // Assert AnswerEvaluated.explanation == persisted explanation text
}
```
