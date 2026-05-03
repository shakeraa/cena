# Assessment Item Schema Research -- Standards, Formats, and Practical Design for Cena

> **Status:** Research
> **Date:** 2026-03-27
> **Purpose:** Inform the normalized question schema for Israeli Bagrut math exam questions in Hebrew and Arabic
> **Audience:** Architecture, Content Authoring, Engineering

---

## 1. IMS QTI 3.0 (Question and Test Interoperability)

### 1.1 Current State

QTI 3.0 is the current version of the 1EdTech (formerly IMS Global) standard for representing assessment items and tests. It is the dominant interoperability standard in education technology and is used by platforms such as Canvas, Moodle, TAO, Learnosity, and most state assessment systems.

**Core specification documents:**
- Assessment, Section and Item Information Model v3.0
- Results Reporting Information Model and XSD Binding v3.0
- QTI 3.0 Metadata Specification
- Best Practices and Implementation Guide

**Key changes from QTI 2.x to 3.0:**
- HTML5-native item body (no more custom XHTML subset)
- Native W3C accessibility integration (WAI-ARIA, SSML)
- Streamlined APIP (Accessible Portable Item Protocol) merged into core
- Native Computer Adaptive Testing (CAT) support
- Portable Custom Interactions (PCI) for Technology-Enhanced Items
- CSS-based styling vocabulary (replacing custom rendering hints)

### 1.2 Item Structure (Simplified)

A QTI 3.0 item is an XML document with this essential structure:

```xml
<qti-assessment-item identifier="item-001" title="Chain Rule" xml:lang="he"
    adaptive="false" time-dependent="false">

  <!-- Metadata (LOM-based) -->
  <qti-item-body>
    <!-- HTML5 content with embedded interactions -->
    <p>נתונה הפונקציה <math xmlns="http://www.w3.org/1998/Math/MathML">
      <mi>f</mi><mo>(</mo><mi>x</mi><mo>)</mo><mo>=</mo>
      <mi>sin</mi><mo>(</mo><msup><mi>x</mi><mn>2</mn></msup><mo>)</mo>
    </math>. מהי <math>...</math>?</p>

    <qti-choice-interaction response-identifier="RESPONSE" shuffle="true"
        max-choices="1">
      <qti-simple-choice identifier="A">...</qti-simple-choice>
      <qti-simple-choice identifier="B">...</qti-simple-choice>
      <qti-simple-choice identifier="C">...</qti-simple-choice>
      <qti-simple-choice identifier="D">...</qti-simple-choice>
    </qti-choice-interaction>
  </qti-item-body>

  <qti-response-processing
      template="https://purl.imsglobal.org/spec/qti/v3p0/rptemplates/match_correct"/>

  <qti-response-declaration identifier="RESPONSE" cardinality="single"
      base-type="identifier">
    <qti-correct-response>
      <qti-value>B</qti-value>
    </qti-correct-response>
  </qti-response-declaration>
</qti-assessment-item>
```

### 1.3 Capability Assessment Against Cena Requirements

| Requirement | QTI 3.0 Support | Details |
|---|---|---|
| **Multi-part questions (a->b->c)** | Partial | QTI supports `qti-assessment-section` with ordered items and `branchRule`/`preCondition` for sequencing. However, true Bagrut-style a/b/c chains where part (b) depends on the answer to (a) require either adaptive items or composite items with response-processing dependencies. This is supported but complex to author. |
| **Mathematical expressions (LaTeX/MathML)** | MathML only | QTI 3.0 natively supports MathML 3 embedded in HTML5 item bodies. LaTeX is NOT natively supported -- it must be converted to MathML before packaging. This is a significant authoring friction for content written in LaTeX. |
| **Scoring rubrics with partial credit** | Yes | QTI has `qti-response-processing` with `qti-outcome-declaration` supporting float scores. Custom response processing templates can implement any partial credit logic. However, rubric-based LLM evaluation (as Cena uses) has no QTI representation -- QTI assumes deterministic or template-based scoring. |
| **Multiple languages** | Partial | QTI uses `xml:lang` to declare item language. For bilingual items, the standard approach is to create **separate item variants** per language, linked by metadata. There is no single-item bilingual representation in the spec. The metadata specification supports language fields for localization. |
| **Bloom's taxonomy classification** | Via metadata only | QTI metadata supports custom taxonomic classifications. Bloom's level would be encoded as a metadata field using the LOM (Learning Object Metadata) extension points. Not a first-class schema field. |
| **Prerequisite relationships** | No | QTI has no concept of prerequisite relationships between items. Items are independent units. Prerequisites are a curriculum/knowledge-graph concern outside QTI's scope. |

### 1.4 Limitations for Cena's Use Case

1. **XML-only format.** QTI is XML/XSD-based. Cena's stack is JSON-native (Neo4j, REST APIs, React Native). Every QTI interaction would require XML<->JSON serialization, adding complexity with no benefit since Cena does not need to interoperate with external LMS platforms.

2. **No LLM evaluation model.** QTI's response processing assumes deterministic or template-based scoring. Cena's free-text (Sonnet) and justification (Kimi) evaluation pipelines have no QTI representation.

3. **No adaptive learning metadata.** QTI has no fields for: BKT parameters, spaced repetition half-life, methodology assignment, stagnation signals, error type taxonomy, or engagement signals. All of Cena's intelligence layer metadata would live outside QTI.

4. **Authoring overhead.** QTI items require schema-valid XML with namespace declarations, response-declaration/outcome-declaration boilerplate, and response-processing templates. For a team generating 20,000+ questions via LLM pipeline, this is unnecessary ceremony.

5. **Bilingual is awkward.** The "create separate items per language" approach doubles the item count and requires maintaining cross-references. For Bagrut exams where Hebrew and Arabic versions are mathematically identical, this is wasteful.

6. **MathML-only for math.** LaTeX is the lingua franca of math content creation (LLMs output LaTeX, SymPy outputs LaTeX, textbooks use LaTeX). Forcing MathML as the canonical format adds a conversion step at every boundary.

### 1.5 Verdict: QTI 3.0 Is Not Right for Cena

QTI is designed for **interoperability between platforms** -- when Pearson exports questions to Canvas, or when a state assessment vendor needs to share items across delivery systems. Cena is a vertically integrated platform where questions are authored, stored, served, and evaluated within a single system. The interoperability tax (XML, MathML-only, no adaptive metadata, no LLM evaluation) provides zero benefit.

**When QTI would matter:** If Cena later needs to export questions to an LMS (e.g., selling content to schools that use Moodle), a QTI export adapter can be built as a projection from Cena's internal schema. This is a write-only concern, not a storage concern.

---

## 2. Lighter-Weight Alternatives

### 2.1 Khan Academy Perseus Format

Perseus is Khan Academy's open-source exercise renderer. Its internal item format is JSON-based and widget-oriented.

