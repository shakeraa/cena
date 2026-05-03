# CNT-008: Question Ingestion Pipeline — Extract, OCR, Normalize, Classify, Dedup, Store

**Priority:** P1 — enables external content ingestion and re-creation pipeline
**Blocked by:** DATA-003 (Neo4j schema), INF-005 (S3), CNT-001 (Math Graph for concept mapping)
**Estimated effort:** 8 days
**Contract:** `docs/question-ingestion-specification.md`, `contracts/llm/prompt-templates.py`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

The Content Authoring pipeline (CNT-001 → CNT-005) generates questions from scratch. This task adds the ability to **ingest questions from external sources** (Bagrut exam PDFs, teacher uploads, student photos, URLs), normalize them into the Cena Item Schema, classify quality, deduplicate, and either store as corpus reference or re-create as original questions.

Source questions are **never served to students**. They feed a style guide. Students only see original, validated, SymPy-verified questions.

## Subtasks

### CNT-008.1: File Watcher + Acquisition Service

**Files to create/modify:**
- `src/Cena.Ingest/FileWatcher/S3EventHandler.cs` — S3 event → SQS consumer
- `src/Cena.Ingest/FileWatcher/UrlFetcher.cs` — download PDF from URL
- `src/Cena.Ingest/FileWatcher/IdempotencyGuard.cs` — SHA-256 dedup on input files
- `config/ingestion/sources.yaml` — S3 bucket, prefixes, supported formats
- `scripts/ingest/local_watcher.py` — Chokidar-based local dev file watcher

**Acceptance:**
- [ ] S3 Event Notification → EventBridge → SQS queue triggers Lambda
- [ ] Supported formats: PDF, PNG, JPG, JPEG, HEIC, WEBP, TIFF (max 50MB)
- [ ] URL fetcher: accepts `https://` URLs, downloads to S3 `incoming/` prefix
- [ ] Idempotency: SHA-256 of file content checked in Redis. Duplicate files return existing item_id, not re-processed
- [ ] Partial upload handling: S3 multipart completion event (not `s3:ObjectCreated:*` which fires on each part)
- [ ] Failed files moved to `s3://cena-ingest/failed/{timestamp}_{filename}` with error JSON sidecar
- [ ] Dead letter queue: SQS DLQ after 3 retries with exponential backoff
- [ ] Local dev: Chokidar v5 watches `./ingest/incoming/` directory
- [ ] Processed files moved to `s3://cena-ingest/processed/`

**Test:**
```csharp
[Fact]
public async Task S3EventHandler_SkipsDuplicateFile()
{
    var hash = "abc123...";
    await _redis.SetAddAsync("ingest:file_hashes", hash);
    var result = await _handler.Process(new S3Event { ObjectKey = "test.pdf", ContentHash = hash });
    Assert.Equal(ProcessResult.Skipped, result.Status);
}

[Fact]
public async Task S3EventHandler_MovesFailedToDeadLetter()
{
    var result = await _handler.Process(new S3Event { ObjectKey = "corrupt.pdf" });
    Assert.Equal(ProcessResult.Failed, result.Status);
    Assert.True(await _s3.ObjectExists("cena-ingest", "failed/corrupt.pdf"));
}
```

---

### CNT-008.2: OCR + Text Extraction Service

**Files to create/modify:**
- `src/Cena.Ingest/OCR/GeminiOcrClient.cs` — Gemini 2.5 Flash API client for page OCR
- `src/Cena.Ingest/OCR/MathpixClient.cs` — Mathpix API client for equation fallback
- `src/Cena.Ingest/OCR/OcrOrchestrator.cs` — primary/fallback routing
- `src/Cena.Ingest/OCR/LanguageDetector.cs` — detect Hebrew/Arabic/English from OCR output
- `config/ingestion/ocr.yaml` — API keys (from secrets), model config, fallback thresholds

