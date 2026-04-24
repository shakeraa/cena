# Question Ingestion Pipeline — Bounded Context Specification

> **Status:** Specification
> **Last updated:** 2026-03-27
> **Classification:** Extension of Content Authoring Context
> **Research basis:** `docs/autoresearch/question-ingestion-research.md`, `docs/autoresearch/syllabus-corpus-research.md`

---

## 1. The Problem

The Content Authoring Context (CNT-001 through CNT-007) describes a batch pipeline: Kimi generates questions overnight, experts review, QA validates, publish. But there is no way to:

1. **Ingest external questions** — from URLs, uploaded PDFs, or photos of exam papers
2. **Normalize** them into a structured schema with math expressions, bilingual text, and classification metadata
3. **Classify quality** automatically — math correctness, Bloom's level, difficulty, language quality
4. **Deduplicate** against the existing corpus
5. **Re-create** original questions inspired by extracted patterns (legally defensible content generation)
6. **Moderate** through an approval workflow before serving to students

This specification fills that gap.

---

## 2. Architecture Overview

```
INPUT LAYER                   PROCESSING LAYER                OUTPUT LAYER
─────────────                 ────────────────                ────────────

URL ──────┐                   ┌─ OCR + Extract ─┐            ┌─ Draft Pool
           ├─► File Watcher ──┤                  ├─► Normalize ──► Classify ──► Dedup ──┤
S3 Upload ─┤   (SQS queue)   └─ Segment Q's  ──┘            │              │           ├─► Review Queue
           │                                                  │              │           │   (moderator)
Photo ─────┘                                                  │              │           │
                                                              │              ▼           │
                                                              │         Re-Create ───────┤
                                                              │         (new original    │
                                                              │          questions)      ▼
                                                              │                     Published Pool
                                                              │                     (Neo4j + S3)
                                                              │                          │
                                                              └──── Style Guide ─────────┘
                                                                   (patterns, not content)
```

### Key Principle: Extract-Normalize-Recreate (ENR)

Source questions are **never served to students**. They feed a style guide. Students only see original, validated, SymPy-verified questions generated in the same pattern. This is the legal and pedagogical foundation.

---

## 3. Input Sources

| Source | Format | Trigger | Example |
|---|---|---|---|
| **URL** | Web page or direct PDF link | Admin submits URL via API | `meyda.education.gov.il/sheeloney_bagrut/2022/2/HEB/35582.pdf` |
| **S3 upload** | PDF, PNG, JPG, HEIC | S3 event notification | Teacher drops file into `s3://cena-ingest/incoming/` |
| **Photo** | Camera capture on mobile | Mobile app upload API | Student photographs a worksheet |
| **Batch directory** | Multiple PDFs | Scheduled scan of `s3://cena-ingest/batch/` | 15 years of Bagrut exams |

### Configuration

```yaml
# config/ingestion/sources.yaml
ingestion:
  s3_bucket: cena-ingest
  incoming_prefix: incoming/
  batch_prefix: batch/
  processed_prefix: processed/
  failed_prefix: failed/

  supported_formats: [pdf, png, jpg, jpeg, heic, webp, tiff]
  max_file_size_mb: 50

  ocr:
    primary: gemini-2.5-flash
    fallback: mathpix
    local_dev: surya

  languages: [he, ar, en]
  default_language: he
```

---

## 4. Processing Pipeline

### Stage 1: File Acquisition

- **S3 Event Notification** → EventBridge → SQS queue (production)
- **Chokidar v5** file watcher (local development)
- **Idempotency**: SHA-256 of file content checked in Redis before processing. Duplicate files skipped.
- **Partial upload handling**: `awaitWriteFinish` / S3 multipart completion event
- **Dead letter queue**: Failed files moved to `s3://cena-ingest/failed/` with error metadata

### Stage 2: OCR + Text Extraction