**Perseus item structure (reconstructed from source):**
```json
{
  "question": {
    "content": "What is the derivative of $f(x) = \\sin(x^2)$?\n\n[[☃ radio 1]]",
    "images": {},
    "widgets": {
      "radio 1": {
        "type": "radio",
        "options": {
          "choices": [
            { "content": "$2x\\cos(x^2)$", "correct": true },
            { "content": "$\\cos(x^2)$", "correct": false },
            { "content": "$2x\\sin(x^2)$", "correct": false },
            { "content": "$x^2\\cos(x^2)$", "correct": false }
          ],
          "randomize": true
        },
        "version": { "major": 1, "minor": 0 }
      }
    }
  },
  "answerArea": { "type": "multiple", "options": {} },
  "hints": [
    { "content": "Remember the chain rule: $(f(g(x)))' = f'(g(x)) \\cdot g'(x)$" },
    { "content": "Here, $f(u) = \\sin(u)$ and $g(x) = x^2$" }
  ],
  "itemDataVersion": { "major": 0, "minor": 1 }
}
```

**Key observations:**
- JSON-native, no XML overhead
- LaTeX via dollar-sign delimiters in Markdown content (rendered by KaTeX/MathJax)
- Widget-based interaction model (radio, numeric-input, expression, etc.)
- Hints are first-class
- No multilingual support, no metadata beyond content
- No scoring rubrics -- scoring is hardcoded per widget type

**Relevance to Cena:** The content model (Markdown + LaTeX + widgets) is practical. But Perseus has no concept of Bloom's taxonomy, prerequisites, adaptive metadata, bilingual support, or error taxonomy. It is a renderer, not a curriculum data model.

### 2.2 Learnosity Item Format

Learnosity is a commercial assessment API platform used by large publishers. Its JSON schema is more complete than Perseus.

**Learnosity question structure (simplified):**
```json
{
  "type": "mcq",
  "reference": "question-001",
  "data": {
    "stimulus": "<p>Given <span class=\"learnosity-math-formula\">f(x) = \\sin(x^2)</span>, find f'(x).</p>",
    "options": [
      { "label": "...", "value": "A" },
      { "label": "...", "value": "B" }
    ],
    "validation": {
      "scoring_type": "exactMatch",
      "valid_response": { "score": 1, "value": ["B"] },
      "alt_responses": [
        { "score": 0.5, "value": ["C"] }
      ]
    },
    "metadata": {
      "rubric_reference": "rubric-001",
      "acknowledgements": "Based on Bagrut 2024 Q3"
    }
  }
}
```

**Key features:**
- JSON-native with rich validation schema
- Partial credit via `alt_responses` with fractional scores
- RTL support via API configuration (`"language": "he"`, direction: RTL)
- Custom metadata extensible
- Math via LaTeX in HTML spans (rendered by MathJax)
- QTI import/export via `learnosity-qti` converter

**Relevance to Cena:** Closer to what Cena needs. The validation model supports partial credit. RTL support exists. But it is still a delivery-focused schema without curriculum graph, adaptive learning, or bilingual-by-design features.

### 2.3 Moodle Question Bank Schema

Moodle's question bank is the most widely deployed open-source assessment database. Its schema is relational:

**Core tables:**
- `question` -- id, name, questiontext, questiontextformat, generalfeedback, defaultgrade, penalty, qtype, hidden, stamp, version, timecreated, timemodified
- `question_answers` -- id, question, answer, answerformat, fraction, feedback, feedbackformat
- `question_categories` -- id, name, info, parent, sortorder, stamp, contextid
- Type-specific tables: `qtype_multichoice_options`, `qtype_numerical_options`, etc.

**Key observations:**
- `fraction` field on answers supports partial credit (0.0 to 1.0, can be negative for wrong answers)
- `penalty` field supports hint-based scoring degradation
- Categories support hierarchical organization
- No built-in Bloom's taxonomy, prerequisite, or adaptive learning fields
- Multilingual via Moodle's `multi-lang` filter (inline `<span lang="he">...</span>`)
- Math via MathJax filter (LaTeX in `$$..$$` delimiters)

**Relevance to Cena:** Moodle's schema is battle-tested for question storage but is LMS-centric. Its relational model does not map well to Neo4j graph storage. The schema lacks all adaptive learning metadata.

### 2.4 OATutor Content Format

From the adaptive learning architecture research already in Cena's docs, OATutor (Berkeley CAHLR Lab) uses a file-based content pool with JSON:

```
content-pool/
  problem-id/
    problem-id.json          # metadata, answer verification
    steps/
      problem-ida/
        problem-ida.json     # step definition
        tutoring/
          defaultPathway.json # hint sequence
```

Each problem JSON contains skill mappings that connect to BKT parameters. This is the closest open-source model to what Cena needs but lacks bilingual support, graph storage integration, and the Bagrut-specific metadata.

---

## 3. Common Schema Fields Across Platforms

### 3.1 Field Comparison Matrix

| Field | QTI 3.0 | Perseus (KA) | Learnosity | Moodle | OATutor | **Cena Need** |
|---|---|---|---|---|---|---|
| Unique ID | identifier (string) | N/A (file-based) | reference (string) | id (integer) | problemID | Yes |
| Question text/stem | qti-item-body (HTML5) | content (Markdown) | stimulus (HTML) | questiontext (HTML) | Problem JSON | Yes |
| Question type | interaction type | widget type | type | qtype | implicit | Yes |
| Answer options | qti-simple-choice | widget.options | options[] | question_answers | steps | Yes |
| Correct answer | qti-correct-response | widget.correct | valid_response | fraction=1.0 | answer | Yes |
| Partial credit | outcome float | No | alt_responses | fraction 0-1 | No | Yes |
| Hints | No (separate) | hints[] | No (separate) | penalty + hints | tutoring/ | Yes (3 levels) |
| Math expressions | MathML 3 | LaTeX ($...$) | LaTeX (MathJax) | LaTeX (MathJax) | LaTeX | Yes (LaTeX) |
| Bloom's taxonomy | LOM metadata | No | Custom metadata | No | No | Yes |
| Difficulty level | LOM metadata | No | Custom metadata | No | No | Yes |
| Language | xml:lang | No | language config | multi-lang filter | No | Yes (he/ar) |
| Prerequisites | No | No | No | No | skillModel.json | Yes (graph edges) |
| Scoring rubric | Custom RP | No | rubric_reference | No | No | Yes |
| Error taxonomy | No | No | No | No | No | Yes |
| BKT parameters | No | No | No | No | bktParams.json | Yes |
| Spaced repetition | No | No | No | No | No | Yes |
| Methodology tag | No | No | No | No | No | Yes |
| Diagram spec | img/object | images{} | img/feature | embedded | No | Yes |
| Multi-part chain | Section ordering | No | Activity | Quiz structure | Steps | Yes |
| Source provenance | No | No | acknowledgements | No | No | Yes |

### 3.2 Observations

