# RDY-058: One-Click "Generate Similar" from an Existing Question

**Parent**: RDY-034 (CAS-gated ingestion) + ADR-0002
**Related**: `AiGenerationService.BatchGenerateAsync`, `QualityGateService`, `CasGatedQuestionPersister`
**Priority**: High — closes the audit-flagged gap between "ingest a question" and "re-create variants at a chosen difficulty"
**Complexity**: Small (backend handler + UI dialog)
**Effort**: 0.5–1 day
**Blocker status**: None — every dependency is on main and green.

## Problem

The admin can already:
- Generate N questions from a free-text stem via `/api/admin/ai/generate`
- Batch-generate at a difficulty band via `/api/admin/ai/generate-batch`
- Generate from an OCR template via `/api/admin/ai/generate-from-template`

…all CAS-gated, all difficulty-tunable, all QualityGate-scored. **But none of these take an existing `QuestionReadModel` id as input.** Curators who want "make 3 similar to this question at difficulty 0.6" have to manually copy the stem + subject + concept + Bloom level into the Generate dialog.

That's the friction the readiness audit flagged.

## Scope

### 1. Endpoint

`POST /api/admin/questions/{id}/generate-similar`

Body:
```json
{
  "count": 3,
  "minDifficulty": 0.55,     // optional; defaults to source - 0.15
  "maxDifficulty": 0.75,     // optional; defaults to source + 0.15
  "language": "he"           // optional; defaults to source.language
}
```

Response: `BatchGenerateResponse` (reused) — the result card renderer on the admin UI already handles it.

### 2. Handler

`GenerateSimilarHandler.HandleAsync` — pure function, no HttpRequest, easily unit-testable:

1. Load `QuestionReadModel` by id (404 if missing).
2. Build an `AiGenerateRequest` from the source:
   - `Subject` = source.Subject
   - `Topic` = source.Topic
   - `Grade` = source.Grade
   - `BloomsLevel` = source.BloomsLevel
   - `Language` = body.Language ?? source.Language
   - `MinDifficulty` = body.MinDifficulty ?? clamp(source.Difficulty - 0.15, 0, 1)
   - `MaxDifficulty` = body.MaxDifficulty ?? clamp(source.Difficulty + 0.15, 0, 1)
   - `Context` = source.StemPreview (carrying the math text the LLM rebuilds around)
   - `Count` = clamp(body.Count, 1, 20)
3. Delegate to `AiGenerationService.BatchGenerateAsync` (which runs QualityGate + CAS).
4. Emit `QuestionSimilarGenerated_V1 { ParentQuestionId, Count, MinDifficulty, MaxDifficulty, GeneratedBy, Timestamp }` on the parent's stream so provenance is queryable.
5. Return the `BatchGenerateResponse` — the standard CAS-drop counters flow through unchanged.

### 3. Event

`QuestionSimilarGenerated_V1(ParentQuestionId, Count, MinDifficulty, MaxDifficulty, GeneratedBy, Timestamp)` in `Cena.Actors/Events/QuestionEvents.cs` (or a new `QuestionRecreationEvents.cs`).

### 4. Admin UI

- "Generate similar" action on each row in the questions list.
- Opens a small dialog with:
  - Count slider (1–20)
  - Difficulty band slider (two-thumb, 0-1)
  - "Inherit from source" toggle (pre-fills the band from source.Difficulty ±0.15)
  - "Generate" / "Cancel"
- Result: reuse the existing generate-result card renderer; each candidate shows its CAS verdict; "Save to bank" button persists via the standard path.

### 5. Tests

- `GenerateSimilarHandlerTests` (xUnit + NSubstitute) — 8 tests:
  - Happy path produces BatchGenerateResponse
  - Source not found → 404
  - Default difficulty band = source ± 0.15, clamped [0,1]
  - Custom difficulty band honoured
  - Difficulty flipped (max < min) → swapped
  - Language override respected; default falls back to source
  - Count clamped to [1,20]
  - CAS failure counters propagate

## Files to Modify

- New: `src/actors/Cena.Actors/Events/QuestionRecreationEvents.cs`
- New: `src/api/Cena.Admin.Api/Questions/GenerateSimilarHandler.cs`
- New: `src/api/Cena.Admin.Api/Questions/GenerateSimilarEndpoints.cs`
- Edit: `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs`
- New: `src/api/Cena.Admin.Api.Tests/Questions/GenerateSimilarHandlerTests.cs`
- New: `src/admin/full-version/src/views/apps/questions/GenerateSimilarDialog.vue`
- Edit: `src/admin/full-version/src/pages/apps/questions/list/index.vue` (action button + dialog mount)

## Acceptance Criteria

- [ ] Endpoint returns 200 + `BatchGenerateResponse` for a real question id
- [ ] Endpoint returns 404 when the id is missing
- [ ] Default difficulty band = source.Difficulty ± 0.15, clamped
- [ ] Every generated candidate carries a CAS verdict
- [ ] `QuestionSimilarGenerated_V1` appended to the parent's stream
- [ ] Admin UI "Generate similar" button opens the dialog + renders results
- [ ] `Cena.Actors.sln` builds with 0 errors
- [ ] All new handler tests pass

## Coordination notes

- Do NOT bypass `AiGenerationService.BatchGenerateAsync` — it is the single gated orchestrator.
- Keep the event narrow (no raw stems) — the student-facing recreation boundary is already enforced by `CasGatedQuestionPersister`.
- ModeratorOrAbove auth + `ai` rate-limit bucket (same as the existing generate endpoints).
