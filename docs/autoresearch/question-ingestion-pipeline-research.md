# Question Ingestion, Normalization, and Quality Classification Pipeline -- Research Findings

> **Status:** Research complete
> **Date:** 2026-03-27
> **Applies to:** Content Authoring Context, CNT-002 (Question Generation), CNT-004 (QA Pass)
> **Purpose:** State-of-the-art research for building an automated pipeline that ingests exam questions from URLs/files, OCRs them, normalizes to a structured schema, classifies quality, deduplicates, and caches results.

---

## Executive Summary

This document covers seven research areas for Cena's question ingestion pipeline. The core recommendation is a **two-tier OCR strategy**: use **Gemini 2.5 Pro** (or GPT-4o) as the primary vision model for Hebrew/Arabic math document OCR, with **Mathpix** as a specialized fallback for high-fidelity LaTeX extraction from complex equations. For question segmentation, LLM-based extraction outperforms traditional layout analysis for exam-style documents. The normalized schema should extend QTI v3 with Bagrut-specific fields. Quality classification combines SymPy verification (mathematical correctness) with LLM-based scoring (pedagogical quality, Bloom's classification). Deduplication uses a two-phase approach: structural hashing for exact/near duplicates, then embedding similarity for semantic duplicates. File ingestion uses S3 event notifications for cloud or chokidar for local development. Caching is content-addressed (SHA-256 of normalized form) with Redis metadata and S3 blob storage.

---

## A. Vision/OCR Models for Hebrew/Arabic Math

### A.1 Model Comparison Matrix

| Model | Hebrew | Arabic | Math/LaTeX | Handwritten | RTL+LTR Mixed | Pricing (per page equiv.) | Self-Hosted | Notes |
|-------|--------|--------|------------|-------------|----------------|--------------------------|-------------|-------|
| **Gemini 2.5 Pro** | Good | Good | Excellent (LaTeX output) | Good | Good | ~$0.0003-0.001/page (token-based, ~258 tokens/image) | No | Best price/performance for OCR. 1M context window allows batch processing. |
| **GPT-4o** | Good | Good | Good (LaTeX with prompting) | Good | Good | ~$0.001-0.003/page (token-based) | No | Higher cost than Gemini. May miss math symbols per OpenAI system card. |
| **Gemini 3 Pro/Ultra** | Excellent | Excellent | Excellent | Very Good | Excellent | TBD (newer model) | No | Gemini 3 cited as "major leap" for OCR, can convert handwritten math to LaTeX. Ultra is current LMArena leader for multilingual OCR. |
| **Mathpix** | Limited | Limited | Best-in-class | Excellent | N/A (math only) | $0.005/page (first 1M), $0.0035 after | No | Gold standard for math-to-LaTeX. Handles handwritten equations better than any VLM. Not a general OCR -- math/STEM focused. |
| **Google Cloud Document AI** | Supported (200+ langs) | Supported | Basic (no LaTeX) | Moderate | Moderate | ~$0.0015-0.006/page | No | Enterprise OCR. Arabic/Chinese "need manual verification" per Google docs. No native math-to-LaTeX. |
| **Azure AI Document Intelligence** | Supported | Supported | Basic (no LaTeX) | Moderate | Moderate | ~$0.001-0.01/page | No | Supports Arabic script. Similar limitations to Google -- no native math expression parsing. |
| **Tesseract 5 + language packs** | Poor-Moderate | Poor-Moderate | None | Poor | Poor | Free (self-hosted) | Yes | Reverses Hebrew/Arabic word order during recognition. No diacritics support. No math. Suitable only as a cheap pre-filter. |
| **Nougat (Meta)** | Not trained | Not trained | Good (academic docs) | Not designed for | N/A | Free (CC-BY-NC) | Yes | Trained on English academic PDFs only. Outputs Markdown with LaTeX. Hallucinates on small images. Not suitable for Hebrew/Arabic. |
| **GOT-OCR 2.0** | Limited | Limited | Good (formulas, TikZ) | Limited | Untested | Free (open-source, 580M params) | Yes | Unified end-to-end model. Handles math/molecular formulas, tables, charts. 1M+ HuggingFace downloads. Multilingual coverage unclear for Hebrew/Arabic. |
| **Surya** | Supported (90+ langs) | Supported (90+ langs) | Good (inline math, LaTeX) | Good (dedicated mode) | Good | Free (GPL-3.0 for research) | Yes | Best open-source option. Layout analysis + OCR + reading order + LaTeX OCR. Texify math model integrated. Hebrew/Arabic in 90+ language support. |

### A.2 Recommendation: Two-Tier Strategy

**Tier 1 -- Primary: Gemini 2.5 Pro (or GPT-4o)**

For full-page document processing including Hebrew/Arabic text + embedded math:
- Send entire page images to Gemini 2.5 Pro with a structured prompt requesting JSON output containing the extracted text (in original language), math expressions (in LaTeX), and document structure
- Gemini handles RTL text + LTR math interleaving well with prompting
- Cost-effective at ~$0.0003-0.001 per page
- Can process batches of pages in a single 1M-context request
- For Cena's use case (Israeli Bagrut exams, ~15 years of PDFs): approximately 500-2000 pages total, costing $0.50-$2.00 total with Gemini

**Tier 2 -- Math Specialist Fallback: Mathpix**

For equations that Gemini fails on or for high-confidence LaTeX extraction:
- When Gemini's LaTeX output fails SymPy parsing, re-extract the equation region with Mathpix
- Mathpix at $0.005/page is 5-17x more expensive than Gemini but achieves best-in-class accuracy on handwritten and complex printed math
- Use selectively: only for failed equations, not for full pages
- Estimated usage: 5-15% of total pages need Mathpix fallback

**Development/Offline: Surya (open-source)**

For local development and testing without API costs:
- Surya provides layout analysis + OCR + math recognition in 90+ languages including Hebrew and Arabic
- GPL-3.0 license suitable for internal tools (not for embedding in shipped product without compliance)
- Integrated LaTeX OCR from the deprecated Texify model
- Can run on a single GPU or CPU (slower)

### A.3 Hebrew RTL + LTR Math: Practical Approach

Based on Cena's existing research in `docs/arabic-math-education-research.md`:

1. **Israeli math is LTR**: Both Hebrew and Arabic Bagrut exams use left-to-right mathematical notation (standard international convention). No Maghreb RTL-math support needed.
2. **Bidi isolation**: When extracting text, wrap math expressions with Unicode bidi isolation characters (U+2066 LRI / U+2069 PDI) or mark them as LTR inline islands.
3. **Numeral system**: Israeli Arab students use Western Arabic numerals (0-9), not Eastern Arabic-Indic numerals. No numeral conversion needed.
4. **Prompt engineering for VLMs**: Include explicit instructions: "The document is in [Hebrew/Arabic] with RTL text flow. Mathematical expressions are in LTR. Extract text preserving the original language. Extract all math expressions as LaTeX."

### A.4 Handwritten vs Printed

- **Printed Bagrut exams** (primary corpus): Any VLM handles these well. Gemini 2.5 Pro is sufficient.
- **Handwritten student work**: Not in scope for ingestion pipeline (students submit answers digitally). If needed later, Mathpix is the clear winner for handwritten math.
- **Teacher worksheets (scanned)**: Quality varies. Surya's handwriting detection mode or Gemini with explicit "this is a scan" prompting works adequately.

---

## B. Question Extraction from Multi-Question Documents

### B.1 The Problem

A typical Bagrut math exam PDF contains 5-8 questions, each potentially with sub-parts (a, b, c). Questions may include:
- Hebrew/Arabic instructional text
- Mathematical equations and expressions
- Geometric diagrams and graphs
- Tables and data sets
- Scoring annotations (e.g., "20 points")

### B.2 Approaches Compared

| Approach | Accuracy | Complexity | Best For |
|----------|----------|------------|----------|
| **LLM-based extraction** (send full page to VLM, ask for structured JSON per question) | 90-95% | Low | Exam-style documents with clear question numbering |
| **Layout analysis + rule-based splitting** (detect question number patterns, split at boundaries) | 70-85% | Medium | Well-structured documents with consistent formatting |
| **Document layout models** (YOLOv8, DocLayNet, LayoutLMv3) | 75-90% | High | Mixed-format documents, tables, figures |
| **Hybrid: layout detection + LLM refinement** | 92-97% | Medium-High | Production systems needing high reliability |

### B.3 Recommended Approach: LLM-First Extraction

For Bagrut exam PDFs, the most effective approach is LLM-first because:

1. **Exam papers have predictable structure**: Questions are numbered (1, 2, 3...) with sub-parts (a, b, c or alef, bet, gimel in Hebrew). LLMs understand this structure natively.
2. **Context matters**: A VLM can understand that a diagram belongs to question 3b, not question 4a, based on spatial proximity and textual references. Rule-based systems struggle with this.
3. **Multi-part dependency**: LLMs can identify that part (b) depends on the result from part (a) -- critical for Cena's question bank.

**Implementation pattern:**

```
Step 1: PDF -> Page images (pdf2image / PyMuPDF)
Step 2: Each page -> Gemini 2.5 Pro with structured extraction prompt
Step 3: Prompt requests JSON array of questions with:
        - question_number, sub_part
        - text_he or text_ar (original language)
        - math_expressions[] (LaTeX)
        - has_diagram (bool)
        - diagram_description (if applicable)
        - points (scoring weight)
        - depends_on (reference to prior sub-part)
Step 4: Merge cross-page questions (question starts on page 3, continues on page 4)
Step 5: Validate extraction completeness (expected N questions based on exam metadata)
```

### B.4 Handling Mixed Content

| Content Type | Extraction Strategy |
|-------------|---------------------|
| **Text + inline math** | VLM extracts both; math goes to LaTeX, text to original language |
| **Diagrams/graphs** | VLM describes the diagram in structured form (e.g., "parabola with vertex at (2,3)"). Diagram image is cropped and stored as a separate asset. |
| **Tables** | VLM extracts as structured data (rows/columns). Alternatively, Surya's table recognition mode. |
| **Multi-part questions** | VLM identifies dependency chain. Output includes `depends_on` field linking sub-parts. |
| **Scoring rubrics** | Often appear as margin annotations or separate answer key pages. Extract separately and link to questions. |

### B.5 Cross-Page Question Merging

When a question spans multiple pages:
1. Extract each page independently
2. Detect continuation markers: question number without a new stem, or text starting with lowercase/continuation
3. Merge: concatenate text, combine math expressions, merge diagrams
4. Validate: the merged question should have exactly one stem and a coherent structure

---

## C. Normalized Question Schema

### C.1 Standards Landscape

| Standard | Scope | Math Support | Adoption | Relevance to Cena |
|----------|-------|-------------|----------|-------------------|
| **QTI v3.0** (1EdTech) | Assessment items, tests, results | MathML via HTML5 | Dominant in LMS/assessment industry | High -- use as structural foundation |
| **IEEE LOM** (Learning Object Metadata) | Metadata for learning resources | Minimal | Broad but aging | Low -- too generic for assessment items |
| **SIF** (Schools Interoperability Framework) | K-12 data exchange | Basic | US/AU K-12 | Low -- focused on student data, not content |
| **Ed-Fi v6.0** | K-12 operational data | Assessment results only | Growing (US K-12) | Low -- tracks scores, not question content |
| **CEDS** (Common Education Data Standards) | Data dictionary for education | Via assessment domain | Federal reference | Low -- vocabulary standard, not content format |

**Recommendation**: Build a **QTI-inspired but Cena-specific schema**. QTI v3 is the closest standard but is XML-heavy and designed for interoperability between LMS platforms. Cena needs a leaner JSON schema optimized for internal use, with QTI export capability added later if needed for partnerships.

### C.2 Proposed Normalized Question Schema