No existing standard or platform schema covers more than 40% of Cena's required fields. The adaptive learning metadata (BKT, HLR, methodology, error taxonomy, stagnation), bilingual support, and Bagrut-specific fields (depth_unit, bagrut_weight, exam provenance) are entirely absent from all surveyed schemas.

The universal fields across all platforms are: unique ID, question text, question type, correct answer, and some form of scoring. Everything else is platform-specific.

---

## 4. Math Expression Representation

### 4.1 Format Comparison

| Criterion | LaTeX | MathML (Presentation) | MathML (Content) | AsciiMath | OpenMath |
|---|---|---|---|---|---|
| **Human readability** | High | Very low (XML) | Very low (XML) | High | Very low (XML) |
| **Compactness** | High | Very low (~10x LaTeX) | Low (~5x LaTeX) | High | Low |
| **Web rendering** | Via MathJax/KaTeX | Native browser (partial) | Via MathJax | Via MathJax | No direct rendering |
| **Mobile rendering** | KaTeX (fast) | WebView required | WebView required | MathJax (slower) | N/A |
| **SymPy parsing** | Yes (latex2sympy2) | Yes (sympy.parsing.mathml) | Yes (content MathML) | No native parser | No native parser |
| **LLM output format** | Native | Rare | Very rare | Occasional | Never |
| **Authoring tools** | Ubiquitous | Rare (editors exist) | Very rare | Some web editors | Academic only |
| **Round-trip fidelity** | Lossy to/from CAS | Lossless (semantic) | Lossless (semantic) | Lossy | Lossless (semantic) |
| **RTL compatibility** | Good (math is always LTR) | Good | Good | Good | N/A |
| **QTI compatibility** | No (must convert) | Yes (native) | Yes (native) | No | No |
| **Ecosystem adoption** | Dominant (90%+) | W3C standard, declining | Niche | Growing (web) | Academic niche |

### 4.2 Recommendation: LaTeX as Canonical Format

**Store LaTeX, render via KaTeX, verify via SymPy.**

Rationale:

1. **LLM pipeline alignment.** Kimi K2.5 and Claude Sonnet output LaTeX natively. The content generation pipeline (Stage 2 in `docs/content-authoring.md`) generates questions with LaTeX. Storing in any other format requires a conversion step at generation time.

2. **SymPy verification alignment.** The CAS evaluator (SymPy sidecar in `docs/assessment-specification.md` Section 1.3) parses student input as LaTeX via `latex2sympy2`. Storing reference answers as LaTeX means zero conversion between storage and verification.

3. **Rendering alignment.** KaTeX renders LaTeX to HTML/SVG with sub-millisecond performance on mobile. MathJax is slower but also supports LaTeX. Both are React Native compatible. No WebView required for KaTeX.

4. **Compactness.** `\frac{3}{3x+1}` (LaTeX, 16 chars) vs. the equivalent MathML Presentation (approximately 200 chars of XML). For 50,000+ questions, storage and transfer size matters.

5. **Author familiarity.** Every Bagrut math teacher writes LaTeX (or a LaTeX-like notation). The education advisor can read and validate LaTeX expressions directly.

**The one risk:** LaTeX is presentational, not semantic. `\sin^2(x)` in LaTeX could mean `sin(x)^2` or `sin(sin(x))` depending on convention. Mitigation: the SymPy parsing step (`latex2sympy2`) disambiguates at verification time, and the content authoring pipeline validates all expressions via SymPy before publication (Stage 2, step 4).