| Scenario | Tool | Cost | Accuracy |
|---|---|---|---|
| Printed PDF (clean) | Gemini 2.5 Flash | $0.0003-0.001/page | High |
| Printed PDF (scanned, older) | Gemini 2.5 Flash + retry with Mathpix | $0.005/page fallback | High |
| Photo of printed page | Gemini 2.5 Flash | $0.0003/image | Good-High |
| Photo of handwritten math | Mathpix | $0.002/image | Moderate-Good |
| Complex embedded equations | Mathpix (LaTeX specialist) | $0.005/equation | Best-in-class |

**Output**: Raw text + LaTeX math expressions per page, with language detection.

### Stage 3: Question Segmentation

- **LLM-first extraction** (Gemini 2.5 Flash with structured JSON output)
- Detects question numbering, sub-parts (a→b→c), point allocations
- Preserves multi-part dependency chains
- Diagrams described as metadata (not extracted as images at this stage)
- **Output**: Array of individual question items in raw JSON

### Stage 4: Normalization

Maps raw extraction into the Cena Item Schema:

```typescript
interface CenaItem {
  item_id: string;              // SHA-256 content hash
  item_type: ItemType;          // mcq | open_numeric | open_expression | proof | function_investigation | ...

  source: {
    type: 'bagrut_exam' | 'textbook' | 'teacher' | 'generated' | 'uploaded';
    year?: number;
    questionnaire_code?: string;
    question_number?: number;
    url?: string;
    upload_path?: string;
    extraction_confidence: number;  // 0-1
  };

  content: {
    stems: LocaleText;            // { he: "...", ar: "..." }
    math_expressions: Record<string, string>;  // { f: "f(x) = x^3 - 3x^2 + 4" }
    diagrams: DiagramRef[];
    sub_parts: SubPart[];
  };

  answers: {
    correct_value: string;
    sympy_expr?: string;
    display_latex: string;
    partial_credit_rubric?: RubricStep[];
  };

  distractors?: Distractor[];     // MCQ only

  classification: {
    bloom_level: BloomLevel;
    bloom_confidence: number;
    webb_dok: 1 | 2 | 3 | 4;
    difficulty: number;           // 0-1
    depth_unit: 3 | 4 | 5;       // Bagrut unit level
    concept_ids: string[];
    prerequisite_concept_ids: string[];
    common_misconceptions: string[];
  };

  quality_scores: {
    math_correctness: number;     // 0-1, from SymPy
    language_quality: { he: number; ar: number };
    pedagogical_quality: number;
    overall: number;
  };

  provenance: {
    extracted_from?: string;      // source item_id
    generated_by?: string;        // model name
    modeled_after?: string[];     // source item_ids that inspired generation
    similarity_score?: number;    // max similarity to any corpus item
    reviewed_by?: string;         // reviewer UID
    review_date?: string;
    sympy_validated: boolean;
  };

  dedup_hashes: {
    exact_sha256: string;
    structural_ast_hash?: string;
    semantic_embedding?: number[];  // stored separately in vector index
  };

  multi_part?: {
    group_id: string;
    sequence: number;
    depends_on: string[];
    score_weight: number;
  };

  status: 'ingested' | 'normalized' | 'classified' | 'draft' | 'in_review' | 'approved' | 'rejected' | 'published' | 'deprecated';

  timestamps: {
    ingested: string;
    normalized?: string;
    classified?: string;
    submitted_for_review?: string;
    reviewed?: string;
    published?: string;
  };
}
```

### Stage 5: Quality Classification

