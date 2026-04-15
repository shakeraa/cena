# RDY-034: CAS-Gated Question Ingestion (ADR-0002 Enforcement)

- **Priority**: **Critical / ship-blocker** — directly violates ADR-0002
- **Complexity**: Senior engineer + Admin API familiarity
- **Source**: Coordinator spot-check after RDY-033 merge (2026-04-15)
- **Tier**: 1
- **Effort**: 3-5 days
- **Dependencies**: RDY-033 (CAS router stack is now wired — can be reused)

## Problem

ADR-0002 is the **first design non-negotiable**: *"SymPy CAS is the sole correctness oracle — the LLM explains, the CAS verifies. No math reaches students unverified."*

A code audit on 2026-04-15 found that **zero question-creation paths call `ICasRouterService.VerifyAsync` before persisting a question**. This means any question — human-authored, AI-generated, PDF-ingested, or seeded — can reach students with an incorrect `CorrectAnswer` and the system will happily mark students wrong for giving the right answer.

### Evidence (collected 2026-04-15)

- `ICasRouterService` is **not registered** in the Admin API DI container ([src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs](../../src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs)).
- `QuestionBankService.CreateQuestionAsync` ([src/api/Cena.Admin.Api/QuestionBankService.cs:296](../../src/api/Cena.Admin.Api/QuestionBankService.cs)) — the single entry point for `QuestionAuthored_V2`, `QuestionAiGenerated_V2`, and `QuestionIngested_V2` events — contains zero CAS calls.
- `AiGenerationService.BatchGenerateAsync` ([src/api/Cena.Admin.Api/AiGenerationService.cs:712](../../src/api/Cena.Admin.Api/AiGenerationService.cs)) and `GenerateFromTemplateAsync` (line 793) do not verify the generated `IsCorrect` option before returning results.
- `QuestionBankService.ApproveAsync` (line 282) appends `QuestionApproved_V1` with no re-check — a question can flow to `Published` with no CAS proof anywhere in its history.
- `QualityGateService.EvaluateAsync` ([src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs:52](../../src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs)) runs a Claude Haiku LLM call for "FactualAccuracy" and defaults to `80/80/75` if no API key is configured. **An LLM opinion is not an oracle.** ADR-0002 is explicit: LLMs explain, CAS verifies.
- [src/shared/Cena.Infrastructure/Documents/QuestionCasBinding.cs](../../src/shared/Cena.Infrastructure/Documents/QuestionCasBinding.cs) defines a `QuestionCasBinding` document with `CanonicalAnswer` and `VerifiedAt` fields — but `grep -r 'session.Store<QuestionCasBinding>'` across the repo returns **zero hits**. The table is defined but never populated.
- [src/actors/Cena.Actors/Cas/CasConformanceSuite.cs](../../src/actors/Cena.Actors/Cas/CasConformanceSuite.cs) defines a 500-pair oracle-agreement data set with a 99%-agreement `PassesThreshold` but has no runner class, no test, and is not wired into CI.

Bottom line: today, a math-incorrect question can ship to students and the system has no automated mechanism to block it.

## Scope

### 1. Register the CAS stack in the Admin API host

Register in both `Cena.Admin.Api.Host/Program.cs` and `CenaAdminServiceRegistration.cs` the same stack RDY-033c added to the Student API host:

- `IMathNetVerifier` → `MathNetVerifier`
- `ISymPySidecarClient` → `SymPySidecarClient`
- `ICasRouterService` → `CasRouterService`
- `ICostCircuitBreaker` (reuse existing registration or add `RedisCostCircuitBreaker`)
- `INatsConnection` (already registered in most hosts — verify)
- `ILlmClient` → `LlmClientRouter` + `AnthropicLlmClient` (already registered in Admin host for other reasons — verify)

### 2. Extend `QuestionCasBinding` and wire the write path

- Populate `QuestionCasBinding { QuestionId, CorrectAnswerRaw, CanonicalAnswer, VerifiedAt, Engine, LatencyMs, Status }` on every successful verification.
- Add `Status` values: `Verified`, `Unverifiable` (non-math content), `Failed`.
- `session.Store(binding)` in the same Marten unit-of-work as the question-authored event.

### 3. Block question creation on CAS failure

Modify `QuestionBankService.CreateQuestionAsync`:

1. Before appending `QuestionAuthored_V2` / `QuestionAiGenerated_V2` / `QuestionIngested_V2`:
   - Call `_casRouter.VerifyAsync(new CasVerifyRequest(CasOperation.NormalForm, correctAnswer, null, variable, 1e-9))` to compute a canonical form.
   - For multi-choice questions, verify the marked-correct option against the question's expected answer (if present) via `CasOperation.Equivalence`.
2. On CAS **Ok + Verified** → persist the question + `QuestionCasBinding { Status=Verified }`.
3. On CAS **Failure** (Verified=false but status Ok) → return `400 Bad Request` with `{ error: "CAS_VERIFICATION_FAILED", details: ... }`. Do not persist.
4. On CAS **Error / Timeout / CircuitBreakerOpen** → accept the question but mark `QuestionState.QualityEvaluation.NeedsReview = true` and persist `QuestionCasBinding { Status=Unverifiable, Engine="none" }`. These questions must not auto-approve.
5. On non-math subjects (language, history) → persist `QuestionCasBinding { Status=Unverifiable, Engine="n/a" }`. Non-math flow is not blocked.

### 4. Block approval on missing/failed CAS

Modify `QuestionBankService.ApproveAsync`:

- Load the `QuestionCasBinding` for the question.
- If `Status != Verified` and subject is math/physics → reject approval (`409 Conflict` with `{ error: "CAS_VERIFICATION_REQUIRED" }`).
- If binding is missing → treat as failed (backfill path handles this).

### 5. Gate AI-generated questions

Modify `AiGenerationService.BatchGenerateAsync` + `GenerateFromTemplateAsync`:

- After generation, run each `GeneratedQuestion.IsCorrectOption` through `ICasRouterService.VerifyAsync` before it lands in `BatchGenerateResult`.
- Questions that fail CAS verification are dropped from the returned batch and tallied under a new `DroppedForCasFailure` counter.
- Structured log: `[AI_GEN_CAS_REJECT] question_id=... correct_answer=... engine=... reason=...`.

### 6. Backfill path for existing questions

Add a one-shot admin endpoint `POST /api/admin/questions/cas-backfill` (role-gated to curriculum-admins) that:

- Iterates math/physics questions where `QuestionCasBinding` is missing.
- Runs CAS verification and writes the binding.
- Marks failed verifications with `NeedsReview = true`.
- Returns a summary: `{ verified: N, failed: M, unverifiable: K }`.

### 7. CasConformanceSuite runner + CI gate

- Add `CasConformanceSuiteRunner` test class that runs all 500 pairs through `ICasRouterService` against both MathNet and SymPy.
- Compute pairwise oracle agreement.
- Fail the test (and CI) if agreement < 99% (the threshold already defined in `CasConformanceSuite.PassesThreshold`).
- Wire into `.github/workflows/backend.yml`.

### 8. ADR-0032 — document the gating rules

New ADR `docs/adr/0032-cas-gated-question-ingestion.md` formalizing:

- Which operations gate on CAS (every math/physics question-creation + every approval).
- What happens on CAS unavailability (degraded path: accept + flag, never auto-approve).
- Which subjects bypass (language, history, code-based questions).
- Why the LLM "factual accuracy" score is **not** a sufficient substitute.

## Files to Create / Modify

### Create
- `docs/adr/0032-cas-gated-question-ingestion.md`
- `src/api/Cena.Admin.Api/QualityGate/CasVerificationGate.cs` — wraps the CAS call and translates results into gate outcomes.
- `src/actors/Cena.Actors.Tests/Cas/CasConformanceSuiteRunner.cs` — the missing test runner.
- `src/api/Cena.Admin.Api.Tests/QuestionBankService/CasGatingTests.cs` — verify creation is blocked on CAS failure, approval is blocked without binding, AI batch drops failing questions.
- `src/api/Cena.Admin.Api/Endpoints/CasBackfillEndpoint.cs` — backfill endpoint.

