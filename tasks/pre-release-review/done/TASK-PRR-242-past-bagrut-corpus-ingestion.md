# TASK-PRR-242: Past-Bagrut corpus ingestion (content acceleration)

**Priority**: P0 — content enabler for EPIC-PRR-G
**Effort**: M (2-3 weeks, leverages existing OCR pipeline)
**Lens consensus**: persona-educator, persona-ministry
**Source docs**: Memory "Bagrut reference-only" (2026-04-15), user directive 2026-04-21 ("I can scrape all previous bagrut questions as a corpus"), memory "Production content pipeline roadmap" (Phase 1A OCR pipeline already complete — 13/13 OCR layers real, 111/111 tests)
**Assignee hint**: content-engineering lead + existing OCR pipeline owner
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-g, priority=p0, content, corpus, ocr, launch-accelerator
**Status**: Ready (depends on nothing new; leverages existing pipeline)
**Source**: User directive 2026-04-21 + persona-educator + persona-ministry agreement that past Bagrut papers are legitimate reference material
**Tier**: launch
**Epic**: [EPIC-PRR-G](EPIC-PRR-G-sat-pet-content-engineering.md) — enabler task that reduces downstream content cost ~50%.

---

## Goal

Ingest Ministry-published past Bagrut papers (2000-present, where available) as a tagged reference corpus into Cena's item-authoring pipeline. Feeds EPIC-PRR-G + PRR-241 (humanities) + PRR-239 (Arab-stream) item authoring, cutting estimated content-engineering cost from ~$40-60k → ~$20-30k.

Per memory "Bagrut reference-only": past Bagrut text is reference material for AI-authored CAS-gated recreations. Never shown raw to students.

## Scope

1. **Scrape/ingest Ministry-published past Bagrut papers**: https://edu.gov.il + relevant public archives. Per-subject per-stream per-year.
2. **OCR pipeline**: leverage existing Phase 1A OCR (13/13 layers real per memory) to convert PDF scans to structured text.
3. **Tagging**: each question tagged with `{subject, שאלון code, year, moed, question_number, stream}`. Machine-extractable metadata validated by human SME sampling.
4. **Storage**: internal reference corpus, NOT exposed to students directly. Access scoped to item-authoring pipeline + rubric authors.
5. **Recreation pipeline feed**: item-authoring LLM prompts include matched reference questions from corpus as "style guide / difficulty anchor"; output items go through CAS oracle (math) or SME review (humanities).
6. **Provenance tracking**: every generated item records its reference-question lineage for audit (per memory "Bagrut reference-only").
7. **Copyright/licensing review**: Ministry papers are publicly published under usage terms permitting reference use; document compliance path. No direct reproduction to students.

## Files

- `scripts/corpus/bagrut-ingest.py` (new) — scraper + ingester.
- `src/content/corpus/BagrutReferenceCorpus/` (new, gitignored; stored per infra plan).
- Item-authoring pipeline integration (per subject).
- Corpus coverage dashboard.
- `docs/legal/bagrut-corpus-usage.md` (new) — compliance memo.
- Tests: ingest fixture files, OCR round-trip, tagging accuracy sample.

## Definition of Done

- Corpus covers 2015-present for all catalog subjects at minimum.
- OCR accuracy verified on SME sample (≥95% character accuracy; human-reviewable flagged).
- Item-authoring pipeline uses corpus references in at least 2 subjects as proof.
- Legal memo reviewed.
- Provenance traceable end-to-end: student-facing item → lineage → reference question.

## Non-negotiable references

- Memory "Bagrut reference-only" (load-bearing).
- Memory "Production content pipeline roadmap" (leverages Phase 1A).
- ADR-0002 (CAS oracle applies to math recreations).
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + corpus coverage report>"`

## Related

- EPIC-PRR-G, PRR-239, PRR-240, PRR-241, PRR-220, PRR-033.
