# RDY-019: Bagrut Corpus Ingestion + Topic Taxonomy (Parent Index)

- **Priority**: Medium — blocks content validity and calibration
- **Complexity**: Senior engineer + curriculum expert
- **Source**: Expert panel audit — Amjad (Curriculum), Yael (Psychometrics)
- **Tier**: 3
- **Effort**: 3–4 weeks aggregated across splits

> **This file is an index.** The work has been split into independently
> claimable subtasks so multiple agents can work in parallel without
> stepping on each other. Pick a subtask below and follow its file.

## Problem (summary)

1. All 1,000 seed questions are programmatically generated — zero items reflect the actual Ministry Bagrut structure.
2. Concepts are implicit string IDs with no formal hierarchy matching the official syllabus — coverage gaps are invisible.
3. 3-unit track: absent. 4-unit track: mentioned but no ontology.

## Subtasks

| File | Scope | Blocker |
|------|-------|---------|
| [RDY-019-ocr-spike.md](RDY-019-ocr-spike.md) | Evaluate offline OCR stacks (Tesseract / Nougat / Marker / Surya / pix2tex) against 10 real Bagrut pages; produce ADR + recommended pipeline | None — unblocks RDY-019b's tooling choice |
| [RDY-019a-bagrut-taxonomy.md](RDY-019a-bagrut-taxonomy.md) | Create `scripts/bagrut-taxonomy.json` (5u/4u/3u hierarchy), remap existing questions, add CI validator | None — can start immediately |
| [RDY-019b-ministry-reference-scrape-recreation.md](RDY-019b-ministry-reference-scrape-recreation.md) | Reference-only scrape of Ministry archive + coverage-calibrated AI recreation pipeline (CAS-gated, never ships raw Ministry text) | Blocked on RDY-019a + RDY-034 (merged) |
| [RDY-019c-3u4u-seed-coverage-review.md](RDY-019c-3u4u-seed-coverage-review.md) | 10+ seed items per 3u/4u track, coverage report endpoint, curriculum-expert sign-off | Blocked on RDY-019a + curriculum expert availability |
| [RDY-019d-bagrut-content-expert-followups.md](RDY-019d-bagrut-content-expert-followups.md) | Legacy expert-followups doc (Amjad-dependent) — retained as historical context | Blocked on Amjad |

## Legal posture (user decision, 2026-04-15)

Ministry exams at `meyda.education.gov.il/sheeloney_bagrut/` are **reference material only**, not redistributed. We scrape to analyze structure (topics, difficulty distribution, Bloom levels, item formats), then **recreate** fresh items via the AI pipeline, all CAS-gated per ADR-0002. Raw PDFs and raw extracted questions never enter the student-facing corpus. See the `bagrut-reference-only` memory record for the authoritative framing.

## Done definition

This index can be moved to `done/` once all five child files are in `done/`.
