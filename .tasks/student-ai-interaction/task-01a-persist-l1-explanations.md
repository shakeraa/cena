# Task 01a: Persist L1 Explanations

**Track**: A (parallel with Task 00)
**Effort**: 1 day
**Depends on**: Nothing
**Blocks**: Task 02

---

## System Context

Cena is an event-sourced .NET educational platform. Questions are created through an AI generation pipeline: `AiGenerationService` produces an `AiGeneratedQuestion` DTO that includes an `Explanation` field (line 91). However, `QuestionBankService.CreateQuestionAsync()` discards this explanation — it is never persisted to the `QuestionAiGenerated_V1` domain event or the `QuestionState` aggregate. The serving layer (`QuestionPoolActor`) loads `PublishedQuestion` records into memory for sub-10ms selection but these also lack explanations.

The `AnswerEvaluated` SignalR event sent to students has an `explanation` field (line 326 of `signalr-messages.ts`) but it is currently a placeholder string because no explanation data exists in the system.

This is the single cheapest win in the entire plan: the data is already generated and thrown away. Persist it.

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | 85-91 | `AiGeneratedQuestion` record — line 91: `string? Explanation` already exists on the DTO |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | 59-77 | `QuestionAiGenerated_V1` record — 18 parameters. `Explanation` is already there as the 17th parameter (nullable). Verify this. |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | 28-35 | `AiGenerationState` record — already has `string? Explanation` as last field. Verify this. |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | 162-185 | `Apply(QuestionAiGenerated_V1)` — check if explanation is propagated to `AiGenerationState` |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | 158-169 | `PublishedQuestion` record — already has `string? Explanation` as last field. Verify this. |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | 55-126 | `InitializeAsync()` — check if the Marten read-model query hydrates the explanation field |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | Full file | `CreateQuestionAsync()` — find where `AiGeneratedQuestion` is mapped to the event. Is `Explanation` passed through or dropped? |
| `contracts/frontend/signalr-messages.ts` | 320-339 | `AnswerEvaluatedPayload.explanation` — confirm it's `string` (not optional), currently placeholder |

---

## Implementation: 6-Step Verification-First Approach

The autoresearch report claims these fields already exist. **Verify each claim before proceeding.** If a field already exists, trace the data flow to find where the chain breaks. If a field is missing, add it.

### Step 1: Verify `QuestionAiGenerated_V1` Event

Read `QuestionEvents.cs` line 59-77. Confirm `Explanation` parameter exists. If it does, this step is done. If not:
- Add `string? Explanation` as the second-to-last parameter (before `DateTimeOffset Timestamp`)
- Nullable because existing events in the store won't have it — Marten handles missing fields as null

### Step 2: Verify `AiGenerationState` Record

Read `QuestionState.cs` line 28-35. Confirm `string? Explanation` field exists.

### Step 3: Verify `Apply(QuestionAiGenerated_V1)` Mapping

Read `QuestionState.cs` line 162-185. Does the `Apply` method map `e.Explanation` to `AiGenerationState.Explanation`? This is the most likely break point — the field may exist on both types but not be wired in the mapping.

### Step 4: Verify `PublishedQuestion` Record

Read `QuestionPoolActor.cs` line 158-169. Confirm `string? Explanation` field exists.

### Step 5: Verify `InitializeAsync()` Hydration

Read `QuestionPoolActor.cs` line 55-126. The Marten query that populates `PublishedQuestion` must include the explanation from the aggregate. If the query uses a projection or manual mapping, the explanation field may exist on the record but never be populated.

### Step 6: Verify `QuestionBankService.CreateQuestionAsync()` Propagation

Read `QuestionBankService.cs`. Find where the `AiGeneratedQuestion` DTO is converted to a `QuestionAiGenerated_V1` event. Is `dto.Explanation` passed to the event constructor, or is it dropped (null/omitted)?

**The chain is**: `AiGenerationService` -> `AiGeneratedQuestion.Explanation` -> `QuestionBankService.CreateQuestionAsync()` -> `QuestionAiGenerated_V1.Explanation` -> `QuestionState.Apply()` -> `AiGenerationState.Explanation` -> `QuestionPoolActor.InitializeAsync()` -> `PublishedQuestion.Explanation`.

Find the break(s) in this chain and fix them.

---

## Architectural Constraints

- **Event versioning** — additive nullable fields on Marten events are safe. Do NOT create `QuestionAiGenerated_V2`. Existing events deserialize with `Explanation = null`.
- **Backward compatibility** — all pre-existing questions will have `Explanation = null`. The serving layer and UI must handle this (show "No explanation available" or hide).
- **No UI changes** — this task is backend pipeline only. The SignalR contract already has the placeholder field.
- **No new files** — this is purely wiring existing fields together.

---

## What NOT to Do

- Do NOT create a V2 event — additive nullable fields are safe in Marten
- Do NOT modify `QuestionAuthored_V1` or `QuestionIngested_V1` events — those creation paths don't have AI explanations
- Do NOT add explanation to Admin API response DTOs — separate concern, separate task
- Do NOT backfill existing questions — that requires Task 00 (real LLM calls)
- Do NOT create new files

---

## Verification Checklist

- [ ] Trace the full chain: `AiGeneratedQuestion.Explanation` -> event -> aggregate -> serving model
- [ ] Create a question via AI generation — verify `QuestionAiGenerated_V1` event in PostgreSQL contains explanation text
- [ ] Query aggregate — verify `QuestionState.AiGeneration.Explanation` is populated
- [ ] Verify `PublishedQuestion` in `QuestionPoolActor` carries the explanation after reload
- [ ] Verify existing questions (pre-change) load with `Explanation = null` — no deserialization errors
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