### Modify
- `src/api/Cena.Admin.Api.Host/Program.cs` + `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs` — register CAS stack.
- `src/api/Cena.Admin.Api/QuestionBankService.cs` — wire CAS gate into `CreateQuestionAsync` + `ApproveAsync`.
- `src/api/Cena.Admin.Api/AiGenerationService.cs` — wire CAS filter into `BatchGenerateAsync` + `GenerateFromTemplateAsync`.
- `src/shared/Cena.Infrastructure/Documents/QuestionCasBinding.cs` — add `Status` enum + new required fields.
- `src/actors/Cena.Actors/Questions/QuestionState.cs` — add `CasBindingId` field or mirror `Status` for projection-side queries.
- `.github/workflows/backend.yml` — add CasConformanceSuiteRunner test step.

## Acceptance Criteria

- [ ] `ICasRouterService` registered in Admin API DI (verified by a contract test modeled on `AnswerEndpointDiResolutionTests`).
- [ ] `CreateQuestionAsync` rejects questions whose marked-correct answer fails CAS equivalence with the canonical form (400 with typed error code).
- [ ] `ApproveAsync` rejects math/physics questions that have no `Verified` `QuestionCasBinding` (409 with typed error code).
- [ ] AI-generated questions are filtered through CAS before being returned; batch result reports `DroppedForCasFailure`.
- [ ] `QuestionCasBinding` is persisted for every math/physics question on creation with `CanonicalAnswer`, `Engine`, `LatencyMs`, `Status`.
- [ ] CAS unavailability (circuit breaker open) degrades gracefully: question accepted with `NeedsReview = true`, never auto-approved.
- [ ] Non-math subjects are not blocked; binding is persisted with `Status = Unverifiable, Engine = "n/a"`.
- [ ] Backfill endpoint exists, is role-gated, and reports verified/failed/unverifiable counts.
- [ ] `CasConformanceSuiteRunner` test exists and enforces 99% agreement threshold; CI fails below the threshold.
- [ ] ADR-0032 merged.
- [ ] Contract test proves every `[FromServices]` on the question-creation endpoint resolves from Admin API DI.
- [ ] Full `Cena.Actors.sln` builds with 0 errors; new tests all pass; legacy tests unchanged.

## Stub-Hardening (tracked here because it is the same code path)

The Bagrut PDF ingestion pipeline shipped in commit **df0b67c (2026-04-13, PHOTO-001 + PHOTO-002)** includes two production-labelled stub methods that violate the no-stubs rule saved to project memory on 2026-04-11:

- [BagrutPdfIngestionService.ExtractPagesAsync](../../src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs#L134) — returns the raw PDF bytes as a single "page" and discards the cancellation token. Comment explicitly says *"Production: use PdfSharp, iTextSharp, or Ghostscript to split pages"*.
- [BagrutPdfIngestionService.OcrPageAsync](../../src/api/Cena.Admin.Api/Ingestion/BagrutPdfIngestionService.cs#L141) — returns an empty `ExtractedPage` with confidence 0.0 and discards its input. Comment explicitly says *"Production: call Mathpix API or Gemini Vision for math-aware OCR"*.

These must be removed as part of RDY-034 (or the PDF ingestion endpoint must be taken offline until they are):

- Replace `ExtractPagesAsync` with a real PDF-to-image split using `PDFsharp` (already available on NuGet, MIT-licensed) or Poppler (`pdftoppm`).
- Replace `OcrPageAsync` with a real call to either Mathpix (the project already has `IPhotoDnaClient` / `IContentSafetyClient` as examples of httpClient integrations) or Gemini Vision via `ILlmClient` with a vision model.
- If neither OCR vendor is configured (missing API key), the ingestion endpoint must return `503 Service Unavailable` with a typed error — never silently succeed with empty extraction.
- Add a feature flag `CENA_BAGRUT_PDF_INGESTION_ENABLED` that defaults to **off** and returns `503` when disabled. Mirrors the pattern used in RDY-001 for `CENA_IMAGE_UPLOAD_ENABLED`.

## Out of Scope

- Retroactive re-verification of all existing questions (belongs to backfill ops, not this task).
- Step-by-step solution verification (`ICasLlmOutputVerifier` already exists and is out of scope here; belongs to a separate task about tutor output).
- Teacher-facing UI to visualize which questions have / lack CAS verification (belongs to admin UX).

> **Why this is ship-blocking**: ADR-0002 is one of three design non-negotiables locked in `CLAUDE.md`. Shipping without this gate means students can be marked wrong on questions that have mathematically incorrect answer keys — precisely the failure mode the ADR was written to prevent.