**Conversion when needed:** If QTI export is ever required, LaTeX-to-MathML conversion is a solved problem (Pandoc, `latex2mathml` Python library, or KaTeX's MathML output mode). This is a write-time concern for an export adapter, not a storage concern.

### 4.3 Expression Storage Convention

```
Inline math: $f(x) = \sin(x^2)$
Display math: $$\int_0^1 f(x)\,dx = \frac{\pi}{4}$$
SymPy-parseable: stored in a separate field as a SymPy-compatible string
```

For CAS-evaluated questions, store TWO representations:
1. `display_latex` -- the human-readable LaTeX for rendering (may include formatting like `\quad`, `\text{}`)
2. `sympy_expr` -- a clean, SymPy-parseable expression string (e.g., `"sin(x**2)"` or `"3/(3*x+1)"`)

This avoids parsing ambiguity: the display version is for humans, the sympy version is for the CAS evaluator.

---

## 5. Bilingual Question Representation

### 5.1 The Hebrew/Arabic Math Problem

Bagrut exams exist in two languages: Hebrew and Arabic. The mathematical content is identical -- only the natural language text differs. This creates a clean separation opportunity.

**Key insight from the corpus strategy (`docs/syllabus-corpus-strategy.md` Section 1.5):** "Arabic Bagrut exams are identical in content to Hebrew exams but phrased in Arabic. The syllabus is the same -- only the language differs."

### 5.2 RTL Considerations

Both Hebrew and Arabic are RTL languages, but mathematical notation is universally LTR. This means:

- Text flows right-to-left
- Math expressions flow left-to-right (always)
- Mixed text+math requires bidirectional handling (Unicode bidi algorithm)
- KaTeX handles this correctly when `dir="rtl"` is set on the containing element
- Diagrams with labels need label text in both languages, but diagram structure is shared

### 5.3 Recommended Representation: Locale Map + Shared Math

Separate natural language from mathematical content at the schema level:

```json
{
  "stem": {
    "he": "נתונה הפונקציה {math:func}. מצא את הנגזרת.",
    "ar": "بالنظر إلى الدالة {math:func}، أوجد المشتقة."
  },
  "math_expressions": {
    "func": "$f(x) = \\sin(x^2)$"
  }
}
```

**Design principles:**

1. **Math placeholders in text.** Natural language text contains `{math:key}` placeholders that reference shared math expressions. This ensures mathematical expressions are authored once and rendered identically in both languages.

2. **Locale map for all text.** Every natural language string is a `{ "he": "...", "ar": "..." }` map. The rendering layer picks the appropriate locale at display time.

3. **Math is never localized.** Mathematical expressions use standard Latin notation regardless of display language. Hebrew and Arabic Bagrut exams both use the same mathematical symbols (standard international notation).

4. **Diagram labels are localized.** Diagram specifications include label text per locale, while geometric/spatial content is shared.

5. **English as fallback.** An optional `"en"` key can serve as a development/debugging fallback and for international content sharing.

### 5.4 Diagram Bilingual Handling

```json
{
  "diagram_id": "trig_unit_circle_01",
  "svg_url": "s3://cena-content/diagrams/trig_unit_circle_01.svg",
  "labels": [
    {
      "label_id": "axis_x",
      "position": [250, 150],
      "text": { "he": "ציר x", "ar": "المحور x" }
    },
    {
      "label_id": "angle",
      "position": [180, 120],
      "text": { "he": "זווית θ", "ar": "الزاوية θ" }
    }
  ]
}
```

The SVG itself contains no text -- all labels are overlaid by the rendering layer using the locale-appropriate text from the labels array.

---

## 6. Practical Schema Design: Cena Assessment Item Schema

### 6.1 Design Constraints

1. **Storage:** Neo4j AuraDB (graph) + PostgreSQL/Marten (event store). Items are nodes in Neo4j with `[:ASSESSES]` edges to Concept nodes.
2. **Languages:** Hebrew (primary) + Arabic (parallel). Math expressions shared.
3. **Question types:** 8 types defined in `docs/assessment-specification.md` Section 1.
4. **Evaluation:** Deterministic, CAS (SymPy), LLM classification (Kimi), LLM free-text (Sonnet).
5. **Adaptive metadata:** BKT parameters, Bloom's level, difficulty Elo, error taxonomy, hint escalation.
6. **Content pipeline:** LLM-generated, expert-reviewed, SymPy-validated.
7. **Scale:** 20,000-50,000 items at MVP (Math + Physics).
8. **Rendering:** React Native (mobile), React (web), RTL layout.

### 6.2 The Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://cena.education/schemas/assessment-item/v1",
  "title": "Cena Assessment Item Schema v1",
  "description": "Normalized schema for a single assessment item. Stored as a Neo4j :Item node with serialized JSON properties. Bilingual by design (Hebrew + Arabic).",

  "type": "object",
  "properties": {

    "item_id": {
      "type": "string",
      "pattern": "^[a-z]+_[0-9]u?_[a-z0-9_]+_[0-9]{3}$",
      "description": "Globally unique item identifier. Convention: {subject}_{depth}u_{topic}_{sequence}. Example: math_5u_chain_rule_001",
      "examples": ["math_5u_chain_rule_001", "phys_5u_newtons_second_law_003"]
    },

    "version": {
      "type": "integer",
      "minimum": 1,
      "description": "Item version. Incremented on every edit. Previous versions retained for audit."
    },

    "status": {
      "type": "string",
      "enum": ["draft", "review", "approved", "published", "retired", "corrected"],
      "description": "Content lifecycle status. Only 'published' items are served to students."
    },

    "item_type": {
      "type": "string",
      "enum": [
        "multiple_choice",
        "numeric",
        "expression",
        "true_false_justification",
        "ordering",
        "fill_blank",
        "diagram_labeling",
        "free_text"
      ],
      "description": "Question type. Determines evaluation pipeline routing and UI component. See docs/assessment-specification.md Section 1."
    },

    "is_diagnostic": {
      "type": "boolean",
      "default": false,
      "description": "True if this item is reserved for the onboarding diagnostic quiz (deterministic types only). Diagnostic items are never shown during regular learning sessions."
    },

    "stem": {
      "type": "object",
      "description": "The question text (natural language portion). Bilingual with math placeholders.",
      "properties": {
        "he": { "type": "string", "description": "Hebrew question text. May contain {math:key} placeholders." },
        "ar": { "type": "string", "description": "Arabic question text. May contain {math:key} placeholders." },
        "en": { "type": ["string", "null"], "description": "Optional English text for development/debugging." }
      },
      "required": ["he"],
      "examples": [
        {
          "he": "נתונה הפונקציה {math:func}. חשבו את {math:derivative_notation}.",
          "ar": "بالنظر إلى الدالة {math:func}، احسبوا {math:derivative_notation}."
        }
      ]
    },

    "math_expressions": {
      "type": "object",
      "description": "Shared mathematical expressions referenced by {math:key} placeholders in stem, options, hints, and explanations. Keys are arbitrary identifiers. Values are LaTeX strings.",
      "additionalProperties": { "type": "string" },
      "examples": [
        {
          "func": "$f(x) = \\sin(x^2)$",
          "derivative_notation": "$f'(x)$",
          "correct_answer": "$2x\\cos(x^2)$"
        }
      ]
    },

    "interaction": {
      "type": "object",
      "description": "Type-specific interaction data. Structure varies by item_type.",
      "oneOf": [
        { "$ref": "#/$defs/interaction_multiple_choice" },
        { "$ref": "#/$defs/interaction_numeric" },
        { "$ref": "#/$defs/interaction_expression" },
        { "$ref": "#/$defs/interaction_true_false_justification" },
        { "$ref": "#/$defs/interaction_ordering" },
        { "$ref": "#/$defs/interaction_fill_blank" },
        { "$ref": "#/$defs/interaction_diagram_labeling" },
        { "$ref": "#/$defs/interaction_free_text" }
      ]
    },

    "evaluation": {
      "type": "object",
      "description": "How this item is scored.",
      "properties": {
        "method": {
          "type": "string",
          "enum": ["deterministic", "cas", "llm_classification", "llm_freetext"],
          "description": "Evaluation pipeline. See docs/assessment-specification.md Section 3."
        },
        "partial_credit": {
          "type": "boolean",
          "description": "Whether partial credit is possible for this item."
        },
        "max_score": {
          "type": "number",
          "minimum": 0,
          "default": 1.0,
          "description": "Maximum score for this item. Usually 1.0 for single items, higher for multi-part."
        }
      },
      "required": ["method", "partial_credit"]
    },

    "hints": {
      "type": "array",
      "description": "Three-level hint escalation. Ordered: [L1_nudge, L2_scaffold, L3_near_answer].",
      "minItems": 0,
      "maxItems": 3,
      "items": {
        "type": "object",
        "properties": {
          "level": { "type": "integer", "enum": [1, 2, 3] },
          "text": {
            "type": "object",
            "properties": {
              "he": { "type": "string" },
              "ar": { "type": "string" }
            },
            "required": ["he"]
          },
          "xp_penalty_pct": {
            "type": "integer",
            "description": "XP penalty as percentage. L1=10, L2=30, L3=50."
          }
        },
        "required": ["level", "text", "xp_penalty_pct"]
      }
    },

    "worked_solution": {
      "type": "object",
      "description": "Full worked solution shown after L3 hint + incorrect, or on demand after completion.",
      "properties": {
        "he": { "type": "string", "description": "Hebrew step-by-step solution (Markdown + LaTeX)." },
        "ar": { "type": "string", "description": "Arabic step-by-step solution." }
      },
      "required": ["he"]
    },

    "taxonomy": {
      "type": "object",
      "description": "Educational classification metadata.",
      "properties": {
        "bloom_level": {
          "type": "string",
          "enum": ["remember", "understand", "apply", "analyze", "evaluate", "create"],
          "description": "Bloom's revised taxonomy level."
        },
        "subject": {
          "type": "string",
          "enum": ["mathematics", "physics", "chemistry", "biology", "computer_science"]
        },
        "depth_unit": {
          "type": "integer",
          "enum": [3, 4, 5],
          "description": "Israeli Bagrut depth level (3/4/5 units). 5 is the most advanced."
        },
        "topic_cluster": {
          "type": "string",
          "description": "Topic cluster ID matching Neo4j :TopicCluster node.",
          "examples": ["calculus", "trigonometry", "linear_algebra", "probability"]
        },
        "concept_ids": {
          "type": "array",
          "items": { "type": "string" },
          "minItems": 1,
          "description": "Concept node IDs this item assesses. Maps to [:ASSESSES] edges in Neo4j.",
          "examples": [["math_5u_derivatives_chain_rule"]]
        },
        "prerequisite_concept_ids": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Concepts the student should know before attempting this item. Derived from the concept graph but stored here for fast filtering."
        },
        "common_misconceptions": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Misconception IDs this item is designed to detect or address. Maps to Neo4j :Misconception nodes."
        }
      },
      "required": ["bloom_level", "subject", "depth_unit", "topic_cluster", "concept_ids"]
    },

    "difficulty": {
      "type": "object",
      "description": "Item difficulty calibration parameters.",
      "properties": {
        "initial_estimate": {
          "type": "number",
          "minimum": 0.0,
          "maximum": 1.0,
          "description": "Expert-estimated difficulty (0=trivial, 1=hardest). Set during content authoring."
        },
        "elo_rating": {
          "type": ["number", "null"],
          "description": "Elo-calibrated difficulty from student interaction data. Null until calibrated (requires 30+ attempts)."
        },
        "discrimination": {
          "type": ["number", "null"],
          "description": "2PL IRT discrimination parameter. Null until Phase 2 calibration."
        },
        "p_slip": {
          "type": "number",
          "minimum": 0.01,
          "maximum": 0.30,
          "default": 0.10,
          "description": "Probability of a knowledgeable student answering incorrectly (careless error)."
        },
        "p_guess": {
          "type": "number",
          "minimum": 0.01,
          "maximum": 0.50,
          "description": "Probability of an unknowing student answering correctly (lucky guess). Default depends on item_type."
        }
      },
      "required": ["initial_estimate", "p_slip", "p_guess"]
    },

    "provenance": {
      "type": "object",
      "description": "Content origin and audit trail.",
      "properties": {
        "generated_by": {
          "type": "string",
          "enum": ["kimi_k2.5", "claude_sonnet", "human_author", "hybrid"],
          "description": "Who/what created this item."
        },
        "generation_prompt_id": {
          "type": ["string", "null"],
          "description": "ID of the generation prompt template used (for reproducibility)."
        },
        "modeled_after": {
          "type": ["string", "null"],
          "description": "Source reference. For corpus-derived items: exam year and question number (e.g., 'bagrut_math_5u_2023_summer_q3b'). Null for fully original items."
        },
        "reviewed_by": {
          "type": ["string", "null"],
          "description": "Expert reviewer identifier."
        },
        "review_date": {
          "type": ["string", "null"],
          "format": "date"
        },
        "extraction_confidence": {
          "type": ["number", "null"],
          "minimum": 0.0,
          "maximum": 1.0,
          "description": "LLM confidence score from content extraction. Items below 0.8 are flagged for expert review."
        },
        "sympy_validated": {
          "type": "boolean",
          "default": false,
          "description": "Whether SymPy has verified the correct answer is mathematically valid (expression/numeric types only)."
        },
        "curriculum_version": {
          "type": "string",
          "description": "The curriculum graph version this item belongs to (e.g., 'math_5u_v1.2.0')."
        }
      },
      "required": ["generated_by", "curriculum_version"]
    },

    "created_at": { "type": "string", "format": "date-time" },
    "updated_at": { "type": "string", "format": "date-time" }
  },

  "required": [
    "item_id", "version", "status", "item_type", "stem",
    "interaction", "evaluation", "taxonomy", "difficulty",
    "provenance", "created_at", "updated_at"
  ],

  "$defs": {

    "locale_text": {
      "type": "object",
      "properties": {
        "he": { "type": "string" },
        "ar": { "type": "string" },
        "en": { "type": ["string", "null"] }
      },
      "required": ["he"]
    },

    "interaction_multiple_choice": {
      "type": "object",
      "properties": {
        "type": { "const": "multiple_choice" },
        "shuffle": { "type": "boolean", "default": true },
        "options": {
          "type": "array",
          "minItems": 4,
          "maxItems": 6,
          "items": {
            "type": "object",
            "properties": {
              "option_id": { "type": "string", "enum": ["A", "B", "C", "D", "E", "F"] },
              "text": { "$ref": "#/$defs/locale_text" },
              "is_correct": { "type": "boolean" },
              "distractor_rationale": {
                "type": ["string", "null"],
                "description": "Why a student might choose this wrong answer. Used for error classification."
              },
              "targeted_misconception": {
                "type": ["string", "null"],
                "description": "Misconception ID this distractor targets."
              }
            },
            "required": ["option_id", "text", "is_correct"]
          }
        }
      },
      "required": ["type", "options"]
    },

    "interaction_numeric": {
      "type": "object",
      "properties": {
        "type": { "const": "numeric" },
        "correct_value": { "type": "number" },
        "tolerance": {
          "type": "object",
          "properties": {
            "type": { "type": "string", "enum": ["absolute", "relative", "significant_figures"] },
            "value": { "type": "number" }
          },
          "required": ["type", "value"]
        },
        "unit_required": { "type": "boolean", "default": false },
        "accepted_units": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Accepted unit strings (e.g., ['m/s', 'm*s^-1'])."
        },
        "significant_figures": {
          "type": ["integer", "null"],
          "description": "Required number of significant figures, or null if not checked."
        }
      },
      "required": ["type", "correct_value", "tolerance"]
    },

    "interaction_expression": {
      "type": "object",
      "properties": {
        "type": { "const": "expression" },
        "correct_latex": {
          "type": "string",
          "description": "Reference answer in LaTeX for display."
        },
        "correct_sympy": {
          "type": "string",
          "description": "Reference answer as SymPy-parseable expression for CAS verification.",
          "examples": ["2*x*cos(x**2)", "3/(3*x+1)"]
        },
        "equivalence_type": {
          "type": "string",
          "enum": ["symbolic", "numeric_sampling", "structural"],
          "default": "symbolic",
          "description": "How to check equivalence. 'symbolic' = simplify(student - ref) == 0. 'numeric_sampling' = evaluate at random points. 'structural' = must match specific form."
        },
        "intermediate_steps": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "step_latex": { "type": "string" },
              "step_sympy": { "type": "string" },
              "partial_credit": { "type": "number", "minimum": 0, "maximum": 1 }
            }
          },
          "description": "Intermediate steps for partial credit. If student answer matches a step, they receive the step's partial credit."
        },
        "variables": {
          "type": "array",
          "items": { "type": "string" },
          "description": "Variable names used in the expression (for numeric sampling domain).",
          "examples": [["x"], ["x", "y"]]
        },
        "sampling_domain": {
          "type": "object",
          "description": "Domain for numeric sampling fallback.",
          "additionalProperties": {
            "type": "object",
            "properties": {
              "min": { "type": "number" },
              "max": { "type": "number" }
            }
          },
          "examples": [{ "x": { "min": -10, "max": 10 } }]
        }
      },
      "required": ["type", "correct_latex", "correct_sympy"]
    },

    "interaction_true_false_justification": {
      "type": "object",
      "properties": {
        "type": { "const": "true_false_justification" },
        "statement": { "$ref": "#/$defs/locale_text" },
        "correct_value": { "type": "boolean" },
        "justification_rubric": {
          "type": "object",
          "properties": {
            "keywords": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Keywords expected in a correct justification."
            },
            "criteria": { "$ref": "#/$defs/locale_text" },
            "model_justification": { "$ref": "#/$defs/locale_text" }
          },
          "required": ["keywords", "criteria"]
        },
        "scoring_weights": {
          "type": "object",
          "properties": {
            "binary_part": { "type": "number", "default": 0.4 },
            "justification_part": { "type": "number", "default": 0.6 }
          }
        }
      },
      "required": ["type", "statement", "correct_value", "justification_rubric"]
    },

    "interaction_ordering": {
      "type": "object",
      "properties": {
        "type": { "const": "ordering" },
        "items_to_order": {
          "type": "array",
          "minItems": 4,
          "maxItems": 8,
          "items": {
            "type": "object",
            "properties": {
              "item_id": { "type": "string" },
              "text": { "$ref": "#/$defs/locale_text" },
              "correct_position": { "type": "integer", "minimum": 1 }
            },
            "required": ["item_id", "text", "correct_position"]
          }
        },
        "scoring_method": {
          "type": "string",
          "enum": ["kendall_tau", "exact_match"],
          "default": "kendall_tau"
        }
      },
      "required": ["type", "items_to_order"]
    },

    "interaction_fill_blank": {
      "type": "object",
      "properties": {
        "type": { "const": "fill_blank" },
        "passage": { "$ref": "#/$defs/locale_text" },
        "blanks": {
          "type": "array",
          "minItems": 1,
          "maxItems": 5,
          "items": {
            "type": "object",
            "properties": {
              "blank_id": { "type": "string" },
              "canonical_answer": { "$ref": "#/$defs/locale_text" },
              "accepted_answers": {
                "type": "array",
                "items": { "type": "string" },
                "description": "All accepted answer strings (case-insensitive, whitespace-normalized). Includes synonyms in both languages."
              },
              "rejected_with_feedback": {
                "type": "object",
                "additionalProperties": { "$ref": "#/$defs/locale_text" },
                "description": "Map of common wrong answers to specific feedback."
              },
              "max_chars": { "type": "integer", "default": 50 }
            },
            "required": ["blank_id", "canonical_answer", "accepted_answers"]
          }
        }
      },
      "required": ["type", "passage", "blanks"]
    },

    "interaction_diagram_labeling": {
      "type": "object",
      "properties": {
        "type": { "const": "diagram_labeling" },
        "diagram_url": {
          "type": "string",
          "format": "uri",
          "description": "S3 URL to the SVG/image diagram (no embedded text)."
        },
        "regions": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "region_id": { "type": "string" },
              "polygon": {
                "type": "array",
                "items": {
                  "type": "array",
                  "items": { "type": "number" },
                  "minItems": 2,
                  "maxItems": 2
                },
                "description": "Polygon vertices as [x, y] coordinate pairs."
              },
              "correct_label": { "$ref": "#/$defs/locale_text" },
              "accepted_labels": {
                "type": "array",
                "items": { "type": "string" }
              }
            },
            "required": ["region_id", "polygon", "correct_label"]
          }
        },
        "label_bank": {
          "type": "array",
          "items": { "$ref": "#/$defs/locale_text" },
          "description": "Pool of labels shown to the student (includes correct + distractors)."
        }
      },
      "required": ["type", "diagram_url", "regions", "label_bank"]
    },

    "interaction_free_text": {
      "type": "object",
      "properties": {
        "type": { "const": "free_text" },
        "prompt": { "$ref": "#/$defs/locale_text" },
        "min_chars": { "type": "integer", "default": 200 },
        "max_chars": { "type": "integer", "default": 2000 },
        "rubric": {
          "type": "object",
          "properties": {
            "criteria": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "weight": { "type": "number", "minimum": 0, "maximum": 1 },
                  "description": { "$ref": "#/$defs/locale_text" }
                },
                "required": ["name", "weight", "description"]
              }
            },
            "model_answer": { "$ref": "#/$defs/locale_text" },
            "evaluator_model": {
              "type": "string",
              "enum": ["sonnet_4.6", "kimi_k2.5"],
              "default": "sonnet_4.6"
            }
          },
          "required": ["criteria", "model_answer"]
        }
      },
      "required": ["type", "prompt", "rubric"]
    }
  }
}
```

### 6.3 Concrete Example: A Bagrut Math Item

```json
{
  "item_id": "math_5u_chain_rule_001",
  "version": 1,
  "status": "published",
  "item_type": "expression",
  "is_diagnostic": false,

  "stem": {
    "he": "נתונה הפונקציה {math:func}.\n\nמצאו את {math:deriv_notation}.",
    "ar": "بالنظر إلى الدالة {math:func}.\n\nأوجدوا {math:deriv_notation}."
  },

  "math_expressions": {
    "func": "$f(x) = \\sin(x^2)$",
    "deriv_notation": "$f'(x)$",
    "correct_display": "$2x\\cos(x^2)$"
  },

  "interaction": {
    "type": "expression",
    "correct_latex": "2x\\cos(x^2)",
    "correct_sympy": "2*x*cos(x**2)",
    "equivalence_type": "symbolic",
    "intermediate_steps": [
      {
        "step_latex": "\\cos(x^2) \\cdot 2x",
        "step_sympy": "cos(x**2) * 2*x",
        "partial_credit": 1.0
      },
      {
        "step_latex": "\\cos(x^2)",
        "step_sympy": "cos(x**2)",
        "partial_credit": 0.25
      }
    ],
    "variables": ["x"],
    "sampling_domain": { "x": { "min": -5, "max": 5 } }
  },

  "evaluation": {
    "method": "cas",
    "partial_credit": true,
    "max_score": 1.0
  },

  "hints": [
    {
      "level": 1,
      "text": {
        "he": "איזה כלל גזירה מתאים כאן? חשבו על הרכבת פונקציות.",
        "ar": "ما هي قاعدة الاشتقاق المناسبة هنا؟ فكروا في تركيب الدوال."
      },
      "xp_penalty_pct": 10
    },
    {
      "level": 2,
      "text": {
        "he": "השתמשו בכלל השרשרת: {math:chain_rule}.\nכאן {math:outer} ו-{math:inner}.",
        "ar": "استخدموا قاعدة السلسلة: {math:chain_rule}.\nهنا {math:outer} و-{math:inner}."
      },
      "xp_penalty_pct": 30
    },
    {
      "level": 3,
      "text": {
        "he": "התשובה צריכה להיראות כמו {math:near_form}. בדקו את המקדם.",
        "ar": "يجب أن تبدو الإجابة مثل {math:near_form}. تحققوا من المعامل."
      },
      "xp_penalty_pct": 50
    }
  ],

  "worked_solution": {
    "he": "**פתרון:**\n\nנשתמש בכלל השרשרת: $(f(g(x)))' = f'(g(x)) \\cdot g'(x)$\n\n1. הפונקציה החיצונית: $f(u) = \\sin(u)$, לכן $f'(u) = \\cos(u)$\n2. הפונקציה הפנימית: $g(x) = x^2$, לכן $g'(x) = 2x$\n3. לפי כלל השרשרת: $f'(x) = \\cos(x^2) \\cdot 2x = 2x\\cos(x^2)$",
    "ar": "**الحل:**\n\nنستخدم قاعدة السلسلة: $(f(g(x)))' = f'(g(x)) \\cdot g'(x)$\n\n1. الدالة الخارجية: $f(u) = \\sin(u)$، إذن $f'(u) = \\cos(u)$\n2. الدالة الداخلية: $g(x) = x^2$، إذن $g'(x) = 2x$\n3. بقاعدة السلسلة: $f'(x) = \\cos(x^2) \\cdot 2x = 2x\\cos(x^2)$"
  },

  "taxonomy": {
    "bloom_level": "apply",
    "subject": "mathematics",
    "depth_unit": 5,
    "topic_cluster": "calculus",
    "concept_ids": ["math_5u_derivatives_chain_rule"],
    "prerequisite_concept_ids": [
      "math_5u_derivatives_basic",
      "math_5u_composite_functions",
      "math_5u_trig_derivatives"
    ],
    "common_misconceptions": ["confuse_chain_product_rule"]
  },

  "difficulty": {
    "initial_estimate": 0.55,
    "elo_rating": null,
    "discrimination": null,
    "p_slip": 0.10,
    "p_guess": 0.02
  },

  "provenance": {
    "generated_by": "kimi_k2.5",
    "generation_prompt_id": "math_expression_gen_v3",
    "modeled_after": "bagrut_math_5u_2022_summer_q2a",
    "reviewed_by": "expert_reviewer_01",
    "review_date": "2026-03-15",
    "extraction_confidence": 0.91,
    "sympy_validated": true,
    "curriculum_version": "math_5u_v1.0.0"
  },

  "created_at": "2026-03-10T14:30:00Z",
  "updated_at": "2026-03-15T09:45:00Z"
}
```

### 6.4 Multi-Part Question Representation

Bagrut exams frequently use multi-part questions (a, b, c) where later parts depend on earlier answers. These are represented as **linked item groups**, not single monolithic items:

```json
{
  "item_group_id": "math_5u_integral_area_group_001",
  "group_type": "multi_part_chain",
  "description": {
    "he": "בעיית חישוב שטח עם אינטגרלים",
    "ar": "مسألة حساب المساحة بالتكاملات"
  },
  "shared_stem": {
    "he": "נתונה הפונקציה {math:func} ונתון שהישר {math:line} חותך את הגרף בנקודות {math:points}.",
    "ar": "بالنظر إلى الدالة {math:func} والمستقيم {math:line} الذي يقطع الرسم البياني في النقاط {math:points}."
  },
  "shared_math_expressions": {
    "func": "$f(x) = x^2 - 4x + 3$",
    "line": "$y = x - 1$",
    "points": "$A(1, 0)$ ו- $B(4, 3)$"
  },
  "shared_diagram_url": "s3://cena-content/diagrams/math_5u_integral_area_001.svg",
  "parts": [
    {
      "part_label": "a",
      "item_id": "math_5u_integral_area_001a",
      "depends_on": [],
      "score_weight": 0.25,
      "sub_stem": {
        "he": "מצאו את נקודות החיתוך של {math:func} עם {math:line}.",
        "ar": "أوجدوا نقاط تقاطع {math:func} مع {math:line}."
      }
    },
    {
      "part_label": "b",
      "item_id": "math_5u_integral_area_001b",
      "depends_on": ["math_5u_integral_area_001a"],
      "score_weight": 0.50,
      "sub_stem": {
        "he": "חשבו את השטח הכלוא בין הגרף של {math:func} לישר {math:line}.",
        "ar": "احسبوا المساحة المحصورة بين الرسم البياني لـ {math:func} والمستقيم {math:line}."
      }
    },
    {
      "part_label": "c",
      "item_id": "math_5u_integral_area_001c",
      "depends_on": ["math_5u_integral_area_001b"],
      "score_weight": 0.25,
      "sub_stem": {
        "he": "הגדירו את הפונקציה {math:area_func} ומצאו את ערכה המקסימלי.",
        "ar": "عرّفوا الدالة {math:area_func} وأوجدوا قيمتها القصوى."
      }
    }
  ],
  "total_score": 1.0
}
```

Each `item_id` in the parts array references a full assessment item (as defined in Section 6.2). The `depends_on` array indicates which parts must be answered first. The `score_weight` allows the Bagrut-style point distribution (e.g., 25 points for part a, 50 for part b, 25 for part c).

### 6.5 Neo4j Graph Storage Model

```cypher
// Assessment item as a node (core fields as properties, interaction as JSON blob)
(:Item {
  item_id: "math_5u_chain_rule_001",
  version: 1,
  status: "published",
  item_type: "expression",
  is_diagnostic: false,
  bloom_level: "apply",
  depth_unit: 5,
  initial_difficulty: 0.55,
  elo_rating: null,
  p_slip: 0.10,
  p_guess: 0.02,
  evaluation_method: "cas",
  generated_by: "kimi_k2.5",
  sympy_validated: true,
  curriculum_version: "math_5u_v1.0.0",
  created_at: datetime("2026-03-10T14:30:00Z"),
  // Full JSON stored as a text property for complex nested data
  item_json: '{ ... full JSON blob ... }'
})

