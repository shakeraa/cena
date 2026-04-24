# Content Authoring Context — Bounded Context Specification

> **Status:** Specification
> **Last updated:** 2026-03-26
> **Classification:** 9th bounded context (added after architecture audit)

---

## 1. The Problem

The architecture extensively describes how content is served and adapted but was silent on how it's created. An investor will ask: "You need 2,000 concept nodes per subject, each with questions, explanations, diagrams, and difficulty ratings — who makes all of that? How do errors get fixed after students interact with affected nodes?"

---

## 2. Content Authoring as a Bounded Context

### Context Definition

| Attribute | Value |
|-----------|-------|
| **Type** | Supporting context |
| **Upstream dependency** | None (this produces the Curriculum context's artifacts) |
| **Downstream consumers** | Curriculum Context (consumes published graphs), Delivery Context (consumes questions/explanations) |
| **Aggregate root** | `ContentGraph` (the in-progress, unpublished version of a subject's knowledge graph) |
| **Key domain concepts** | ConceptDraft, QuestionBank, ExplanationTemplate, DiagramSpec, ReviewCycle, PublicationPipeline |

### Relationship to Curriculum Context
- The **Curriculum Context** serves the published, immutable, production knowledge graph
- The **Content Authoring Context** manages the mutable, in-progress draft graph that goes through review and publication
- On publication: a snapshot of the authored graph becomes the new version of the Curriculum Context's domain graph
- Students never see draft content — only published graphs loaded into actor memory

---

## 3. Content Pipeline: Corpus Learning → Generation → Expert Review → QA → Publication

### Core Principle: Learn First, Generate Second

The LLM does NOT generate content from general knowledge. It first **learns the existing corpus** — real Bagrut exams, approved textbooks, teacher materials, Ministry of Education guidelines — and then generates questions and explanations that match the **exact style, difficulty, and terminology** of the Bagrut exam ecosystem.

This produces questions that feel like real Bagrut prep, not generic AI-generated exercises.

### 3.0 Stage 0: Corpus Ingestion (Foundation — runs once per subject)

**Input corpus (per subject):**
- Official Bagrut exam papers (last 10–15 years, publicly available from Ministry of Education)
- Approved textbook chapters (1–2 primary textbooks per subject, e.g., Geva for Math, Halliday for Physics)
- Ministry of Education syllabus documents and teaching guides
- Teacher-created materials and worksheets (contributed by education advisor)
- Common student errors documented by experienced Bagrut teachers

**Process:**
1. Feed the entire corpus into Kimi K2.5 (256K context, batch mode) in structured chunks
2. For each corpus source, the LLM produces a **corpus analysis document**:
   - Terminology index: exact Hebrew terms used for each concept, with English equivalents
   - Question style patterns: how Bagrut exams phrase questions (e.g., "הוכח כי..." / "חשב את..." / "מצא את...")
   - Difficulty distribution: how many questions at each Bloom's level per exam
   - Common distractor patterns: what wrong answers appear in real MCQs and why they're wrong
   - Scoring rubrics: how partial credit is awarded in real Bagrut grading
   - Cross-concept connections: which concepts appear together in multi-part questions
3. The corpus analysis documents become the **style guide** for all subsequent content generation
4. Stored as structured JSON artifacts in S3, loaded by the generation pipeline

**Why this matters:** A student preparing for Bagrut expects questions that look and feel like real Bagrut questions. If Cena generates generic math questions, it fails the credibility test. By learning the corpus first, every generated question inherits the exact phrasing patterns, difficulty calibration, and pedagogical style of real Bagrut materials.

### 3.1 Stage 1: Knowledge Graph Extraction (from corpus)

**Input:** Corpus analysis documents + official syllabus PDFs

**Process:**
1. Feed corpus analysis + syllabus to Kimi K2.5 (long context, structured extraction)
2. Prompt instructs: extract concept nodes, prerequisite edges, difficulty estimates, key formulas, common misconceptions — **using the exact terminology from the corpus**
3. Cross-reference extracted graph against real Bagrut exam coverage: every concept that has appeared in an exam in the last 10 years must be represented
4. Output: structured JSON graph per syllabus unit, with corpus provenance (which textbook/exam each concept was derived from)

**Output per concept node:**
```json
{
  "concept_id": "math_5u_derivatives_chain_rule",
  "display_name": { "he": "כלל השרשרת", "en": "Chain Rule" },
  "subject": "mathematics",
  "depth_unit": 5,
  "prerequisites": ["math_5u_derivatives_basic", "math_5u_composite_functions"],
  "builds_on": ["math_5u_derivatives_product_rule"],
  "bloom_level": "application",
  "estimated_mastery_time_minutes": 45,
  "common_misconceptions": [
    "Confusing chain rule with product rule when functions are nested vs multiplied"
  ],
  "key_formulas": ["(f(g(x)))' = f'(g(x)) · g'(x)"],
  "extraction_confidence": 0.87,
  "needs_expert_review": true
}
```

**Estimated throughput:** 200–400 concept nodes per day using Kimi batch processing. One subject (2,000 nodes) takes 5–10 working days of extraction.

### 3.2 Stage 2: Question Generation (Corpus-Informed)

For each concept node, generate a question bank **in the style of real Bagrut exams**.

**Generation process:**
1. **Input to LLM:** The concept definition + the corpus style guide (terminology, question phrasing patterns, distractor patterns, scoring rubrics from Stage 0) + 3–5 real Bagrut questions on this concept as examples
2. **Kimi K2.5** generates initial question bank (batch, overnight). Prompt: "Generate questions in the exact style of the provided Bagrut examples. Use the same Hebrew terminology. Match the difficulty distribution. Create distractors that reflect real student misconceptions documented in the corpus."
3. **Claude Sonnet** reviews each question for: quality, ambiguity, Bloom's level alignment, and faithfulness to corpus style. Flags any question that "doesn't feel like a real Bagrut question."
4. **SymPy validation** for numeric/expression questions: automatically verify that the correct answer is mathematically valid
5. Expert reviews flagged items (low confidence, complex wording, style mismatch)

**Question types supported at MVP:**
| Type | Evaluation Method | Example (Bagrut style) |
|------|------------------|------------------------|
| **Multiple choice** (4 options) | Deterministic | "נתונה הפונקציה f(x)=sin(x²). מהי f'(x)?" with Bagrut-style distractors |
| **True/False** with justification | LLM evaluation | "הנגזרת של e^(2x) היא e^(2x). נכון או לא? נמק." |
| **Numeric answer** | Exact match ± tolerance | "חשב f'(3) עבור f(x)=x³+2x" → 29 |
| **Expression input** | CAS (SymPy) equivalence | "גזור את f(x)=ln(3x+1)" → 3/(3x+1) |
| **Ordering/Sequencing** | Deterministic | "סדר את השלבים בפתרון בעזרת אינטגרציה בחלקים" |
| **Multi-part problem** | Staged evaluation | Bagrut-style "a), b), c)" problems where each part builds on the previous |
| **Proof/Derivation** | LLM evaluation (Sonnet) | "הוכח כי..." — evaluated for logical flow and completeness |
| **Diagram interpretation** | Deterministic + LLM | Given a graph/circuit/diagram, answer questions about it |

**Target:** 8–15 questions per concept (mix of types and Bloom's levels, matching the distribution found in real Bagrut exams from corpus analysis)

**Corpus provenance tracking:** Each generated question records which real Bagrut questions it was modeled after, enabling traceability and quality auditing

### 3.3 Stage 3: Explanation & Diagram Generation (Corpus-Informed)

For each concept, explanations are generated **using the language and pedagogical patterns from the learned corpus** — matching how approved textbooks explain concepts, not generic LLM explanations.

- **Explanation templates** (3 per concept, each methodology family):
  - Socratic prompt sequence: modeled after the questioning patterns used in the corpus textbooks' "think about it" sections
  - Direct explanation: mirrors the textbook's explanatory style, with the same notation conventions and Hebrew terminology
  - Worked example: follows the step-by-step format from real Bagrut exam solutions (published by Ministry of Education)
  - Generated by Sonnet with corpus style guide as context. Reviewed by expert.
- **Diagram specifications** (1–2 per concept): JSON description for the Remotion/SVG engine. Generated by Kimi using visual descriptions from the textbook corpus (e.g., "draw the graph as shown in Geva Ch.5 Fig.3 style"). Rendered and visually reviewed.
- **Video script** (1 per concept, optional): Sonnet generates script for Remotion rendering, using the explanation template as the narration base. Batch process.
- **Common error explanations** (1–3 per concept): Generated from the corpus's documented common misconceptions. "Students often think X because Y — here's why that's wrong." These are served when the stagnation detector identifies a recurring error pattern.

### 3.4 Stage 4: Expert Review (Domain Expert, part-time)

The education domain expert (licensed Bagrut teacher) reviews:

**Review queue priorities:**
1. Prerequisite edges — wrong prerequisites cause cascading learning failures
2. Questions with low extraction confidence (<0.8)
3. Common misconception accuracy
4. Bloom's level classification
5. Hebrew language quality and terminology

**Review tool:** Internal admin UI (minimal, built with AI agents in ~1 week):
- List of concepts pending review, sorted by priority
- Side-by-side: LLM extraction vs. source material
- Accept / Edit / Reject per field
- Bulk approve for high-confidence items (>0.95)

**Throughput estimate:** 1 expert, part-time (20 hrs/week), reviews ~100–150 concepts/week. One subject (2,000 nodes) takes ~15 weeks of expert review. With 2 experts: ~8 weeks.

**Expert rejection handling:**
- **Rejection rate tracking**: Dashboard tracks per-stage rejection rates (prerequisite edges, questions, explanations, diagrams). Target: <30% expert rejection rate for LLM-generated content.
- **Escalation threshold**: If rejection rate exceeds 40% for a concept cluster (e.g., "Trigonometric Identities"), the cluster is flagged for prompt engineering review — the extraction/generation prompts for that subject area are not performing well enough.
- **Retraining trigger**: If rejection rate exceeds 50% across any full subject for 2 consecutive review batches, the pipeline pauses for that subject. Root cause analysis: was the corpus insufficient? Is the extraction prompt misaligned with this subject's structure? Fix prompts before resuming generation.
- **Rejected content lifecycle**: Rejected items are tagged with rejection reason (factual error, wrong difficulty, poor Hebrew, wrong prerequisite, wrong Bloom's level). Rejection reasons are aggregated weekly to identify systematic LLM weaknesses — e.g., if 60% of rejections are "wrong Bloom's level," the Bloom's classification prompt needs refinement.
- **Expert feedback loop**: Accepted-with-edits items are especially valuable — the expert's edits become few-shot examples for improving the generation prompts for similar concepts.

### 3.5 Stage 5: QA Pass

Automated checks before publication:
1. **Graph connectivity:** Every concept has at least one path from a foundational node (no orphan concepts)
2. **Prerequisite acyclicity:** No circular prerequisite chains
3. **Question coverage:** Every concept has ≥5 questions spanning ≥2 Bloom's levels
4. **Explanation coverage:** Every concept has ≥2 explanation templates
5. **SymPy validation:** Expression-type questions are verified to have correct answers
6. **Language check:** All Hebrew text passes spell-check and terminology validation
7. **Difficulty distribution:** Questions per concept span at least 3 difficulty levels

### 3.6 Stage 6: Publication

1. QA-approved graph is versioned (semantic versioning: `math_5u_v1.2.0`)
2. Published to S3 as a serialized Protobuf artifact
3. Neo4j AuraDB updated (source of truth for admin/analytics)
4. NATS event emitted: `CurriculumPublished_V1` with graph version and diff from previous
5. Proto.Actor silos detect the event and hot-reload the in-memory domain graph (no restart required)
6. Student actors whose affected concepts are in active sessions receive a `ContentUpdated` notification — current session continues with old content; next session uses new content

---

## 4. Content Correction After Student Interaction

### The Problem
A concept node has an error (wrong prerequisite, incorrect question, misleading explanation). 500 students have already interacted with it.

### Correction Protocol

1. **Identify the error** — via expert review, student feedback, or automated anomaly detection (unusual mastery patterns on a concept suggest content issues)

2. **Draft the correction** in the Content Authoring tool. Mark affected content items as `corrected`

3. **Impact analysis** — automated query:
   - How many students have mastered this concept?
   - How many have active stagnation on this concept?
   - Are any students mid-session on this concept right now?

4. **Publish correction** as a new graph version with a `ContentCorrected` event:
   ```protobuf
   message ContentCorrected_V1 {
     string concept_id = 1;
     string correction_type = 2;    // "prerequisite_fixed" | "question_fixed" | "explanation_fixed" | "difficulty_adjusted"
     string previous_version = 3;
     string new_version = 4;
     string correction_description = 5;
     repeated string affected_student_ids = 6; // students who interacted with the error
   }
   ```

5. **Student impact handling:**
   - **Wrong prerequisite removed:** Students who mastered the concept keep their mastery. No retroactive change — the concept was learned regardless of the wrong prerequisite edge.
   - **Wrong question fixed:** Students who got the wrong question wrong are NOT penalized (their `ConceptAttempted` events remain, but the question is retired from the bank). Students who got the wrong question right keep the credit.
   - **Difficulty adjusted:** BKT parameters recalculated for affected students on next interaction. No retroactive change.
   - **Explanation corrected:** No student impact — the old explanation is simply replaced.

6. **Principle:** Never retroactively invalidate a student's mastery or XP. Corrections affect future interactions only. The student should never feel punished for a content error.

---

## 5. Content Volume Estimates

| Subject | Est. Concept Nodes | Questions (8-15/concept) | Explanations (3/concept) | Diagrams (1-2/concept) |
|---------|-------------------|--------------------------|--------------------------|------------------------|
| Mathematics (5-unit) | 1,800–2,200 | 14,400–33,000 | 5,400–6,600 | 1,800–4,400 |
| Physics (5-unit) | 1,200–1,500 | 9,600–22,500 | 3,600–4,500 | 1,200–3,000 |
| Chemistry | 1,000–1,300 | 8,000–19,500 | 3,000–3,900 | 1,000–2,600 |
| Biology | 1,200–1,500 | 9,600–22,500 | 3,600–4,500 | 1,200–3,000 |
| Computer Science | 800–1,000 | 6,400–15,000 | 2,400–3,000 | 800–2,000 |
| **Total (5 subjects)** | **6,000–7,500** | **48,000–112,500** | **18,000–22,500** | **6,000–15,000** |

### MVP (Math + Physics only)
- 3,000–3,700 concept nodes
- 24,000–55,500 questions
- 9,000–11,100 explanations
- 3,000–7,400 diagrams

### Estimated Timeline (with AI agents)
| Phase | Duration | Output |
|-------|----------|--------|
| Math extraction | 5–10 days | 2,000 draft nodes |
| Math question gen | 3–5 days | 16,000–30,000 draft questions |
| Math expert review | 8–15 weeks | Validated graph |
| Physics extraction | 5–8 days | 1,500 draft nodes |
| Physics question gen | 2–4 days | 12,000–22,500 draft questions |
| Physics expert review | 6–12 weeks | Validated graph |

**Critical path:** Expert review. LLM extraction and question generation are fast (days). Expert review is the bottleneck (weeks). Mitigation: start with a subset (50% of Math) for beta launch, expand while live.

---

## 6. Authoring Tool (Internal Admin UI)

### MVP Feature Set
Built with React + simple REST API, takes ~1 week with AI coding agents:

1. **Graph visualization editor** — display concept nodes and edges; drag to rearrange; click to edit
2. **Concept detail panel** — edit fields, view extraction confidence, mark as reviewed
3. **Question bank manager** — list/edit/approve/reject questions per concept
4. **Review queue** — sorted by priority, filterable by subject/status/confidence
5. **Publication dashboard** — run QA checks, view diff vs. current production, publish
6. **Correction workflow** — mark content as corrected, run impact analysis, publish fix

### Authentication
Admin-only access. No student or parent access. Separate from the student-facing API.

---

## 7. MCM Graph Authoring

The Mode × Capability × Methodology graph is authored separately from the knowledge graph:

- **Initial version:** Manually crafted by architect + education advisor based on pedagogical research
- **Structure:** Maps `(error_type, concept_category, student_profile_cluster)` → `recommended_methodology` with a confidence score
- **Updates:** After sufficient student interaction data (target: 1,000+ methodology switches), retrain MCM mappings using aggregate anonymized data. This is the **data flywheel** — the system gets smarter at picking the right methodology as more students use it.
- **Stored as:** JSON artifact in S3, loaded into Pedagogy context at startup alongside the domain graph. Not in Neo4j (too simple/small to need a graph DB).