This schema extends what is already defined in `contracts/llm/acl-interfaces.py` (the `AnswerEvaluationRequest` family) and aligns with the question types in `docs/assessment-specification.md`.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "CenaIngestedQuestion",
  "description": "Normalized schema for questions ingested from external sources (exams, worksheets, uploads)",
  "type": "object",
  "required": ["id", "source", "content", "classification", "status"],
  "properties": {
    "id": {
      "type": "string",
      "description": "Content-addressed ID: SHA-256 of canonical form (see C.5)"
    },
    "version": {
      "type": "integer",
      "description": "Schema version for forward compatibility",
      "default": 1
    },
    "source": {
      "type": "object",
      "description": "Provenance tracking",
      "required": ["type", "ingested_at"],
      "properties": {
        "type": { "enum": ["bagrut_exam", "textbook", "worksheet", "user_upload", "generated"] },
        "url": { "type": "string", "format": "uri", "description": "Original URL if applicable" },
        "file_path": { "type": "string", "description": "S3 key of original file" },
        "page_number": { "type": "integer" },
        "question_number": { "type": "string", "description": "Original numbering (e.g., '3b')" },
        "exam_year": { "type": "integer" },
        "exam_session": { "enum": ["winter", "spring", "summer", "moed_a", "moed_b", "moed_c"] },
        "unit_level": { "type": "integer", "enum": [3, 4, 5], "description": "Bagrut unit level" },
        "ingested_at": { "type": "string", "format": "date-time" },
        "ingested_by": { "type": "string", "description": "Pipeline version or user ID" }
      }
    },
    "content": {
      "type": "object",
      "required": ["type", "stem"],
      "properties": {
        "type": {
          "enum": ["mcq", "numeric", "expression", "true_false_justify", "ordering", "proof", "free_text", "graph_sketch"],
          "description": "Aligned with assessment-specification.md question taxonomy"
        },
        "stem": {
          "type": "object",
          "description": "The question prompt",
          "properties": {
            "text_he": { "type": "string", "description": "Hebrew question text" },
            "text_ar": { "type": "string", "description": "Arabic question text" },
            "text_en": { "type": "string", "description": "English (for logging/admin)" },
            "math_expressions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "latex": { "type": "string" },
                  "display_mode": { "type": "boolean", "description": "true=block, false=inline" },
                  "position": { "type": "integer", "description": "Character offset in text where expression appears" }
                }
              }
            },
            "diagrams": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "asset_key": { "type": "string", "description": "S3 key for diagram image" },
                  "alt_text_he": { "type": "string" },
                  "alt_text_ar": { "type": "string" },
                  "diagram_type": { "enum": ["function_graph", "geometric_figure", "number_line", "table", "coordinate_plane", "venn_diagram", "tree_diagram", "other"] },
                  "structured_description": { "type": "object", "description": "Machine-readable diagram params (e.g., function expression, vertices)" }
                }
              }
            }
          }
        },
        "answer": {
          "type": "object",
          "properties": {
            "correct_answer": { "type": "string", "description": "LaTeX or plain text" },
            "correct_answer_latex": { "type": "string" },
            "tolerance": { "type": "object", "description": "For numeric: {type, value}" },
            "unit": { "type": "string" },
            "solution_steps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "step_number": { "type": "integer" },
                  "description_he": { "type": "string" },
                  "expression_latex": { "type": "string" }
                }
              }
            }
          }
        },
        "mcq_options": {
          "type": "array",
          "description": "For MCQ type only. 4 options.",
          "items": {
            "type": "object",
            "properties": {
              "label": { "type": "string", "description": "A/B/C/D or alef/bet/gimel/dalet" },
              "text_he": { "type": "string" },
              "text_ar": { "type": "string" },
              "latex": { "type": "string" },
              "is_correct": { "type": "boolean" },
              "misconception_type": { "enum": ["procedural_slip", "sign_error", "order_of_operations", "conceptual_error", "magnitude_error", "none"] }
            }
          }
        },
        "rubric": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "criterion_id": { "type": "string" },
              "description_he": { "type": "string" },
              "max_points": { "type": "number" },
              "keywords": { "type": "array", "items": { "type": "string" } }
            }
          }
        },
        "sub_parts": {
          "type": "array",
          "description": "For multi-part questions (a, b, c). Each sub_part is a recursive CenaIngestedQuestion.",
          "items": { "$ref": "#" }
        },
        "depends_on": {
          "type": "string",
          "description": "ID of parent question/sub-part if this is a dependent sub-part"
        },
        "points": { "type": "number", "description": "Scoring weight from original exam" }
      }
    },
    "classification": {
      "type": "object",
      "properties": {
        "concepts": {
          "type": "array",
          "description": "Mapped concept IDs from Neo4j knowledge graph",
          "items": { "type": "string" }
        },
        "bloom_level": {
          "enum": ["remember", "understand", "apply", "analyze", "evaluate", "create"],
          "description": "Revised Bloom's taxonomy level"
        },
        "bloom_confidence": { "type": "number", "minimum": 0, "maximum": 1 },
        "difficulty": {
          "type": "number", "minimum": 0, "maximum": 1,
          "description": "0=trivial, 1=extremely hard. Calibrated against Bagrut difficulty distribution."
        },
        "difficulty_method": { "enum": ["heuristic", "irt", "expert", "llm_estimated"] },
        "language": { "enum": ["he", "ar", "en", "he+en", "ar+en"] },
        "bagrut_topic": { "type": "string", "description": "Official Bagrut syllabus topic tag" },
        "prerequisites": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Concept IDs that must be mastered before attempting this question"
        }
      }
    },
    "quality": {
      "type": "object",
      "properties": {
        "overall_score": { "type": "number", "minimum": 0, "maximum": 1 },
        "math_correctness": {
          "type": "object",
          "properties": {
            "verified": { "type": "boolean" },
            "method": { "enum": ["sympy", "llm", "expert", "unverified"] },
            "issues": { "type": "array", "items": { "type": "string" } }
          }
        },
        "language_quality": {
          "type": "object",
          "properties": {
            "score": { "type": "number", "minimum": 0, "maximum": 1 },
            "issues": { "type": "array", "items": { "type": "string" } }
          }
        },
        "pedagogical_quality": {
          "type": "object",
          "properties": {
            "score": { "type": "number", "minimum": 0, "maximum": 1 },
            "is_ambiguous": { "type": "boolean" },
            "is_solvable": { "type": "boolean" },
            "has_single_correct_answer": { "type": "boolean" },
            "issues": { "type": "array", "items": { "type": "string" } }
          }
        },
        "ocr_confidence": {
          "type": "number", "minimum": 0, "maximum": 1,
          "description": "Confidence of the OCR extraction step"
        }
      }
    },
    "deduplication": {
      "type": "object",
      "properties": {
        "content_hash": { "type": "string", "description": "SHA-256 of canonical form" },
        "structural_hash": { "type": "string", "description": "Hash of mathematical structure (variables normalized)" },
        "embedding_vector_key": { "type": "string", "description": "Key to retrieve embedding from vector store" },
        "duplicate_of": { "type": "string", "description": "ID of the canonical question if this is a duplicate" },
        "similarity_score": { "type": "number", "minimum": 0, "maximum": 1 },
        "duplicate_type": { "enum": ["exact", "near_exact", "semantic", "structural", "cross_language", "none"] }
      }
    },
    "status": {
      "enum": ["ingested", "normalized", "classified", "verified", "review_pending", "approved", "rejected", "duplicate"],
      "description": "Pipeline processing status"
    },
    "review": {
      "type": "object",
      "properties": {
        "reviewer_id": { "type": "string" },
        "reviewed_at": { "type": "string", "format": "date-time" },
        "verdict": { "enum": ["approved", "edited", "rejected"] },
        "notes": { "type": "string" }
      }
    }
  }
}
```

### C.3 Math Expression Representation

| Format | Authoring | Rendering | Portable | CAS Compatible | Recommendation |
|--------|-----------|-----------|----------|----------------|----------------|
| **LaTeX** | Easy for math people | MathJax/KaTeX | Excellent | SymPy parses via `parse_latex` | **Primary format** -- store all math as LaTeX |
| **MathML** | Verbose, hard to author | Native browser (MathML Core) | Good | Limited | Generate from LaTeX for web rendering; do not store as primary |
| **AsciiMath** | Very easy, student-friendly | MathJax | Moderate | Limited | Good for student input; convert to LaTeX for storage |

**Decision**: Store math as **LaTeX** (primary) with on-demand conversion to MathML for web rendering (MathJax/KaTeX handles this automatically). This aligns with what Cena's `assessment-specification.md` already specifies for the `<MathInput>` component.

### C.4 Bilingual Question Handling

Per existing Cena architecture (from `docs/arabic-math-education-research.md` and `contracts/llm/prompt-templates.py`):

1. **Math expressions are language-independent**: `x^2 + 3x - 4 = 0` is identical in Hebrew, Arabic, and English versions
2. **Only the natural language text differs**: The instructional wrapper ("Calculate...", "Prove that...", "Find...") is language-specific
3. **Schema approach**: `text_he` and `text_ar` are separate fields; `math_expressions[]` is shared
4. **Generation**: Arabic questions are generated independently (not translated from Hebrew) to avoid subtle errors
5. **Validation**: `extract_math_expressions(q.text_he) == extract_math_expressions(q.text_ar)` -- the math must be identical across languages

### C.5 Content-Addressed ID Generation

```python
import hashlib
import json