**Acceptance:**
- [ ] Gemini 2.5 Flash as primary OCR: send full PDF page as image, receive structured JSON with text + LaTeX
- [ ] Mathpix as fallback: triggered when Gemini extraction confidence < 0.7 or LaTeX parse fails
- [ ] Language detection: auto-detect Hebrew, Arabic, or English from extracted text
- [ ] RTL handling: Hebrew and Arabic text preserved with correct reading order
- [ ] Math expressions extracted as LaTeX strings (not MathML, not Unicode approximation)
- [ ] Mixed BiDi content: LTR math expressions correctly isolated within RTL text
- [ ] Per-page output: `{ page_number, language, text_blocks[], math_expressions[], confidence }`
- [ ] Cost tracking: log API cost per file for budget monitoring
- [ ] Local dev: Surya (open-source) as offline fallback when API keys not configured

**Test:**
```python
def test_ocr_hebrew_math_bidi():
    """Hebrew text with embedded LTR math expression."""
    result = ocr_page("fixtures/bagrut_2022_q3.png")
    assert result.language == "he"
    assert "f(x) = x^3 - 3x^2 + 4" in result.math_expressions.values()
    assert "נתונה הפונקציה" in result.text_blocks[0]

def test_ocr_arabic_parallel():
    """Same question in Arabic should produce same math, different text."""
    he_result = ocr_page("fixtures/bagrut_2022_q3_he.png")
    ar_result = ocr_page("fixtures/bagrut_2022_q3_ar.png")
    assert he_result.math_expressions == ar_result.math_expressions
    assert he_result.text_blocks != ar_result.text_blocks

def test_ocr_fallback_to_mathpix():
    """Complex equation triggers Mathpix fallback."""
    result = ocr_page("fixtures/complex_integral.png")
    assert result.fallback_used == "mathpix"
    assert result.math_expressions["integral"].startswith("\\int")
```

---

### CNT-008.3: Question Segmentation

**Files to create/modify:**
- `src/Cena.Ingest/Segmentation/QuestionExtractor.cs` — LLM-based question segmentation
- `src/Cena.Ingest/Segmentation/SubPartParser.cs` — multi-part dependency chain detection
- `src/Cena.Ingest/Segmentation/ExtractionPrompts.cs` — structured prompts for Gemini

**Acceptance:**
- [ ] LLM-first segmentation: send full page OCR output to Gemini with structured JSON prompt
- [ ] Detects: question number, point allocation, sub-parts (a, b, c...), dependency chains
- [ ] Multi-part dependencies preserved: if part (b) depends on answer from (a), `depends_on: ["a"]`
- [ ] Diagrams referenced by position: `{ diagram_ref: "fig_3", position: "right_of_text" }`
- [ ] Each segmented question has: `question_number`, `points`, `language`, `stem_text`, `math_expressions`, `sub_parts[]`
- [ ] Handles both single-question pages and multi-question pages
- [ ] Confidence score per extracted question (0-1)
- [ ] Items with confidence < 0.6 flagged for manual review

**Test:**
```python
def test_segment_multipart_question():
    page_ocr = load_fixture("bagrut_page_with_3_questions.json")
    questions = segment(page_ocr)
    assert len(questions) == 3
    assert questions[0].sub_parts[1].depends_on == ["a"]
    assert all(q.confidence > 0.7 for q in questions)

def test_segment_preserves_point_allocation():
    page_ocr = load_fixture("bagrut_page_q3_25pts.json")
    questions = segment(page_ocr)
    assert questions[0].points == 25
```

---

### CNT-008.4: Normalizer — Raw Extraction → Cena Item Schema

**Files to create/modify:**
- `src/Cena.Ingest/Normalize/ItemNormalizer.cs` — map raw extraction to CenaItem
- `src/Cena.Ingest/Normalize/MathExpressionExtractor.cs` — isolate LaTeX, create `{math:key}` placeholders
- `src/Cena.Ingest/Normalize/BilingualMapper.cs` — create locale map from detected language
- `src/Cena.Ingest/Normalize/AnswerSolver.cs` — SymPy integration to compute correct answers
- `src/Cena.Data/Content/CenaItemSchema.cs` — TypeScript-equivalent C# record types

