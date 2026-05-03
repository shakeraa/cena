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

### 2. Text extraction + OCR fallback

**Architecture change (2026-04-16)**: per [RDY-019-OCR-SPIKE](RDY-019-ocr-spike.md) findings, the Ministry PDFs already ship with clean Hebrew text layers in ~90 % of cases (spike measured 9/10 real PDFs at 80–87 % Hebrew, 0 % gibberish). Extraction therefore runs the OCR cascade only as a fallback, not as the default path. The cascade is defined in [ADR-0033](../../docs/adr/0033-cena-ocr-stack.md) and shared with Surface A (student photos).

Per-file flow:

1. **Triage**: `scripts/ocr-spike/pdf_triage.py` classifies each PDF as `text` / `image_only` / `mixed` / `scanned_bad_ocr` / `encrypted`.
2. **If `text`**: extract via `pypdf` — no OCR, no cloud calls.
3. **Else**: hand to the cascade service (`OcrCascadeService` — C# port of `scripts/ocr-spike/pipeline_prototype.py`).
4. **Cascade internals**: Surya layout → Tesseract/Surya text + pix2tex math → confidence gate at τ=0.65 → Mathpix/Gemini fallback for low-confidence regions only → SymPy CAS validation (ADR-0002).

Cloud dependencies become **opt-in** rather than blocking:
- Mathpix / Gemini keys are used *only when* Layer 4 fires. Projected to fire on <5 % of pages.
- Projected spend for the 640-page scrape: **< $1** (vs ~$2.56 cloud-first).
- RDY-019b no longer blocks on API-key provisioning.

Extract per-question: topic cluster, difficulty signal, Bloom level, item format (multiple choice / free response / proof / computation). Output `corpus/bagrut/reference/analysis.json` — structural summary **only**. Exam text is NOT persisted to Marten, NOT ingested into QuestionBank.

### 3. Coverage-calibrated recreation pipeline

- Build `AiGenerationService` parameter bundles driven by the reference analysis: "generate N items, topic=calculus.derivatives.chain_rule, difficulty=0.65, Bloom=apply, format=free_response"
- Every generated item flows through `QuestionBankService.CreateQuestionAsync` + CAS router (ADR-0002)
- Tag with `Provenance = "recreation"`, `ReferenceCalibration = { year, topic, difficulty }` — trace back to the reference cluster, NOT to any specific Ministry question.

### 4. Target

- Reference scrape: 640 pages fully analyzed, structural JSON committed
- Recreations: 100+ CAS-verified items spanning math_5u, driven by reference coverage weights

## Files to Modify

- New: `scripts/bagrut-scraper.py` (network-mode scraper — a superset of `scripts/ocr-spike/bagrut_scrape.py` already in main)
- New: `scripts/bagrut-reference-analyzer.py`
- New: `corpus/bagrut/reference/` (git-ignored; `.gitignore` already covers `corpus/` from RDY-019-OCR-SPIKE)
- New: `src/shared/Cena.Infrastructure/Ocr/OcrCascadeService.cs` — C# port of [`scripts/ocr-spike/pipeline_prototype.py`](../../scripts/ocr-spike/pipeline_prototype.py); shared with Surface A
- New: `src/shared/Cena.Infrastructure/Ocr/OcrContextHints.cs` — DTO mirroring the Python `OcrContextHints` dataclass
- New: `src/shared/Cena.Infrastructure/Content/ReferenceCalibratedGenerationService.cs`
- Edit: `src/api/Cena.Admin.Api/Program.cs` (register cascade + recreation services)
- Tests: scraper unit, analyzer fixture test, recreation pipeline integration (mock CAS router), cascade integration on frozen fixture subset

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