def compute_question_id(question: dict) -> str:
    """Generate deterministic content-addressed ID from canonical form.

    Canonical form strips:
    - Whitespace variations
    - Language text (to allow cross-language dedup)
    - Metadata (source, timestamps)

    Preserves:
    - Math expressions (normalized LaTeX)
    - Question type
    - Answer structure
    """
    canonical = {
        "type": question["content"]["type"],
        "math": sorted([e["latex"] for e in question["content"]["stem"].get("math_expressions", [])]),
        "answer": question["content"].get("answer", {}).get("correct_answer_latex", ""),
        "concepts": sorted(question["classification"].get("concepts", [])),
    }
    canonical_json = json.dumps(canonical, sort_keys=True, ensure_ascii=False)
    return hashlib.sha256(canonical_json.encode()).hexdigest()
```

---

## D. Automated Quality Classification

### D.1 Mathematical Correctness (SymPy)

**What SymPy can verify:**
- Algebraic equivalence: `simplify(student_expr - reference_expr) == 0`
- Numeric evaluation at sample points (fallback when symbolic comparison times out)
- Equation solving: verify that stated solutions satisfy the equation
- Derivative/integral correctness: compute and compare
- Limit evaluation
- Matrix operations (determinant, eigenvalues, rank)
- Basic inequality checking
- Trigonometric identity verification

**What SymPy cannot verify (or struggles with):**
- Geometric proofs (no native geometry reasoning)
- Word problems (cannot parse natural language to math)
- Statistical reasoning (limited distribution support)
- Tensor calculus in arbitrary spaces
- Multi-variable inequality systems (solver limitations)
- "Prove that..." questions (cannot construct proofs, only verify specific claims)
- Graph theory problems
- Combinatorial arguments

**Practical coverage for Bagrut math**: SymPy can verify approximately **60-75% of Bagrut 5-unit math questions** automatically. The remaining 25-40% (proofs, geometric reasoning, word problems requiring interpretation) need LLM-based or expert review.

**Integration with existing Cena architecture**: The `assessment-specification.md` already specifies a SymPy sidecar service behind FastAPI with a 2-second timeout and fallback to numerical evaluation at 5 random points. This same service can be reused for ingestion-time verification.

### D.2 LLM-Based Quality Scoring

Based on 2025-2026 research:

| Quality Dimension | Method | Expected Accuracy | Notes |
|------------------|--------|-------------------|-------|
| **Mathematical correctness** (solvability) | SymPy + LLM fallback | 90-95% | SymPy is deterministic; LLM fills gaps |
| **Bloom's taxonomy classification** | SVM with data augmentation | **~94%** (best reported) | SVM outperforms BERT/RoBERTa on small educational datasets. LLMs achieve ~72-73% zero-shot. |
| **Bloom's taxonomy classification** | Fine-tuned RoBERTa | ~86-90% | Needs 500+ labeled examples per Bloom level |
| **Bloom's taxonomy classification** | LLM (GPT-4o/Gemini) zero-shot | ~72-73% | Usable as first pass; insufficient as sole classifier |
| **Difficulty estimation** | LLM + historical data | Moderate (~70-80%) | Best calibrated against actual student performance data (IRT). LLM provides initial estimate. |
| **Language quality (Hebrew)** | LLM (Sonnet/Opus) | High (~90%) | Checks grammar, terminology alignment with Bagrut style, clarity |
| **Pedagogical quality** | LLM + heuristics | Moderate (~80%) | Checks: ambiguity, solvability, single correct answer, distractor quality |
| **Ambiguity detection** | LLM | Good (~85%) | Prompt: "Can this question be interpreted in multiple ways?" |

**Recommended classification pipeline:**

```
1. SymPy verification (math correctness)     -- deterministic, fast
2. Bloom's classification (SVM model)         -- pre-trained, fast
3. Difficulty estimation (LLM, Kimi K2.5)     -- batch, overnight
4. Language quality (LLM, Sonnet 4.6)         -- batch
5. Pedagogical review (LLM, Sonnet 4.6)      -- batch
6. Composite quality score                     -- weighted aggregation
```

### D.3 Bloom's Taxonomy Classification -- Detailed Approach

**Best approach for Cena (given small labeled dataset initially):**

1. **Phase 1 (launch)**: Use LLM zero-shot classification (~73% accuracy) as initial labels. Have education advisor correct misclassifications during expert review.
2. **Phase 2 (after 500+ labeled questions per level)**: Train an SVM classifier on the corrected labels with synonym-based data augmentation. Expected accuracy: ~94%.
3. **Phase 3 (ongoing)**: Ensemble: SVM prediction + LLM prediction, take SVM when confident (>0.85), LLM otherwise. Human review for disagreements.

**Key insight from research**: Deep learning models (BERT, RoBERTa) consistently underperform classical SVM on small educational datasets due to overfitting. Do not over-invest in fine-tuning transformers until the labeled dataset exceeds 2000+ examples.

**Bloom's keyword indicators for Hebrew math questions:**

| Bloom Level | Hebrew Verbs | Example |
|-------------|-------------|---------|
| Remember | הגדר, מנה, ציין | "הגדר מהי פונקציה" |
| Understand | הסבר, תאר, פרש | "הסבר מדוע f'(x)=0 בנקודת קיצון" |
| Apply | חשב, פתור, מצא | "חשב את הנגזרת של f(x)=3x^2+2x" |
| Analyze | השווה, נתח, הבדל | "השווה בין שתי הפונקציות" |
| Evaluate | הוכח, הראה כי, קבע | "הוכח כי f(x) רציפה בקטע [0,1]" |
| Create | בנה, תכנן, צור | "בנה פונקציה שמקיימת את התנאים הבאים" |

### D.4 Detecting Poorly Formed Questions

Automated checks (heuristic + LLM):

| Check | Method | Description |
|-------|--------|-------------|
| **Unsolvable** | SymPy solve returns empty set | Question has no valid solution |
| **Multiple valid answers** | SymPy solve returns multiple solutions where one expected | MCQ has more than one correct option |
| **Trivial** | SymPy solution is immediate (single step) | Question too easy for stated Bloom level |
| **Ambiguous** | LLM prompt: "List all valid interpretations" | Multiple reasonable readings |
| **Missing information** | LLM prompt: "What information is needed to solve this?" | Question omits necessary data |
| **Notation inconsistency** | Regex + LLM | Mixed notation styles within one question |
| **Distractor plausibility** | Magnitude check: distractors within 10x of correct answer | Absurd distractors detected |
| **Language quality** | LLM: "Is this natural Bagrut-style Hebrew?" | Non-standard phrasing |

---

## E. Deduplication Strategies

### E.1 Three-Level Deduplication

| Level | What It Catches | Method | Speed | Example |
|-------|----------------|--------|-------|---------|
| **1. Exact** | Identical questions | SHA-256 of normalized LaTeX + text | O(1) lookup | Same question ingested twice from different sources |
| **2. Structural** | Same math structure, different variables | AST normalization + hashing | O(1) lookup | "Solve 2x+3=7" vs "Solve 3y+5=11" |
| **3. Semantic** | Same concept, different wording | Embedding similarity (cosine > 0.92) | O(log n) with HNSW | "Find the derivative of x^2" vs "Differentiate f(x)=x^2" |

### E.2 Implementation Details

**Level 1 -- Exact Deduplication:**
```python
# Canonical form: strip whitespace, normalize LaTeX, lowercase text
canonical = normalize_latex(question.math) + normalize_text(question.text_he)
content_hash = sha256(canonical.encode()).hexdigest()
# O(1) lookup in Redis SET
if redis.sismember("question_hashes", content_hash):
    mark_as_duplicate(question)