// Graph edges (first-class relationships)
(:Item {item_id: "math_5u_chain_rule_001"})
  -[:ASSESSES]->
(:Concept {id: "math_5u_derivatives_chain_rule"})

(:Item {item_id: "math_5u_chain_rule_001"})
  -[:TARGETS_MISCONCEPTION]->
(:Misconception {id: "confuse_chain_product_rule"})

(:Item {item_id: "math_5u_chain_rule_001"})
  -[:IN_CLUSTER]->
(:TopicCluster {id: "calculus"})

// Multi-part group relationships
(:ItemGroup {group_id: "math_5u_integral_area_group_001"})
  -[:CONTAINS {part_label: "a", position: 1, score_weight: 0.25}]->
(:Item {item_id: "math_5u_integral_area_001a"})

// Part dependency chain
(:Item {item_id: "math_5u_integral_area_001b"})
  -[:DEPENDS_ON]->
(:Item {item_id: "math_5u_integral_area_001a"})
```

**Storage strategy:** Scalar metadata fields that are used for filtering and graph traversal (bloom_level, difficulty, item_type, status, concept_ids) are stored as Neo4j node properties. Complex nested structures (interaction data, hints, localized text, provenance) are stored as a serialized JSON text property (`item_json`). This gives the best of both worlds: graph-native querying for item selection, and JSON flexibility for the full item representation.

**Querying pattern:** The item selection algorithm in the `LearningSessionActor` uses Cypher to find candidate items:

```cypher
MATCH (i:Item)-[:ASSESSES]->(c:Concept {id: $concept_id})
WHERE i.status = 'published'
  AND i.is_diagnostic = false
  AND i.bloom_level IN $target_bloom_levels
  AND i.initial_difficulty BETWEEN $min_diff AND $max_diff
  AND NOT i.item_id IN $already_seen_items