**Acceptance:**
- [ ] Maps raw extraction JSON to full `CenaItem` schema (all fields from spec Section 4)
- [ ] Math expressions isolated into shared `math_expressions` dict with `{math:key}` placeholders in text
- [ ] Hebrew and Arabic stems stored in `LocaleText` (`{ he: "...", ar: "..." }`)
- [ ] If source is single-language, the other language field is `null` (filled later by generation or translation)
- [ ] SymPy `solve()` / `simplify()` computes correct answers for numeric and expression questions
- [ ] For proof/investigation questions: answer field contains expected solution steps (from scoring rubric if available)
- [ ] `item_id` = SHA-256 of normalized content (content-addressed)
- [ ] `source` metadata populated: exam year, questionnaire code, question number, URL/path
- [ ] `status` set to `normalized`
- [ ] Emit NATS event `cena.ingest.item.normalized`

**Test:**
```csharp
[Fact]
public void Normalizer_IsolatesMathFromText()
{
    var raw = new RawExtraction { StemText = "נתונה הפונקציה f(x) = x^3 - 3x^2 + 4" };
    var item = _normalizer.Normalize(raw);
    Assert.Contains("{math:f}", item.Content.Stems.He);
    Assert.Equal("f(x) = x^3 - 3x^2 + 4", item.Content.MathExpressions["f"]);
}

[Fact]
public void Normalizer_SolvesWithSymPy()
{
    var raw = new RawExtraction { StemText = "חשב: 2x + 3 = 7", Type = "open_numeric" };
    var item = _normalizer.Normalize(raw);
    Assert.Equal("2", item.Answers.CorrectValue);
    Assert.True(item.Provenance.SympyValidated);
}

[Fact]
public void Normalizer_ContentAddressedId()
{
    var raw = new RawExtraction { StemText = "חשב: 2x + 3 = 7" };
    var item1 = _normalizer.Normalize(raw);
    var item2 = _normalizer.Normalize(raw);
    Assert.Equal(item1.ItemId, item2.ItemId); // Same content = same ID
}
```

---

### CNT-008.5: Quality Classifier — 5-Stage Pipeline

**Files to create/modify:**
- `src/Cena.Ingest/Classify/MathCorrectnessChecker.cs` — SymPy verification
- `src/Cena.Ingest/Classify/BloomClassifier.cs` — SVM + TF-IDF (pre-trained model)
- `src/Cena.Ingest/Classify/WebbDokClassifier.cs` — LLM-based DOK classification
- `src/Cena.Ingest/Classify/DifficultyEstimator.cs` — LLM calibrated to Bagrut levels
- `src/Cena.Ingest/Classify/LanguageQualityChecker.cs` — LLM + glossary validation
- `src/Cena.Ingest/Classify/PedagogicalQualityChecker.cs` — ambiguity, solvability, clarity
- `src/Cena.Ingest/Classify/ClassificationOrchestrator.cs` — runs all 5 stages, aggregates scores
- `models/bloom_svm_classifier.pkl` — pre-trained SVM model for Bloom's classification
- `config/ingestion/classification.yaml` — thresholds, model paths

**Acceptance:**
- [ ] **Stage 1 — Math correctness**: SymPy verifies answer for numeric/expression questions. Covers ~60-75% of Bagrut math. Score: 0 or 1 (deterministic).
- [ ] **Stage 2 — Bloom's level**: SVM classifier with TF-IDF features. Target accuracy: ~94%. Output: bloom_level + bloom_confidence.
- [ ] **Stage 3 — Webb's DOK**: LLM classification. Output: webb_dok (1-4) + confidence.
- [ ] **Stage 4 — Difficulty**: LLM estimate calibrated to Bagrut 3/4/5-unit. Output: difficulty (0-1).
- [ ] **Stage 5 — Language + pedagogy**: LLM checks Hebrew grammar, Arabic gender agreement, terminology correctness, question clarity, solvability.
- [ ] Aggregate `quality_scores.overall` = weighted average (math 0.4, bloom 0.15, difficulty 0.1, language 0.2, pedagogy 0.15)
- [ ] Items with `overall >= 0.95` AND `math_correctness == 1.0` flagged for auto-approval
- [ ] Items with `overall < 0.5` flagged as low-quality, routed to `failed/` not review queue
- [ ] `status` set to `classified`
- [ ] Emit NATS event `cena.ingest.item.classified`