```

**Level 2 -- Structural Deduplication (Math AST):**
```python
import sympy
from sympy import symbols, Wild

def structural_hash(latex_expr: str) -> str:
    """Replace all numeric constants and variable names with placeholders,
    then hash the resulting AST structure."""
    expr = sympy.parse_latex(latex_expr)
    # Replace all Numbers with placeholder
    expr = expr.replace(lambda e: e.is_Number, lambda e: sympy.Symbol('N'))
    # Replace all free symbols with ordered placeholders
    free = sorted(expr.free_symbols, key=str)
    subs = {s: sympy.Symbol(f'v{i}') for i, s in enumerate(free)}
    normalized = expr.subs(subs)
    return sha256(str(normalized).encode()).hexdigest()

# "2x + 3 = 7" and "5y + 1 = 9" both normalize to "N*v0 + N = N"
```

**Level 3 -- Semantic Deduplication:**

Use a math-aware embedding model:
- **Primary**: Embed the question text (Hebrew/Arabic) using a multilingual sentence transformer (e.g., `multilingual-e5-large` or `bge-m3`). These models support Hebrew and Arabic.
- **Enhancement**: Concatenate the LaTeX expression as a suffix to capture mathematical semantics.
- **Index**: Store embeddings in a vector store (HNSW index in Neo4j, or dedicated Qdrant/Weaviate instance, or Redis Vector Search).
- **Threshold**: cosine similarity > 0.92 = likely duplicate; 0.85-0.92 = flag for human review; < 0.85 = distinct.

**Cross-Language Deduplication:**

Since Hebrew and Arabic Bagrut questions are mathematically identical (only the natural language wrapper differs), structural hashing (Level 2) catches cross-language duplicates automatically because it operates on the math AST, which is language-independent.

For semantic deduplication across languages, multilingual embedding models (mE5, BGE-M3) project Hebrew and Arabic text into the same embedding space, so "Calculate the derivative" in Hebrew and Arabic will have high cosine similarity.

### E.3 MinHash/LSH for Scale

For Cena's initial corpus (16,000-30,000 questions), brute-force pairwise comparison is feasible (O(n^2) = ~450M comparisons, doable in minutes with embeddings). MinHash/LSH becomes necessary only at >100K questions.

If scale demands it later:
- Use `datasketch` library for MinHash LSH
- Shingle the question text (character n-grams, n=3-5)
- LSH with Jaccard threshold 0.7 for near-duplicate detection
- Band/row parameters: 20 bands of 5 rows for ~0.7 threshold

### E.4 Deduplication Timing in Pipeline

```
Ingest -> OCR -> Normalize -> [EXACT DEDUP] -> Classify -> [STRUCTURAL DEDUP] -> [SEMANTIC DEDUP] -> Store
                                    |                              |                      |
                              (fastest, first)           (after LaTeX parsed)    (after embedding computed)
