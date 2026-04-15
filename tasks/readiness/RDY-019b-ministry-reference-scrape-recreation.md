# RDY-019b: Ministry Archive Reference Scrape + AI-Authored Recreation Pipeline

**Parent**: [RDY-019](tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) (split 2 of 3, v2)
**Priority**: Medium
**Complexity**: Senior engineer
**Effort**: ~2 weeks
**Blocker status**: BLOCKED until (a) RDY-019a taxonomy merged, (b) RDY-034 CAS-gated ingestion merged. Legal posture resolved — see below.

## Legal posture (user decision, 2026-04-15)

Ministry exams at `meyda.education.gov.il/sheeloney_bagrut/` are used as **reference material only**, not redistributed. We scrape to analyze structure (topics, difficulty distribution, Bloom levels, item formats), then **recreate** fresh items via the AI generation pipeline, all CAS-gated per ADR-0002. Raw PDFs and raw extracted questions never enter the student-facing corpus.

## Problem

Zero items in our corpus reflect actual Bagrut structure. Generated content may drift from real exam topic weightings, difficulty curve, and question formats. We need a reference corpus to calibrate AI authorship against.

## Scope

### 1. Scraper (reference-only)

New file: `scripts/bagrut-scraper.py`

- Download Bagrut exam PDFs from Ministry website (polite rate limiting, robots.txt)
- Store raw PDFs under `corpus/bagrut/reference/<year>/<track>/<paper>.pdf` — **git-ignored, local-only, never shipped**
- Checkpointed so partial runs resume

### 2. OCR + structural extraction

- Use existing Gemini/Mathpix HTTP clients (resilience via `HttpPolicies.cs` from RDY-012)
- Env vars: `GEMINI_API_KEY`, `MATHPIX_APP_ID`, `MATHPIX_APP_KEY`
- Extract per-question: topic cluster, difficulty signal, Bloom level, item format (multiple choice / free response / proof / computation)
- Output `corpus/bagrut/reference/analysis.json` — structural summary **only**. Exam text is NOT persisted to Marten, NOT ingested into QuestionBank.

### 3. Coverage-calibrated recreation pipeline

- Build `AiGenerationService` parameter bundles driven by the reference analysis: "generate N items, topic=calculus.derivatives.chain_rule, difficulty=0.65, Bloom=apply, format=free_response"
- Every generated item flows through `QuestionBankService.CreateQuestionAsync` + CAS router (ADR-0002)
- Tag with `Provenance = "recreation"`, `ReferenceCalibration = { year, topic, difficulty }` — trace back to the reference cluster, NOT to any specific Ministry question.

### 4. Target

- Reference scrape: 640 pages fully analyzed, structural JSON committed
- Recreations: 100+ CAS-verified items spanning math_5u, driven by reference coverage weights

## Files to Modify

- New: `scripts/bagrut-scraper.py`
- New: `scripts/bagrut-reference-analyzer.py`
- New: `corpus/bagrut/reference/` (git-ignored; add `.gitignore` entry)
- New: `src/shared/Cena.Infrastructure/Content/ReferenceCalibratedGenerationService.cs`
- Edit: `src/api/Cena.Admin.Api/Program.cs` (register service)
- Tests: scraper unit, analyzer fixture test, recreation pipeline integration (mock CAS router)

## Acceptance Criteria

- [ ] 640+ pages scraped to local reference store
- [ ] `analysis.json` committed with topic × difficulty × format distribution
- [ ] 100+ CAS-verified recreations ingested via `QuestionBankService`, every one taxonomy-mapped and carrying `Provenance=recreation`
- [ ] No raw Ministry question text appears in Marten events or any committed JSON
- [ ] Structured logging `[BAGRUT_RECREATION]` with per-topic counts
- [ ] Full `Cena.Actors.sln` builds with 0 errors

## Coordination notes

- DO NOT start until RDY-019a AND RDY-034 are merged.
- Reference PDFs stay local; ensure `.gitignore` covers `corpus/bagrut/reference/` before first scrape run.
- Recreation path reuses AI generation stack — coordinate with claude-1 if that stack is changing.

---- events ----
2026-04-15T16:31:45.854Z  enqueued   -
2026-04-15T16:36:29.822Z  updated    -