**Test:**
```python
def test_sympy_catches_wrong_answer():
    item = make_item(stem="2x + 3 = 7", answer="3")  # wrong, should be 2
    result = math_checker.verify(item)
    assert result.math_correctness == 0.0

def test_bloom_svm_classifies_application():
    item = make_item(stem="חשב את הנגזרת של f(x) = x^3")
    result = bloom_classifier.classify(item)
    assert result.bloom_level == "application"
    assert result.bloom_confidence > 0.85

def test_overall_score_aggregation():
    scores = QualityScores(math=1.0, bloom=0.9, difficulty=0.8, language=0.85, pedagogy=0.9)
    assert 0.9 <= scores.overall <= 0.95
```

---

### CNT-008.6: Deduplication Service — 3-Level

**Files to create/modify:**
- `src/Cena.Ingest/Dedup/ExactDedupChecker.cs` — SHA-256 Redis SET
- `src/Cena.Ingest/Dedup/StructuralDedupChecker.cs` — math AST normalization + hash
- `src/Cena.Ingest/Dedup/SemanticDedupChecker.cs` — mE5-large embedding + cosine similarity
- `src/Cena.Ingest/Dedup/DedupOrchestrator.cs` — runs all 3 levels in order

**Acceptance:**
- [ ] **Level 1 — Exact**: SHA-256 of normalized `content` field → Redis SET lookup, O(1). Duplicate = identical text and math.
- [ ] **Level 2 — Structural**: Normalize math AST (replace variable names with placeholders, constants with `C`), hash the canonical AST. Catches "same problem, different numbers/variables."
- [ ] **Level 3 — Semantic**: Generate mE5-large embedding of question stem. Cosine similarity > 0.92 against corpus = duplicate. Uses approximate nearest neighbor search (Redis vector or pgvector).
- [ ] Cross-language dedup: structural level is language-independent (math ASTs have no language). Semantic level uses multilingual embeddings.
- [ ] Dedup result stored on item: `dedup_hashes.exact_sha256`, `dedup_hashes.structural_ast_hash`, plus `dedup_hashes.semantic_embedding` stored in vector index.
- [ ] Duplicate items: link to existing item via `provenance.extracted_from`, set `status: 'duplicate'`, do not enter review queue.
- [ ] Near-duplicate items (structural or semantic match): flag as `potential_duplicate`, enter review queue with reference to similar item.

**Test:**
```python
def test_exact_dedup():
    item = normalize("חשב: 2x + 3 = 7")
    store(item)
    duplicate = normalize("חשב: 2x + 3 = 7")
    assert dedup(duplicate).status == "duplicate"

def test_structural_dedup_different_numbers():
    item1 = normalize("חשב: 2x + 3 = 7")  # answer: 2
    item2 = normalize("חשב: 3x + 5 = 14") # answer: 3 — same structure, different numbers
    store(item1)
    assert dedup(item2).status == "potential_duplicate"

def test_cross_language_structural_dedup():
    he_item = normalize("חשב: 2x + 3 = 7")
    ar_item = normalize("احسب: 2x + 3 = 7")
    store(he_item)
    assert dedup(ar_item).structural_match is True
```

---

### CNT-008.7: Re-Creation Engine — Generate Original Questions from Patterns

