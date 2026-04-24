# Syllabus & Corpus Strategy — Research Index

> **Topic:** Content pipeline for Israeli Bagrut exam preparation (Hebrew + Arabic)
> **Date:** 2026-03-27
> **Scope:** 9 parallel research agents across 2 rounds covering legal, LLM, OCR, pipeline, schema, Arabic, timeline, and competitive domains
> **Total sources:** 70+ citations

---

## 5-Point Summary

**1. The legal foundation needs fixing.** Bagrut exams are NOT public domain — they are copyrighted State works with a 50-year term (Israeli Copyright Act 2007, Sections 36/42). The strategy document's "public domain" claim is a material misstatement for investors. Fix: change to "fair use under Section 19" and seek explicit Ministry of Education permission.

**2. Replace Kimi K2.5 with GPT-4o or Claude.** Kimi's Hebrew/Arabic is weak (Chinese-first training), RTL+math BiDi handling produces formatting errors, and the China-based API creates operational friction (RMB billing, latency from Israel). GPT-4o ranks #1 for this use case (best BiDi handling, strong Hebrew/Arabic, mature batch API). For OCR specifically, Gemini 2.5 Flash is the best value at $0.15-0.30/M tokens with native Hebrew/Arabic.

**3. The Math Bagrut has zero MCQs and the timelines are optimistic.** All questions are open-ended (calculate, prove, find). The strategy's MCQ distractor extraction is invalid — distractors must be generated from misconception patterns instead. Timelines are achievable only with 2 experts or AI pre-screening (which can 2-3x expert throughput). With 1 expert alone, Math 5-unit takes 22-31 weeks, not the claimed 12-18.

**4. Build a 7-stage question ingestion pipeline for ~$25-50 total.** File watcher (S3 events) → OCR (Gemini + Mathpix fallback) → LLM-first question segmentation → Cena-specific JSON schema (not QTI — it's overkill) → 5-stage quality classification (SymPy + SVM Bloom's at 94% accuracy + LLM) → 3-level deduplication (hash + math AST + semantic embedding) → content-addressed cache (~$30/month). LaTeX for storage, KaTeX for rendering, SymPy for verification.

**5. Arabic is a genuine competitive advantage if done right.** No Israeli EdTech platform generates Arabic-native math content — they all translate from Hebrew. Cena can differentiate by generating directly in Arabic using Arabic Bagrut corpus data. But: expand the glossary from 30 to 200+ terms, validate against real Arabic Bagrut PDFs (not pan-Arab dictionaries), build an Arabic quality benchmark (no LLM exceeds 67% accuracy on Arabic math vs 85%+ English), and hire an Arabic-speaking Bagrut teacher for review. The Arab-sector 5-unit math gap (23% vs 47% eligibility) is the highest-impact target.

---

## Research Files

### Round 1: Strategy Review (6 parallel agents)

| File | Topic | Key Finding |
|---|---|---|
| [syllabus-corpus-research.md](syllabus-corpus-research.md) | Consolidated strategy review | 3 critical errors, 5 corrections, 8 enhancements |
| [arabic-math-education-research.md](arabic-math-education-research.md) | Arabic language specifics | Glossary expansion, RTL+math, translation vs generation, performance gaps |

### Round 2: Ingestion Pipeline (3 parallel agents)

| File | Topic | Key Finding |
|---|---|---|
| [question-ingestion-research.md](question-ingestion-research.md) | Consolidated pipeline spec | 7-stage pipeline, $25-50 total, $30/month ongoing |
| [math-ocr-research.md](math-ocr-research.md) | OCR tool comparison | Gemini 2.5 Flash primary, Mathpix fallback, Tesseract/Nougat ruled out |
| [question-ingestion-pipeline-research.md](question-ingestion-pipeline-research.md) | Full pipeline architecture | File watching, dedup, caching, quality classification |
| [assessment-item-schema-research.md](assessment-item-schema-research.md) | Item schema design | QTI rejected, Cena-specific JSON, ~30 fields, 7 working JSON examples |

### Logs

| File | Purpose |
|---|---|
| [results_log.txt](results_log.txt) | Chronological research log with iteration details |
| [AUTORESEARCH_CONFIG.txt](AUTORESEARCH_CONFIG.txt) | Config from prior focus-degradation research |