| Check | Method | Accuracy | What It Catches |
|---|---|---|---|
| Math correctness | SymPy `simplify()` + `solve()` | 100% (deterministic) | Wrong answers, unsolvable questions |
| Bloom's level | SVM + TF-IDF classifier | ~94% | Misclassified cognitive demand |
| Webb's DOK | LLM classification | ~85% | Depth of knowledge mismatch |
| Difficulty | LLM calibrated to Bagrut | ~80% | Questions too easy/hard for unit level |
| Hebrew quality | LLM + terminology glossary lookup | ~90% | Wrong terms, grammar errors |
| Arabic quality | LLM + glossary + CAMeL morphology | ~85% | Gender agreement, terminology |
| Pedagogical quality | LLM + heuristics | ~85% | Ambiguous, poorly worded, trick questions |
| Plagiarism | 3-level dedup (hash + AST + embedding) | ~95% | Verbatim copies, structural duplicates |

### Stage 6: Deduplication

Three levels, checked in order (cheapest first):

1. **Exact**: SHA-256 of normalized content → Redis SET, O(1)
2. **Structural**: Normalize math AST (replace variables/constants with placeholders) → hash → Redis SET
3. **Semantic**: mE5-large embedding → cosine similarity > 0.92 = duplicate

Cross-language dedup is automatic at the structural level (math ASTs are language-independent).

### Stage 7: Re-Creation (Original Question Generation)

For each ingested question with quality > 0.8:

1. Extract the **style pattern** (question type, sub-part structure, Bloom's level, concept coverage, phrasing pattern, difficulty)
2. Feed the pattern to GPT-4o/Claude as a generation prompt with constraints (different function/numbers, must be solvable, integer coefficients)
3. Generate in Hebrew AND Arabic independently (not translated)
4. Validate with SymPy
5. Check plagiarism against full corpus
6. Route to moderation queue

---

## 5. Storage Architecture

| Store | What | Why |
|---|---|---|
| **S3** (`cena-ingest/`) | Raw uploaded files, processed artifacts | Durable, cheap, event-driven |
| **S3** (`cena-content/items/`) | Normalized item JSON blobs | Content-addressed (hash = key), immutable |
| **Redis** | Dedup hash sets, processing state, metadata indexes | Fast lookup, idempotency |
| **PostgreSQL** (Marten) | Queryable item data, review workflow state | Joins with student records, ACID transactions |
| **Neo4j** | Concept linkages, prerequisite edges for published items | Graph traversal for adaptive learning |

**Cost**: ~$30/month for the initial corpus.

---

## 6. Events (NATS Subjects)

| Event | When | Consumers |
|---|---|---|
| `cena.ingest.file.received` | New file in S3 | Ingestion Lambda |
| `cena.ingest.item.extracted` | Question segmented from document | Normalization service |
| `cena.ingest.item.normalized` | Schema populated | Classification service |
| `cena.ingest.item.classified` | Quality scores computed | Dedup service |
| `cena.ingest.item.deduplicated` | Dedup complete, item is unique | Review queue / Recreation service |
| `cena.ingest.item.recreated` | New original question generated | Validation service |
| `cena.review.item.submitted` | Item enters moderation queue | Moderator UI |
| `cena.review.item.approved` | Moderator approves | Publication pipeline |
| `cena.review.item.rejected` | Moderator rejects | Rejection tracker |
| `cena.review.item.edited` | Moderator edits and approves | Publication pipeline |
| `cena.serve.item.published` | Item enters production pool | Cache invalidation, actor hot-reload |

---

## 7. Moderation Workflow

### Roles

| Role | Permissions | Who |
|---|---|---|
| **Content Admin** | Upload files, submit URLs, trigger batch ingestion | Cena team |
| **Moderator** (Subject Expert) | Review queue: approve, edit, reject items. View quality scores. | Licensed Bagrut teacher |
| **Curriculum Lead** | Escalation target. Override moderator decisions. Set thresholds. | Senior education advisor |
| **System** | Automated classification, dedup, SymPy validation | Pipeline |

### Review States

```
ingested → normalized → classified → draft
                                       │
                                       ├──► in_review (assigned to moderator)
                                       │       │
                                       │       ├──► approved → published
                                       │       ├──► approved_with_edits → published
                                       │       └──► rejected (with reason)
                                       │               │
                                       │               └──► regenerated → draft (re-enters queue)
                                       │
                                       └──► auto_approved (quality > 0.95 AND SymPy verified AND extracted from published Bagrut)
```

### Auto-Approval Criteria

Items skip human review if ALL conditions are met:
- `quality_scores.overall >= 0.95`
- `quality_scores.math_correctness == 1.0` (SymPy verified)
- `source.type == 'bagrut_exam'` (from official published exam)
- `dedup_hashes.semantic_embedding` similarity to any existing approved item < 0.5 (genuinely new)
- `classification.bloom_confidence >= 0.90`

Auto-approved items are flagged for spot-check (random 10% sent to moderator anyway).

### Rejection Escalation

| Rejection Rate | Action |
|---|---|
| < 20% per concept cluster | Normal — pipeline quality is good |
| 20-40% | Alert to Content Admin — review prompt templates for this concept |
| > 40% | Escalate to Curriculum Lead — pause generation for this concept, manual investigation |

---

## 8. Serving Architecture

### How Published Items Reach Students

```
Published Item (PostgreSQL + Neo4j)
       │
       ▼
Question Pool Actor (in-memory, per subject)
       │
       ├── Student requests next question
       │     │
       │     ▼
       │   Methodology Router (BKT + MCM)
       │     │
       │     ├── Selects concept (based on mastery state)
       │     ├── Selects question (based on Bloom's level, difficulty, history)
       │     └── Filters: never repeat same question, respect spaced repetition schedule
       │
       ▼
Delivery API (REST + SignalR)
       │
       ├── GET /api/session/{id}/next-question
       │     Response: { item_id, content (localized), interaction_type }
       │
       ├── POST /api/session/{id}/answer
       │     Request: { item_id, student_answer, time_spent_ms }
       │     Response: { correct, feedback, next_question }
       │
       └── SignalR: real-time push for collaborative sessions
```

### Question Selection Algorithm

1. **BKT** determines which concept the student should work on next (lowest mastery, highest learning gain)
2. **MCM** (Multi-Criteria Methodology) selects the pedagogical approach (direct instruction, scaffolded, Socratic)
3. **Question selector** picks from the concept's question pool:
   - Filter by Bloom's level appropriate to student's mastery phase
   - Filter by difficulty within student's zone of proximal development (ZPD)
   - Exclude questions student has seen in the last N sessions
   - Prefer questions with high quality scores
   - Balance item exposure (don't over-use high-rated items)
4. **Localization**: Serve in student's preferred language (Hebrew or Arabic). Math expressions identical in both.

### Caching for Serving

| Layer | Cache | TTL | Invalidation |
|---|---|---|---|
| Actor in-memory | Full question pool per subject | Session lifetime | NATS `cena.serve.item.published` triggers reload |
| Redis | Frequently-served items by concept | 1 hour | On publish event |
| CDN | Static diagram SVGs | 24 hours | Version in URL |

### Serving Quality Gates

Before any item is served to a student:
- `status == 'published'` (mandatory)
- `quality_scores.math_correctness == 1.0` (mandatory)
- `quality_scores.overall >= 0.80` (configurable threshold)
- Item has been published for >= 24 hours (cool-off period for spot-check)
- Item has not been flagged by student reports

---

## 9. Metrics & Monitoring

| Metric | Target | Alert Threshold |
|---|---|---|
| Ingestion latency (file → classified) | < 30 seconds | > 2 minutes |
| OCR accuracy (sampled) | > 95% | < 90% |
| Classification agreement with expert | > 85% | < 75% |
| Dedup false positive rate | < 1% | > 5% |
| Moderator queue depth | < 200 items | > 500 items |
| Rejection rate (overall) | < 25% | > 40% |
| Question pool coverage (concepts with >= 8 questions) | > 90% | < 80% |
| Student "report question" rate | < 0.5% | > 2% |
