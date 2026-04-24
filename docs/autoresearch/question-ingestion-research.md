# Auto-Research: Question Ingestion & Normalization Pipeline

> **Status:** Research complete
> **Date:** 2026-03-27
> **Purpose:** Design an automated pipeline for ingesting exam questions from URLs, PDFs, and photos — normalizing, classifying quality/difficulty/correctness, deduplicating, and caching
> **Research method:** 3 parallel agents (Math OCR, Pipeline Architecture, Assessment Schemas)
> **Supporting docs:**
> - `docs/math-ocr-research.md` — OCR tool comparison
> - `docs/question-ingestion-pipeline-research.md` — full pipeline architecture
> - `docs/assessment-item-schema-research.md` — schema design (1,289 lines, 7 JSON blocks)

---

## Executive Summary

The pipeline ingests exam questions from any source (URL, PDF upload, photo) and produces structured, quality-scored, deduplicated items ready for the adaptive learning graph. Total cost for the entire Bagrut math corpus: **~$25-50 one-time**. Ongoing real-time ingestion: **~$30-50/month** at scale.

---

## Pipeline Architecture

```
INPUT SOURCES
  ├── URL (web page or PDF link)
  ├── S3 upload (predefined bucket/prefix)
  └── Photo upload (mobile camera, scan)
         │
         ▼
STAGE 1: File Acquisition & Watching
  ├── S3 Event Notification → EventBridge → SQS (production)
  ├── Chokidar v5 file watcher (local dev)
  ├── URL fetcher (headless download, PDF extraction)
  └── Idempotency: SHA-256 content hash checked in Redis before processing
         │
         ▼
STAGE 2: OCR & Text Extraction
  ├── PRIMARY: Gemini 2.5 Flash ($0.15-0.30/M tokens)
  │     Native Hebrew/Arabic, handles BiDi, 1M context
  │     Send full PDF pages, get structured JSON output
  ├── FALLBACK: Mathpix ($0.005/page)
  │     Best math-to-LaTeX accuracy for complex equations
  ├── LOCAL DEV: Surya (open-source, 90+ languages, LaTeX OCR)
  └── Output: raw text + LaTeX math expressions per page
         │
         ▼
STAGE 3: Question Segmentation
  ├── LLM-first extraction (outperforms layout analysis for exams)
  │     Gemini understands question numbering, multi-part deps, diagram-question mapping
  ├── Multi-part chains preserved (a→b→c dependency)
  └── Output: individual question items in raw JSON
         │
         ▼
STAGE 4: Normalization → Cena Item Schema
  ├── Map to Cena-specific schema (~30 fields)
  │     LaTeX for math, locale map for bilingual text
  │     {math:key} placeholders in Hebrew/Arabic text reference shared expressions
  ├── Source provenance: exam year, questionnaire code, question number
  ├── Bilingual: Hebrew + Arabic stems, shared math, text-free SVG diagrams
  └── Output: structured CenaItem JSON
         │
         ▼
STAGE 5: Quality Classification (5-stage)
  ├── 1. SymPy verification — deterministic math check (covers 60-75% of questions)
  ├── 2. Bloom's classification — SVM + TF-IDF (~94% accuracy, beats LLMs at 72-73%)
  ├── 3. Difficulty estimation — LLM-based, calibrated to Bagrut 3/4/5-unit levels
  ├── 4. Language quality — LLM check for Hebrew/Arabic grammar, terminology
  ├── 5. Pedagogical quality — LLM + heuristics (ambiguity, solvability, clarity)
  └── Output: quality_scores object on each item
         │
         ▼
STAGE 6: Deduplication (3-level)
  ├── EXACT: SHA-256 of normalized content → Redis SET, O(1) lookup
  ├── STRUCTURAL: Math AST normalization (replace vars/constants with placeholders)
  │     Catches "same problem, different numbers" — language-independent
  ├── SEMANTIC: mE5-large embeddings, cosine >0.92 = duplicate
  │     Handles cross-language dedup automatically
  └── MinHash/LSH unnecessary until >100K questions
         │
         ▼
STAGE 7: Cache & Store
  ├── S3: question JSON blobs (content-addressed, SHA-256 = item ID)
  ├── Redis: metadata indexes, dedup hash sets
  ├── PostgreSQL: queryable data joined with student records
  ├── Neo4j: concept linkages, prerequisite edges
  └── Total infra: ~$30/month
```

---

## Item Schema (Summary)

**Decision: QTI 3.0 is overkill.** XML-only, MathML-only, no adaptive learning metadata, no bilingual support, no prerequisite relationships. Build Cena-specific JSON schema with QTI export adapter later if LMS integration is needed.

**Key schema design:**