```

Run exact dedup immediately after normalization (cheapest). Structural dedup after LaTeX is parsed. Semantic dedup as a batch job after embeddings are generated.

---

## F. File Watching / Directory Monitoring Patterns

### F.1 Architecture Options

| Pattern | Technology | Best For | Latency | Reliability |
|---------|-----------|----------|---------|-------------|
| **S3 Event Notifications + Lambda** | AWS native | Production cloud deployment | 1-5s | High (at-least-once delivery) |
| **S3 + EventBridge + SQS + Lambda** | AWS native | Production with filtering/fan-out | 2-10s | Very high (DLQ support) |
| **Chokidar (Node.js)** | npm library | Local development, on-prem | <1s | Moderate (single-process) |
| **fsnotify/inotify** | OS-level | Linux servers | <1s | High (kernel-level) |
| **Cloud Storage triggers** (GCS, Azure Blob) | Cloud native | Multi-cloud | 1-10s | High |

### F.2 Recommended: S3 Event Notifications (Production)

Aligns with Cena's existing AWS infrastructure (from `tasks/infra/INF-005-s3-cdn.md`):

```
User uploads file -> S3 bucket (s3://cena-content/ingestion/incoming/)
                          |
                    S3 Event Notification (ObjectCreated)
                          |
                    EventBridge rule (filter: *.pdf, *.png, *.jpg, *.jpeg, *.heic)
                          |
                    SQS queue (cena-ingestion-queue)
                          |
                    Lambda / ECS task (ingestion worker)
                          |
                    Processing pipeline
                          |
                    Move to s3://cena-content/ingestion/processed/
                    or      s3://cena-content/ingestion/failed/
```

**Key design decisions:**

1. **SQS as buffer**: Decouple S3 events from processing. SQS provides retry (up to 3 times), visibility timeout (processing window), and dead-letter queue (DLQ) for persistent failures.
2. **Idempotency**: S3 events can be delivered more than once. Use the S3 object key + version as an idempotency key. Check Redis before processing.
3. **Partial upload handling**: Use the `awaitWriteFinish` equivalent -- S3 has "multipart upload complete" events. Only trigger on `s3:ObjectCreated:CompleteMultipartUpload` and `s3:ObjectCreated:Put`, not on `s3:ObjectCreated:*`.
4. **File validation**: First step in Lambda: validate file type, size (<50MB), and basic corruption check (PDF header, image magic bytes).
5. **Two-bucket pattern**: Incoming bucket triggers Lambda. Output goes to a different bucket to avoid infinite loops.

### F.3 Local Development: Chokidar

For the development experience where a teacher drops files into a local folder:

```typescript
import { watch } from 'chokidar';

const watcher = watch('/path/to/incoming/', {
  ignored: /(^|[\/\\])\../, // ignore dotfiles
  persistent: true,
  awaitWriteFinish: {
    stabilityThreshold: 2000, // wait 2s after last change
    pollInterval: 100
  },
  depth: 0 // only watch top level
});

watcher
  .on('add', (path) => processNewFile(path))
  .on('error', (error) => log.error('Watcher error', error));
```

Chokidar v5 (November 2025): ESM-only, requires Node.js 20+, used in ~30M repositories. The `awaitWriteFinish` option is critical for handling large PDF uploads that take seconds to complete.

### F.4 Handling Failure Cases

| Failure | Handling |
|---------|----------|
| **Corrupt PDF** | Validate PDF header. Move to `/failed/` with error log. Alert admin. |
| **Unsupported format** | Check MIME type. Reject with clear error message. |
| **OCR timeout** | Retry up to 3x with exponential backoff (1s, 4s, 16s). Then DLQ. |
| **Partial upload** | `awaitWriteFinish` (local) or S3 multipart completion event (cloud). |
| **Duplicate file** | Content-hash check before full processing. Skip if already processed. |
| **Rate limiting (API)** | SQS visibility timeout provides natural backpressure. Adjust concurrency. |
| **Large file (>50MB)** | Split into pages first, process pages independently, merge results. |

### F.5 URL Ingestion

For URL-based ingestion (teacher pastes a URL to a Bagrut exam PDF):

```
URL submitted -> Validate URL (allowlist: *.education.gov.il, known sources)
             -> Download to S3 (via Lambda)
             -> Same S3 event pipeline as file upload
             -> Cache URL -> S3 key mapping to avoid re-downloading
```

Use `playwright` or `requests` for downloading. For URLs that require JavaScript rendering (some Ministry of Education pages), use a headless browser.

---

## G. Caching Architecture

### G.1 What to Cache and Where

| Data | Store | TTL | Invalidation | Why This Store |
|------|-------|-----|-------------|----------------|
| **Normalized question JSON** | S3 (`s3://cena-content/questions/{hash}.json`) | Indefinite (content-addressed) | Never (immutable by hash) | Cheap, durable, content-addressed |
| **Question metadata index** | Redis (Hash) | Indefinite | On re-processing or schema upgrade | Fast lookup by hash, concept, status |
| **Embedding vectors** | Redis Vector Search or dedicated vector DB | Indefinite | On re-embedding (model change) | Similarity search for dedup |
| **OCR results (raw)** | S3 (`s3://cena-content/ocr-cache/{file-hash}/`) | 90 days | On pipeline version bump | Avoid re-OCRing same files |
| **Source file originals** | S3 (`s3://cena-content/ingestion/originals/`) | Indefinite | Never | Legal/audit trail |
| **Processing status** | Redis (String) | 24h | On completion | Idempotency check, progress tracking |
| **Classification results** | PostgreSQL (Marten) | Indefinite | On re-classification | Queryable, joins with student data |
| **Deduplication hashes** | Redis (Set + Sorted Set) | Indefinite | On corpus rebuild | O(1) duplicate checking |

### G.2 Content-Addressed Storage

The core principle: **the question's ID is its content hash**. This provides:

1. **Automatic deduplication**: Two identical questions from different sources produce the same hash and map to the same stored object.
2. **Immutability**: A stored question never changes. If the normalization pipeline improves, the re-processed question gets a new hash (and therefore a new ID).
3. **Verifiability**: Anyone can recompute the hash from the content to verify integrity.
4. **Cache-friendly**: The hash is the cache key. Cache hits are guaranteed to be correct.

**Versioning when the pipeline improves:**

```
Question in S3:  s3://cena-content/questions/{sha256_hash}.json
Metadata in Redis:  question:{sha256_hash} -> { concepts, bloom_level, ... }
Version mapping:  pipeline_v2:{old_hash} -> {new_hash}
```

When you re-run the pipeline with improved normalization:
1. New hash is computed for the re-normalized question
2. Old hash -> new hash mapping is stored
3. All references in Neo4j/PostgreSQL are updated to point to the new hash
4. Old content remains in S3 for audit (with a `superseded_by` pointer)

### G.3 Redis Key Structure

```
# Question metadata (Hash)
question:{sha256}  ->  { type, concepts, bloom, difficulty, status, source_type, ... }

# Deduplication sets
dedup:exact         ->  SET of content hashes
dedup:structural    ->  SET of structural hashes
dedup:structural_to_canonical:{structural_hash}  ->  canonical question hash

# Processing status (String with TTL)
processing:{file_hash}  ->  "in_progress" | "completed" | "failed"   TTL: 86400

# URL cache (String)
url_cache:{url_sha256}  ->  s3_key   TTL: indefinite

# Pipeline version tracking
pipeline:version  ->  "2.1.0"
pipeline:processed:{file_hash}  ->  "2.1.0"  (which version processed this file)
```

### G.4 Cache Warming and Invalidation

**Warming**: On pipeline startup, load dedup hashes from Redis. If Redis is cold (restart), rebuild from S3 inventory scan (run as background job).

**Invalidation triggers**:
1. **Pipeline version bump**: Re-process all questions. Old results remain accessible but are marked `superseded`.
2. **Schema change**: Migrate Redis metadata via a migration script. S3 objects are immutable -- create new objects with new schema.
3. **Concept graph update**: When Neo4j concept graph is updated (new concepts, changed prerequisites), re-classify affected questions (match by concept ID).
4. **Expert review**: When an expert edits a question, the edited version gets a new hash. The old version is marked `superseded_by: {new_hash}`.

### G.5 Cost Estimation

For Cena's initial corpus (16,000-30,000 questions):

| Resource | Usage | Monthly Cost |
|----------|-------|-------------|
| S3 (questions + originals) | ~500MB | <$0.05 |
| Redis (metadata + dedup) | ~100MB | $15-25 (ElastiCache t4g.micro) |
| OCR API (Gemini, one-time) | ~2000 pages | $0.50-2.00 (one-time) |
| OCR API (Mathpix fallback) | ~200 pages | $1.00 (one-time) |
| LLM classification (Kimi K2.5) | ~30K questions | $5-15 (one-time batch) |
| Embedding generation | ~30K questions | $1-3 (one-time) |

**Total one-time ingestion cost: approximately $25-50 for the entire Bagrut math corpus.**

---

## Architecture Summary: End-to-End Pipeline

```
                    ┌─────────────────────────────┐
                    │         INPUT LAYER          │
                    │                              │
                    │  URL ──> Download ──> S3     │
                    │  File Upload ──────> S3      │
                    │  Hot Folder ──────> S3       │
                    └──────────┬──────────────────┘
                               │ S3 Event
                               ▼
                    ┌─────────────────────────────┐
                    │      INGESTION WORKER        │
                    │                              │
                    │  1. Validate file            │
                    │  2. Check idempotency (Redis)│
                    │  3. PDF -> page images       │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │         OCR LAYER            │
                    │                              │
                    │  Tier 1: Gemini 2.5 Pro      │
                    │    (full page: text + math)  │
                    │  Tier 2: Mathpix (fallback   │
                    │    for failed equations)     │
                    │                              │
                    │  Output: raw text + LaTeX    │
                    │  Cache: S3 (by file hash)    │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │    EXTRACTION & NORMALIZE    │
                    │                              │
                    │  LLM extracts individual     │
                    │  questions from full page    │
                    │  Normalizes to schema (C.2)  │
                    │  Computes content hash       │
                    │                              │
                    │  [EXACT DEDUP CHECK]         │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │    CLASSIFICATION LAYER      │
                    │                              │
                    │  1. SymPy: math correctness  │
                    │  2. SVM/LLM: Bloom's level   │
                    │  3. LLM: difficulty estimate  │
                    │  4. LLM: language quality     │
                    │  5. LLM: pedagogical quality  │
                    │  6. Concept mapping (Neo4j)  │
                    │                              │
                    │  [STRUCTURAL DEDUP CHECK]    │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │     EMBEDDING & DEDUP        │
                    │                              │
                    │  Generate embedding vector   │
                    │  [SEMANTIC DEDUP CHECK]      │
                    │  Store in vector index       │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │      STORAGE LAYER           │
                    │                              │
                    │  S3: question JSON (by hash) │
                    │  Redis: metadata index       │
                    │  PostgreSQL: queryable store  │
                    │  Neo4j: concept linkage      │
                    │                              │
                    │  Status: review_pending      │
                    └──────────┬──────────────────┘
                               │
                               ▼
                    ┌─────────────────────────────┐
                    │      EXPERT REVIEW           │
                    │  (CNT-003 Admin UI)          │
                    │                              │
                    │  Review + approve/edit/reject│
                    │  Approved -> status: approved│
                    │  -> Available to students    │
                    └─────────────────────────────┘
```

---

## Practical Recommendations for Cena

### Immediate Actions (Week 1-2)

1. **Set up the S3 bucket structure** for ingestion (`incoming/`, `processed/`, `failed/`, `originals/`)
2. **Build a minimal OCR proof-of-concept**: Take 10 Bagrut exam PDF pages, send to Gemini 2.5 Pro with a structured extraction prompt, evaluate quality of Hebrew text + LaTeX output
3. **Test Mathpix** on 5 pages with complex equations to establish the fallback quality bar
4. **Define the JSON schema** (Section C.2 above) as a Pydantic model in `contracts/`

### Phase 1 (Week 3-4)

4. **Build the ingestion worker**: S3 event -> SQS -> Lambda/ECS task -> OCR -> normalize -> store
5. **Implement exact deduplication** (SHA-256, Redis SET)
6. **Implement SymPy verification** (reuse existing CAS sidecar from assessment-specification.md)
7. **LLM classification pipeline**: Bloom's (zero-shot first), difficulty, quality scoring via Kimi K2.5 batch

### Phase 2 (Week 5-8)

8. **Structural deduplication** (math AST normalization)
9. **Semantic deduplication** (embedding generation + vector similarity)
10. **Train SVM Bloom's classifier** once 500+ labeled questions are available from expert review
11. **Integration with CNT-003** (Expert Review Tool): ingested questions appear in review queue

### What NOT to Build

- **Do not build a custom OCR model**. VLMs (Gemini/GPT-4o) are good enough and improving rapidly. Custom OCR for Hebrew math is a multi-year research project.
- **Do not implement QTI export yet**. Build the internal schema first; add QTI export when a partnership requires it.
- **Do not use Tesseract** for anything in the pipeline. It produces poor results for Hebrew/Arabic and has no math support.
- **Do not build real-time processing**. Exam ingestion is a batch operation (teacher uploads files, results available in minutes). Do not over-engineer for sub-second latency.

---

## Sources

### Vision/OCR Models
- [Gemini 3 Pro Vision](https://blog.google/technology/developers/gemini-3-pro-vision/)
- [OCR Comparison: GCP, Tesseract, Gemini, GPT-4o](https://blog.gdeltproject.org/ocring-television-news-comparing-gcp-cloud-vision-api-paligemma-tesseract-gemini-1-5-pro-gemini-1-5-flash-gpt-4o/)
- [LMMs vs Classical OCR](https://blog.gdeltproject.org/can-lmms-like-chatgpt-4o-and-gemini-yield-better-results-than-cloud-visions-classical-ai-ocr/)
- [Mathpix API Documentation](https://docs.mathpix.com/)
- [Mathpix Convert API Pricing](https://mathpix.com/pricing/api)
- [Nougat: Neural OCR for Academic Documents (Meta)](https://arxiv.org/abs/2308.13418)
- [GOT-OCR 2.0 (General OCR Theory)](https://github.com/Ucas-HaoranWei/GOT-OCR2.0)
- [Surya OCR (90+ languages)](https://github.com/datalab-to/surya)
- [Texify (deprecated, merged into Surya)](https://github.com/VikParuchuri/texify)
- [Google Cloud Document AI](https://cloud.google.com/document-ai)
- [Azure AI Document Intelligence OCR](https://docs.azure.cn/en-us/ai-services/computer-vision/overview-ocr)
- [Tesseract Hebrew Issues](https://github.com/tesseract-ocr/langdata/issues/82)
- [Tesseract Arabic RTL Issues](https://github.com/tesseract-ocr/tesseract/issues/361)

### Gemini/GPT Pricing
- [Gemini Developer API Pricing](https://ai.google.dev/gemini-api/docs/pricing)
- [Gemini 2.5 Pro Pricing (2026)](https://pricepertoken.com/pricing-page/model/google-gemini-2.5-pro)
- [OpenAI API Pricing](https://openai.com/api/pricing/)
- [GPT-4o Pricing (2026)](https://pricepertoken.com/pricing-page/model/openai-gpt-4o)
- [Gemini for OCR Cost Effectiveness](https://the-rogue-marketing.github.io/why-google-gemini-2.5-pro-api-provides-best-and-cost-effective-solution-for-ocr-and-document-intelligence/)

### Assessment Standards
- [QTI v3.0 Specification (1EdTech)](https://www.1edtech.org/standards/qti/index)
- [QTI Overview (IMS Global)](https://www.imsglobal.org/spec/qti/v3p0/oview)
- [Ed-Fi Data Standard](https://www.ed-fi.org/ed-fi-data-standard/)
- [CEDS (Common Education Data Standards)](https://ceds.ed.gov/relatedInitiatives.aspx)
- [Complete Guide to QTI](https://digitaliser.getmarked.ai/blog/complete-guide-to-qti/)

### Bloom's Taxonomy Classification
- [Automated Analysis of Learning Outcomes (2025)](https://arxiv.org/abs/2511.10903)
- [Bayesian-Optimized Ensemble for Bloom's Classification](https://www.mdpi.com/2079-9292/14/12/2312)
- [BloomBERT Classifier](https://github.com/RyanLauQF/BloomBERT)
- [LLMs Meet Bloom's Taxonomy (COLING 2025)](https://aclanthology.org/2025.coling-main.350.pdf)
- [Automated Question Classification (ERIC)](https://files.eric.ed.gov/fulltext/EJ1413430.pdf)

### LLM Quality Assessment
- [Evaluating LLMs for Automated Scoring (2025)](https://www.mdpi.com/2076-3417/15/5/2787)
- [LLM-Powered Automated Assessment: Systematic Review](https://www.mdpi.com/2076-3417/15/10/5683)
- [LLM-Based Short Answer Grading with RAG (EDM 2025)](https://educationaldatamining.org/EDM2025/proceedings/2025.EDM.short-papers.81/index.html)
- [LLM vs Expert MCQ Assessment](https://www.tandfonline.com/doi/full/10.1080/10872981.2025.2554678)

### SymPy Verification
- [SymPy Official](https://www.sympy.org/)
- [SymCode: Neurosymbolic Mathematical Reasoning](https://arxiv.org/pdf/2510.25975)
- [Step-Wise Formal Verification for LLM Math](https://arxiv.org/pdf/2505.20869)

### Deduplication
- [MinHash LSH in Milvus](https://milvus.io/blog/minhash-lsh-in-milvus-the-secret-weapon-for-fighting-duplicates-in-llm-training-data.md)
- [SemHash: Fast Semantic Deduplication](https://github.com/MinishLab/semhash)
- [Data Deduplication at Trillion Scale (Zilliz)](https://zilliz.com/blog/data-deduplication-at-trillion-scale-solve-the-biggest-bottleneck-of-llm-training)
- [LSHBloom: Internet-Scale Deduplication](https://arxiv.org/pdf/2411.04257)

### File Ingestion
- [Chokidar (file watcher)](https://github.com/paulmillr/chokidar)
- [S3 Event Notifications with Lambda](https://docs.aws.amazon.com/lambda/latest/dg/with-s3.html)
- [S3 to Lambda with EventBridge](https://www.cloudthat.com/resources/blog/building-real-time-amazon-s3-to-aws-lambda-triggers-using-amazon-eventbridge)
- [S3 Event Notifications Guide](https://docs.aws.amazon.com/AmazonS3/latest/userguide/EventNotifications.html)

### Caching
- [ElastiCache Redis with S3](https://aws.amazon.com/blogs/storage/turbocharge-amazon-s3-with-amazon-elasticache-for-redis/)
- [Redis Cache Optimization Strategies](https://redis.io/blog/guide-to-cache-optimization-strategies/)

### Math Representation
- [LaTeX, MathML, AsciiMath Comparison](https://mathlive.io/mathfield/guides/static/)
- [Math Format Converter (MathPad)](https://mathpad.ai/tools/math-format-converter/)
- [AsciiMath Specification](https://asciimath.org/)

### Document Layout Analysis
- [DocLayNet Dataset](https://huggingface.co/HURIDOCS/pdf-document-layout-analysis)
- [PDF Document Layout Analysis (HURIDOCS)](https://github.com/huridocs/pdf-document-layout-analysis)
- [LayoutLM Pre-training](https://arxiv.org/pdf/1912.13318)