RETURN i.item_id, i.item_json
ORDER BY rand()
LIMIT 3
```

Then the full item JSON is deserialized for rendering.

---

## 7. Summary and Recommendations

### 7.1 Key Decisions

| Decision | Recommendation | Rationale |
|---|---|---|
| **Standards compliance** | Do not adopt QTI as internal format | Cena is vertically integrated; QTI's interoperability tax provides zero benefit. Build a QTI export adapter later if needed. |
| **Storage format** | Custom JSON schema (Section 6.2) | Covers all Cena requirements: bilingual, adaptive metadata, graph-native, LLM evaluation, Bagrut-specific fields. |
| **Math expressions** | LaTeX canonical, KaTeX rendering, SymPy verification | Aligns with LLM pipeline (LaTeX output), CAS evaluator (SymPy input), and rendering (KaTeX performance). Store both display_latex and sympy_expr for CAS items. |
| **Bilingual model** | Locale map (`{he, ar}`) + shared math placeholders | Math authored once, natural language per locale. Diagrams use text-free SVG with locale-aware label overlays. |
| **Multi-part questions** | Linked item groups with dependency chains | Each part is a standalone item (independently gradeable, reusable in adaptive selection) linked by an ItemGroup with dependency edges. |
| **Graph storage** | Scalar metadata as Neo4j properties; full JSON as text blob | Enables Cypher-based item selection filtering while preserving JSON flexibility for the full item representation. |
| **Difficulty calibration** | Expert estimate at authoring -> Elo calibration from data -> IRT discrimination at scale | Three-phase difficulty model matching the mastery engine phases (BKT -> MIRT). |

### 7.2 Migration Path

1. **Phase 0 (now):** Define the JSON schema. Validate with 10-20 hand-authored sample items spanning all 8 types.
2. **Phase 1 (content pipeline):** LLM generation pipeline outputs items in this schema. SymPy validation as a QA gate.
3. **Phase 2 (calibration):** After 1,000+ student interactions, populate `elo_rating` from interaction data. Adjust `p_slip`/`p_guess` via pyBKT parameter fitting.
4. **Phase 3 (export):** If school/LMS integration is needed, build a QTI 3.0 export adapter that projects from this schema to QTI XML + MathML.

### 7.3 What This Schema Does NOT Cover

- **Student response data** -- handled by the event store (`ConceptAttempted_V1` in `docs/event-schemas.md`)
- **Student mastery state** -- handled by the StudentActor aggregate (BKT/HLR in `docs/mastery-engine-architecture.md`)
- **Session management** -- handled by the LearningSessionActor (Pedagogy Context)
- **Content authoring workflow** -- handled by the Content Authoring Context (review queue, publication pipeline in `docs/content-authoring.md`)

This schema is purely about the **item definition** -- the static, published representation of a question that the adaptive engine selects and the delivery layer renders.

---

## Sources

- [QTI Specification Documents | 1EdTech](https://www.1edtech.org/standards/qti/index)
- [QTI 3.0 Overview](https://www.imsglobal.org/spec/qti/v3p0/oview)
- [QTI 3.0 Assessment, Section and Item Information Model](https://www.imsglobal.org/sites/default/files/spec/qti/v3/info/index.html)
- [QTI v3 Best Practices and Implementation Guide](https://www.imsglobal.org/spec/qti/v3p0/impl)
- [1EdTech QTI 3 Beginner's Guide](https://www.imsglobal.org/spec/qti/v3p0/guide)
- [QTI 3.0 Metadata Specification](https://www.imsglobal.org/sites/default/files/spec/qti/v3/md-bind/index.html)
- [Complete Guide to QTI](https://digitaliser.getmarked.ai/blog/complete-guide-to-qti/)
- [QTI - Wikipedia](https://en.wikipedia.org/wiki/QTI)
- [1EdTech QTI Examples (GitHub)](https://github.com/1EdTech/qti-examples)
- [Maths with QTI -- Item Body (Oxford)](https://learntech.medsci.ox.ac.uk/wordpress-blog/maths-with-qti-itembody/)
- [Khan Academy Perseus (GitHub)](https://github.com/Khan/perseus)
- [Learnosity QTI Converter (GitHub)](https://github.com/Learnosity/learnosity-qti)
- [Learnosity Question Types Reference](https://reference.learnosity.com/questions-api/questiontypes)
- [Learnosity RTL Configuration](https://help.learnosity.com/hc/en-us/articles/360002588377-Configuring-Items-API-to-Initialize-in-RTL-Right-to-Left-Mode-Arabic-and-Hebrew-Language-Support)
- [Moodle Question Database Structure](https://docs.moodle.org/dev/Question_database_structure)
- [Moodle Question Data Structures](https://docs.moodle.org/dev/Question_data_structures)
- [OpenMath and MathML](https://openmath.org/om-mml/)
- [MathML - Wikipedia](https://en.wikipedia.org/wiki/MathML)
- [Mathematical Markup Language - Wikipedia](https://en.wikipedia.org/wiki/Mathematical_markup_language)
- [SymPy Printing Documentation](https://docs.sympy.org/latest/tutorials/intro-tutorial/printing.html)
- [latex2sympy2 (PyPI)](https://pypi.org/project/latex2sympy2/)
- [py-asciimath (PyPI)](https://pypi.org/project/py-asciimath/)
- [AsciiMath](https://asciimath.org/)
- [Cross-Browser Math with MathML/LaTeX (Scott Hanselman)](https://www.hanselman.com/blog/exploring-crossbrowser-math-equations-using-mathml-or-latex-with-mathjax)
- [W3C Exercises and Activities Community Group - Existing Technologies](https://www.w3.org/community/exercises-and-activities/wiki/Existing_Technologies)
- [OATutor (GitHub)](https://github.com/CAHLR/OATutor)
- [text2qti -- Markdown to QTI (GitHub)](https://github.com/gpoore/text2qti)