**Files to create/modify:**
- `src/Cena.Ingest/Recreate/PatternExtractor.cs` — extract style patterns from classified items
- `src/Cena.Ingest/Recreate/QuestionGenerator.cs` — GPT-4o/Claude generation with constraints
- `src/Cena.Ingest/Recreate/PlagiarismChecker.cs` — verify originality against full corpus
- `src/Cena.Ingest/Recreate/RecreationPrompts.cs` — prompt templates for generation
- `config/ingestion/recreation.yaml` — generation model, constraints, similarity thresholds

**Acceptance:**
- [ ] For each ingested item with `quality_scores.overall >= 0.8`, extract style pattern: question_type, sub_part_structure, bloom_level, concepts, phrasing_pattern, difficulty_range
- [ ] Generate 3-5 original questions per pattern using GPT-4o (primary) or Claude (fallback)
- [ ] Each generated question must use DIFFERENT mathematical expressions (different function, different numbers, different coefficients)
- [ ] Generate in Hebrew AND Arabic independently (not translated)
- [ ] SymPy validates math correctness of every generated question
- [ ] Plagiarism check: `similarity_score < 0.5` against full corpus (exact + structural + semantic)
- [ ] `provenance.modeled_after` links to source item(s) that inspired generation
- [ ] `provenance.similarity_score` records max similarity to any corpus item
- [ ] Generated items enter the standard classification pipeline (CNT-008.5) before review
- [ ] `status` set to `draft`

**Test:**
```python
def test_recreated_question_is_original():
    source = ingest("fixtures/bagrut_2022_q3.pdf")
    recreated = recreate(source, count=3)
    for q in recreated:
        assert q.provenance.similarity_score < 0.5
        assert q.content.math_expressions != source.content.math_expressions
        assert q.provenance.sympy_validated is True

def test_recreated_preserves_style():
    source = ingest("fixtures/bagrut_2022_q3.pdf")
    recreated = recreate(source, count=1)
    assert recreated[0].classification.bloom_level == source.classification.bloom_level
    assert recreated[0].item_type == source.item_type
    assert len(recreated[0].content.sub_parts) == len(source.content.sub_parts)

def test_recreated_bilingual():
    source = ingest("fixtures/bagrut_2022_q3.pdf")
    recreated = recreate(source, count=1)
    assert recreated[0].content.stems.he is not None
    assert recreated[0].content.stems.ar is not None
    assert recreated[0].content.stems.he != recreated[0].content.stems.ar
```

---

### CNT-008.8: Ingestion API + Admin Endpoints

**Files to create/modify:**
- `src/Cena.Web/Controllers/IngestController.cs` — REST endpoints for ingestion
- `src/Cena.Web/Controllers/IngestBatchController.cs` — batch ingestion endpoints

**Acceptance:**
- [ ] `POST /api/admin/ingest/url` — submit a URL for ingestion. Returns `{ job_id, status: "queued" }`
- [ ] `POST /api/admin/ingest/upload` — multipart file upload. Returns `{ job_id, item_ids[], status }`
- [ ] `POST /api/admin/ingest/batch` — trigger batch processing of `s3://cena-ingest/batch/` prefix
- [ ] `GET /api/admin/ingest/job/{id}` — job status: `{ status, items_extracted, items_classified, items_failed, errors[] }`
- [ ] `GET /api/admin/ingest/stats` — pipeline stats: total ingested, classified, duplicates, pending review
- [ ] All endpoints require `ADMIN` role (Firebase Auth)
- [ ] Rate limit: 10 URLs/minute, 50 files/hour per admin user

**Test:**
```csharp
[Fact]
public async Task IngestUrl_ReturnsJobId()
{
    var response = await _client.PostAsJsonAsync("/api/admin/ingest/url",
        new { url = "https://meyda.education.gov.il/sheeloney_bagrut/2022/2/HEB/35582.pdf" });
    response.EnsureSuccessStatusCode();
    var job = await response.Content.ReadFromJsonAsync<IngestJob>();
    Assert.NotNull(job.JobId);
    Assert.Equal("queued", job.Status);
}
```