| Aspect | Decision | Rationale |
|---|---|---|
| Math format | **LaTeX** (primary), KaTeX (render), SymPy (verify) | LLMs output LaTeX natively; 10x more compact than MathML |
| Bilingual model | Locale map `{"he": "...", "ar": "..."}` + shared `{math:key}` placeholders | Math authored once, rendered identically in both languages |
| Diagrams | Text-free SVGs with locale-aware label overlays | Language-independent base, localized annotations |
| Multi-part | Linked item group with dependency chain + score weights | Not monolithic — each sub-question is a separate item |
| Difficulty | 3-phase: expert estimate → Elo from data → IRT at scale | Progressive calibration as student data accumulates |
| Provenance | `generated_by`, `modeled_after`, `reviewed_by`, `extraction_confidence`, `sympy_validated` | Full audit trail for legal defensibility |
| Neo4j storage | Scalar metadata as node properties + full JSON as text blob | Cypher filtering on metadata, full JSON for rendering |

### Schema Fields (top-level)

```
item_id              SHA-256 content hash
item_type            mcq | open_numeric | open_expression | proof | function_investigation | ...
source               { type, exam_year, questionnaire_code, question_number, url, upload_path }
content              { stems: {he, ar}, math_expressions: {key: latex}, diagrams: [...] }
answer               { correct_value, sympy_expr, display_latex, partial_credit_rubric }
distractors[]        { value, misconception_type, explanation }
classification       { bloom_level, webb_dok, difficulty, depth_unit, concept_ids, prerequisites }
quality_scores       { math_correctness, language_quality, pedagogical_quality, overall }
provenance           { generated_by, modeled_after, reviewed_by, extraction_confidence }
dedup_hashes         { exact_sha256, structural_ast_hash, semantic_embedding }
multi_part           { group_id, sequence, depends_on, score_weight }
timestamps           { created, last_reviewed, published }
status               draft | reviewed | published | deprecated
```

---

## OCR Tool Comparison

| Tool | Hebrew/Arabic | Math→LaTeX | Cost | Best For |
|---|---|---|---|---|
| **Gemini 2.5 Flash** | Native | Good | $0.15-0.30/M tokens | Primary OCR, full pages |
| **Mathpix** | Confirmed | Best-in-class | $0.005/page | Complex equation fallback |
| **Surya/Marker** | 90+ languages | Moderate | Free (AGPL) | Local development |
| **Mistral OCR 3** | Claimed | 94.29% accuracy | $2/1K pages | Cost-sensitive batch |
| **GPT-4o Vision** | Strong | Good | $2.50/M tokens | Already in stack |
| Tesseract | Reverses word order | None | Free | **Not recommended** |
| Nougat (Meta) | English only | Good | Free | **Not recommended** |
| GOT-OCR 2.0 | No He/Ar training | Limited | Free | **Not recommended** |

---

## Cost Summary

| Activity | Cost | Frequency |
|---|---|---|
| Full Bagrut corpus OCR (~2,000 pages) | $0.50-$2 (Gemini) | One-time |
| Complex equation fallback (Mathpix) | $5-10 | One-time |
| Quality classification (full corpus) | $5-15 | One-time |
| **Total corpus ingestion** | **~$25-50** | **One-time** |
| S3 + Redis + infra | ~$30/month | Ongoing |
| Real-time photo ingestion (100K/month) | $35-50/month | At scale |

---

## Critical Next Steps

1. **Build internal Hebrew/Arabic math OCR benchmark** — 50-100 manually annotated Bagrut pages, head-to-head Gemini vs Mathpix vs Surya
2. **Implement SVM Bloom's classifier** — outperforms LLMs at 94% vs 72-73% for small educational datasets
3. **Create the Cena Item Schema** as a TypeScript interface + JSON Schema — full spec in `docs/assessment-item-schema-research.md`
4. **Prototype the OCR → normalization → classification pipeline** on 10 Bagrut exam pages to validate before scaling

---

## Sources

### Math OCR
- Gemini 2.5 Flash pricing and capabilities (Google AI)
- Mathpix API documentation and Hebrew/Arabic support
- Surya/Marker (VikParuchuri) — open-source OCR
- Mistral OCR 3 benchmarks
- Docling/Granite-Docling (IBM) — equation F1 of 0.968

### Pipeline Architecture
- S3 Event Notifications + EventBridge + SQS patterns (AWS)
- Chokidar v5 file watching (Node.js)
- Content-addressed storage patterns
- mE5-large multilingual embeddings for cross-language dedup
- SVM + TF-IDF for Bloom's classification (outperforms BERT/LLMs on small datasets)

### Assessment Schemas
- QTI 3.0 specification (1EdTech)
- Khan Academy Perseus JSON format
- Learnosity RTL configuration
- OATutor BKT skill mapping schema
- latex2sympy2 for LaTeX ↔ SymPy conversion
