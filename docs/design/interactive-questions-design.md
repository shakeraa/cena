# Interactive vs Static Questions — Design Decision Document

## Context

Cena's question bank supports **static questions** — fixed stem, 4 options, single correct answer, binary scoring. For learning mode (as opposed to assessment mode), we want questions that adapt to the student's active methodology and learning state.

The question bank is event-sourced (`QuestionState` aggregate rebuilt via Marten `AggregateStreamAsync`), supports three creation paths (authored, ingested, AI-generated), and already has an 8-dimension quality gate. See `src/actors/Cena.Actors/Questions/QuestionState.cs` for the aggregate and `src/actors/Cena.Actors/Events/QuestionEvents.cs` for the domain events.

**Related documents:**

- `docs/discussion-student-ai-interaction.md` — Cache/RAG/strategy discussion for student-facing AI (Tier 1-3 explanation pipeline, hint progression, conversational tutoring). The LLM grading architecture in Decision 2 below aligns with that document's Tier 1 (adaptive explanations) and Tier 2 (hint progression) priorities.

## Decisions Required

1. **Should interactive behavior be a property of the question itself, or should the actor layer wrap any static question with interactive behavior based on the active methodology?** (Decision 1)
2. **Should questions be MCQ-only, or should the platform support open-ended writing responses alongside MCQ?** (Decision 2)

---

## Option A: Question-Level Interactivity

Each question is authored with explicit interactive content baked in.

### Data Model

```text
QuestionState {
  mode: "static" | "interactive"
  interactionSteps: InteractionStep[]     // ordered steps
  hintBank: HintEntry[]                   // hints tied to specific distractors
  scaffoldQuestions: SubQuestion[]         // breakdown sub-questions
  variationTemplate: string?              // parameterized stem for variants
  freeTextPrompt: string?                 // for Feynman explain-first mode
}

InteractionStep {
  trigger: "wrong_answer" | "first_attempt" | "timeout" | "low_confidence"
  action: "show_hint" | "show_worked_example" | "ask_sub_question" | "reveal_options" | "ask_explanation"
  content: string
  targetDistractor: string?               // which wrong answer triggers this
}
```

### Pros

- Rich, hand-crafted pedagogical content per question
- Content creators control the exact learning experience
- Quality gate can evaluate interactive content during creation
- Each question is self-contained — no runtime logic needed

### Cons

- Much higher authoring burden (5-10x more content per question)
- AI generation becomes significantly more complex
- Hard to scale — 100 questions x 9 methodologies = 900 interaction variants
- Methodology changes require re-authoring questions
- Question bank becomes tightly coupled to specific methodologies

### Best For

- High-stakes, carefully curated content (e.g., Bagrut flagship questions)
- Subjects where the pedagogical approach is inherently part of the question (e.g., worked examples in math)

---

## Option B: Actor-Wrapped Interactivity

Questions remain static. The **QuestionPoolActor** and **SessionActor** wrap any question with interactive behavior at serve-time based on the student's active methodology.

### Architecture

```text
Student requests question
  -> QuestionPoolActor selects static question (difficulty, concept match)
  -> SessionActor checks active methodology for this concept
  -> SessionActor wraps question with methodology-specific interaction protocol
  -> Student receives interactive session envelope
```

### Interaction Protocols by Methodology

| Methodology | Protocol | Actor Behavior |
| --- | --- | --- |
| **Socratic** | Wrong -> follow-up guiding question -> re-attempt | Actor generates hint question from distractor rationale |
| **Worked Example** | Wrong -> show similar solved problem -> re-ask | Actor pulls worked example from concept's example bank |
| **Feynman** | Free-text explain -> then show options -> score both | Actor prompts explanation, evaluates with LLM, then shows MCQ |
| **Scaffolded** | Break into sub-steps -> build to full question | Actor decomposes based on Bloom's level and prerequisite graph |
| **Retrieval Practice** | Show stem only (recall) -> then show options | Actor serves in 2 phases: open recall, then MCQ |
| **Spaced Repetition** | Same question with parameter variation | Actor uses variation template or synonym substitution |
| **Drill & Practice** | Rapid-fire, timed, minimal feedback | Actor sets timer, shows correct/incorrect immediately |
| **Analogy** | Wrong -> show analogous concept -> re-ask | Actor maps concept to analogy bank |
| **Bloom's Progression** | Start at Remember -> escalate to Apply/Analyze | Actor selects progressively harder questions on same concept |

### Data Model Addition (Minimal)

```text
QuestionState {
  // Existing fields unchanged
  distractorRationales: already exists     // used by Socratic protocol
  workedExampleRef: string?                // optional link to worked example
  variationParams: string[]?               // parameterizable parts of stem
}

// New: per-concept resource bank (not per-question)
ConceptResourceBank {
  conceptId: string
  workedExamples: WorkedExample[]
  analogies: Analogy[]
  scaffoldTemplates: ScaffoldTemplate[]
}
```

### Pros (Option B)

- Every existing question becomes interactive with zero re-authoring
- New methodologies can be added without touching questions
- The MCM graph drives methodology selection, actors drive interaction
- Scales naturally: N questions x M methodologies handled by M protocol actors
- A/B testing methodology effectiveness is trivial (change protocol, same questions)
- Aligns with Cena's actor-based architecture

### Cons (Option B)

- Runtime complexity in actors (but this is what actors are designed for)
- Generated interactions may be lower quality than hand-crafted
- Some methodologies need supplementary content (worked examples, analogies)
- Feynman mode requires LLM evaluation at serve-time

### Best For (Option B)

- Scale — hundreds of questions across all methodologies
- Data-driven methodology optimization (the MCM graph learns what works)
- Rapid iteration on pedagogy without content bottleneck

---

## Option C: Hybrid (ADOPTED)

Base questions are static. A subset of high-value questions get hand-crafted interactive content. The actor layer provides a baseline interactive experience for all questions, but defers to authored content when available.

### Priority Matrix

| Content Type | Authoring | Actor Wrapping |
| --- | --- | --- |
| Distractor rationales | Required (already exist) | Used by Socratic protocol |
| Hints | Optional (author can add) | Auto-generated from rationale if missing |
| Worked examples | Per-concept bank (shared) | Actor selects closest match |
| Scaffold sub-questions | Optional per question | Actor auto-decomposes if missing |
| Variation parameters | Optional per question | Actor uses synonym substitution fallback |
| Free-text prompts | Optional | Actor uses generic "Explain your reasoning" |

### Resolution Logic

```python
if question.hasAuthoredInteractiveContent(methodology):
    use authored content (highest quality)
elif conceptBank.hasResource(concept, methodology):
    use concept-level resource (shared, curated)
else:
    use actor-generated protocol (automated, good-enough)
```

---

## What Has Been Built (Implementation Status)

The static question layer is fully implemented. The interactive wrapping layer (methodology-specific protocols) is the next phase.

### Implemented: Static Question Foundation

**Event-sourced aggregate** — `QuestionState` (`src/actors/Cena.Actors/Questions/QuestionState.cs`)

Core state:

```csharp
QuestionOptionState(Label, Text, TextHtml, IsCorrect, DistractorRationale)
QuestionProvenanceState(SourceDocId, SourceUrl, SourceFilename, OriginalText, ...)
AiGenerationState(PromptText, ModelId, ModelTemperature, RawModelOutput, ...)
LanguageVersionState(Language, Stem, StemHtml, Options, TranslatedBy, AddedAt)
QualityEvaluationState(CompositeScore, 8 dimension scores, GateDecision, ...)
```

Lifecycle: `Draft -> InReview -> Approved -> Published -> Deprecated`

**Three creation paths** — each with its own domain event (`src/actors/Cena.Actors/Events/QuestionEvents.cs`):

| Path | Event | Key Fields |
| --- | --- | --- |
| Teacher-authored | `QuestionAuthored_V1` | AuthorId |
| OCR/document import | `QuestionIngested_V1` | SourceDocId, SourceUrl, OriginalText |
| LLM-generated | `QuestionAiGenerated_V1` | PromptText, ModelId, Temperature, RawModelOutput |

**8-dimension quality gate** — (`src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs`):

| Dimension | Weight | Stage |
| --- | --- | --- |
| Structural Validity | 20% | Automated (StructuralValidator) |
| Stem Clarity | 15% | Automated (StemClarityScorer) |
| Distractor Quality | 15% | Automated (DistractorQualityScorer) |
| Factual Accuracy | 15% | LLM (stub — defaults to 80) |
| Bloom Alignment | 10% | Automated (BloomAlignmentScorer) |
| Language Quality | 10% | LLM (stub — defaults to 80) |
| Pedagogical Quality | 10% | LLM (stub — defaults to 75) |
| Cultural Sensitivity | 5% | LLM (stub — defaults to 80) |

Gate decisions: `AutoApproved`, `NeedsReview`, `AutoRejected`

**Adaptive question serving** — (`src/actors/Cena.Actors/Serving/`):

- `QuestionPoolActor` — per-subject in-memory pool with NATS hot-reload on `cena.serve.item.published`. Index: `conceptId -> List<PublishedQuestion>` sorted by (bloomLevel, difficulty). Target: <10ms selection.
- `QuestionSelector` — multi-criteria adaptive algorithm:
  1. Concept selection (weighted by mastery, goal, spacing; 10% exploration rate)
  2. Bloom's range based on mastery phase (e.g., mastery <0.3 -> Remember/Understand levels 1-2)
  3. Difficulty in ZPD range centered on mastery, adjusted by focus state (`Strong`/`Stable`/`Declining`/`Degrading`/`Critical`)
  4. Item selection: prefer fresh (not seen in 7 days), score by quality + recency

Student context for selection:

```csharp
StudentContext(StudentId, PreferredLanguage, DepthUnit,
    ConceptMastery, LastPracticed, ItemsSeenThisSession,
    ItemsSeenLast7Days, CurrentFocus, Goal)

enum FocusState { Strong, Stable, Declining, Degrading, Critical }
enum SessionGoal { Practice, Review, Challenge, Diagnostic, ExamPrep }
```

**Student answering and grading** — `AttemptConcept` message processed by `StudentActor`:

- Bayesian Knowledge Tracing (BKT): `PriorMastery -> PosteriorMastery`
- Events: `ConceptAttempted_V1`, `ConceptMastered_V1` (threshold 0.85), `StagnationDetected_V1`, `MethodologySwitched_V1`
- Behavioral signals captured: `ResponseTimeMs`, `HintCountUsed`, `BackspaceCount`, `AnswerChangeCount`, `WasSkipped`, `WasOffline`

**8 question types** defined in contracts (`contracts/backend/actor-contracts.cs`):

1. MultipleChoice (4 options — primary for Bagrut)
2. Numeric (short answer)
3. Expression (mathematical)
4. TrueFalseJustification (with explanation)
5. Ordering (sequence/sort)
6. FillBlank (gap-fill)
7. DiagramLabeling (image interaction)
8. FreeText (extended response)

**Admin UI** — question list with 8+ filters, detail panel with quality gate breakdown, event history viewer, edit/deprecate/approve/publish flows (`src/admin/full-version/src/pages/apps/questions/`).

**12 REST endpoints** at `/api/admin/questions` (`src/api/Cena.Admin.Api/AdminApiEndpoints.cs` lines 450-558).

**100+ seeded Bagrut-aligned questions** across 6 subjects, 3 languages (`src/api/Cena.Admin.Api/QuestionBankSeedData.cs`).

### Not Yet Implemented

The following are needed to activate interactivity and mixed-format question serving:

| Component | Category | Status | Notes |
| --- | --- | --- | --- |
| `AnswerEvaluator` service | Open-ended | Not started | Route by `QuestionType`: deterministic (MCQ) vs LLM (FreeText/Expression) |
| Type-aware `QuestionSelector` | Open-ended | Not started | Select `QuestionType` based on mastery phase (see Decision 2) |
| Dual-signal BKT update path | Open-ended | Not started | Binary for `BktService`, continuous score for Bloom's progression |
| Language-aware rubric config | Open-ended | Not started | Per-subject `language_quality_weight` override for STEM grading |
| Open-ended seed questions | Open-ended | Not started | Bagrut-format Expression + FreeText items for Math, Physics |
| `ConceptResourceBank` aggregate | Interactive | Not started | Per-concept worked examples, analogies, scaffold templates |
| Methodology protocol actors | Interactive | Not started | One actor per methodology (Socratic, Feynman, etc.) |
| `InteractiveSessionEnvelope` | Interactive | Not started | Wraps static question with `ProtocolStep[]`, delivers via SignalR |
| LLM evaluation for Feynman mode | Interactive | Not started | Free-text explanation scoring (Claude Sonnet, rubric-based) |
| "Justify your answer" protocol | Interactive | Not started | MCQ + 1-2 sentence justification, routed to Kimi K2.5 |
| `workedExampleRef` on QuestionState | Interactive | Not started | Optional field for hand-crafted examples |
| `variationParams` on QuestionState | Interactive | Not started | Parameterizable stem parts for spaced repetition |

The existing `DistractorRationale` field on each option is already present and will power the Socratic protocol without changes. The `Answer` field on `AttemptConcept` is already a plain `string` — it can hold free-text responses without schema changes.

### End-to-End Question Flow (Current)

```text
CREATION (3 paths)
  Authored / Ingested (OCR) / AI-Generated
    -> Domain event appended to Marten event stream
    -> QuestionState aggregate rebuilt
    -> QuestionReadModel projection updated (inline)
         |
         v
QUALITY GATE (8-dimension)
  QualityGateService.Evaluate()
    -> QuestionQualityEvaluated_V1 event
    -> AutoApproved | NeedsReview | AutoRejected
         |
         v
APPROVAL & PUBLISHING
  Admin approves -> QuestionApproved_V1
  Admin publishes -> QuestionPublished_V1
    -> NATS "cena.serve.item.published" broadcast
         |
         v
DELIVERY (in-memory)
  QuestionPoolActor hot-reloads pool
  QuestionSelector picks next question:
    concept (mastery-weighted) -> Bloom's range -> ZPD difficulty -> freshness
         |
         v
ANSWERING
  AttemptConcept message -> StudentActor
    -> BKT mastery update (prior -> posterior)
    -> ConceptAttempted_V1 event
    -> Stagnation/methodology-switch detection
         |
         v
[FUTURE] INTERACTIVE WRAPPING
  SessionActor checks active methodology
    -> Wraps with protocol (Socratic hints, scaffolding, etc.)
    -> Falls back to actor-generated if no authored content
```

---

## Decision 1: Interactivity Model

**Option C (Hybrid)** with actor-wrapping as the default and authored content as progressive enhancement.

Rationale:

- Gets interactive learning live immediately (zero authoring needed)
- Quality improves over time as content creators add per-question/per-concept resources
- Aligns with the event-sourced architecture (interactions are events, enabling A/B analysis)
- The MCM graph already decides methodology — actors just execute the protocol
- Doesn't block on content creation bottleneck
- The existing `DistractorRationale` field already supports Socratic protocol without schema changes
- The `QuestionSelector` already adapts by mastery, Bloom's, difficulty, and focus — interactive wrapping is a natural extension

---

## Decision 2: Question Format — MCQ-Only vs Mixed with Open-Ended

### The Problem

The question bank is MCQ-dominated today. The contracts define 8 `QuestionType` values (`contracts/backend/actor-contracts.cs`) but only `MultipleChoice` has a working evaluation pipeline. The entire answering path — `AttemptConcept` -> `StudentActor` -> `BktService` — assumes binary `IsCorrect` outcomes.

Meanwhile:

- **The Bagrut exam is primarily open-ended.** Math 5-unit is 100% open (proofs, calculations, function investigation). Physics and Biology are 30-60% open. An MCQ-only platform cannot prepare students for the actual exam format.
- **MCQ caps at Bloom's level 3 (Apply).** The `QuestionSelector` already tracks Bloom's progression per concept, but levels 5-6 (Evaluate/Create) are unreachable without open-ended items.
- **Free recall produces deeper learning than recognition.** The generation effect (Slamecka & Graf, 1978) shows 10-20% retention advantage for free recall over MCQ at 1-week delay (Kang, McDermott, Roediger, 2007).
- **Writing-to-learn works in STEM.** Meta-analysis (Bangert-Drowns et al., 2004) shows ES=0.26 across subjects including math, strongest when combined with metacognitive reflection.
- **BKT gets a stronger signal.** MCQ has `PGuess=0.20` (4 options). Free-text has `PGuess~0.0`. A single correct open-ended answer is far more informative for mastery estimation.

### Decision

**Mixed format with mastery-gated progression.** Open-ended questions are unlocked as students demonstrate readiness, not imposed on beginners. This controls LLM grading cost (scales with advancement, not total student count) and avoids overwhelming novice learners.

### Mastery-to-Question-Type Progression

| Mastery Phase | Bloom's Range | Primary Question Types | Writing Component |
| --- | --- | --- | --- |
| Novice (< 0.30) | Remember, Understand (1-2) | MultipleChoice, FillBlank | None |
| Developing (0.30-0.60) | Understand, Apply (2-3) | MultipleChoice, Numeric, TrueFalseJustification | 1-2 sentence justification |
| Proficient (0.60-0.85) | Apply, Analyze (3-4) | MCQ, Expression, Ordering + short justification | "Justify your answer" after MCQ |
| Mastered (> 0.85) | Analyze, Evaluate, Create (4-6) | FreeText, Expression, Feynman protocol | Full explanation / proof |
| Exam Prep (goal=ExamPrep) | Full range, Bagrut-weighted | Open-ended matching actual Bagrut format | Bagrut-format responses |

The `QuestionSelector` (`src/actors/Cena.Actors/Serving/QuestionSelector.cs`) already selects Bloom's range based on mastery phase. Extending it to also select `QuestionType` is a natural addition — the `QuestionType` is already stored on `PublishedQuestion`.

### BKT with Partial Credit: Dual-Signal Approach

Open-ended questions produce continuous scores (0.0-1.0), but `BktService` (`src/actors/Cena.Actors/Services/BktService.cs`) takes `bool IsCorrect`. Three options:

| Approach | Description | Pros | Cons |
| --- | --- | --- | --- |
| A. Threshold binarization | `IsCorrect = (score >= 0.6)` | Zero BKT changes | Loses information (0.59 = 0.0) |
| B. Probabilistic (PC-BKT) | Score as observation probability | Mathematically elegant | Changes BKT equations |
| C. Dual-signal (ADOPTED) | Binary for BKT, continuous for Bloom's | No BKT changes, richer model | Two update paths |

**Adopted: Approach C (Dual-Signal).** Binarize the score for the existing `BktService.Update()` hot path. Store the continuous score separately to drive Bloom's level progression in `ConceptMasteryState`. This keeps the mastery model clean, the BKT path allocation-free, and adds richer signal without touching proven code.

For the Feynman protocol (explain-then-choose), the MCQ answer feeds BKT and the explanation score feeds Bloom's progression — two independent, well-defined signals from a single interaction.

### LLM Grading Architecture

| Evaluator | Question Types | Model | Target p50 | Target p99 | Cost/eval |
| --- | --- | --- | --- | --- | --- |
| Deterministic | MultipleChoice, FillBlank, Ordering | None | <5ms | <20ms | $0 |
| LLM classification | TrueFalseJustification, short justification | Kimi K2.5 | <500ms | <2s | ~$0.0002 |
| LLM rubric | FreeText, Expression (partial credit), Feynman explanation | Claude Sonnet | <1s | <3s | ~$0.004 |

**Consistency safeguards:**

- Temperature = 0 for all grading calls
- Structured JSON output schema enforcement
- Ensemble grading before `ConceptMastered_V1` emission: if the triggering answer was LLM-evaluated, run a second evaluation; if scores diverge > 0.2, flag for human review
- Store raw LLM evaluation as an event (event-sourced architecture makes this natural; enables re-evaluation if grading quality improves)

**Cost at scale** (1,000 students, 5 open-ended questions/day): ~5,000 evaluations/day = ~$20-25/day. Manageable because open-ended questions are mastery-gated (most students are in the MCQ-heavy novice/developing phases).

**Latency is acceptable.** Students spend 30-120s writing an answer. A 1-3s evaluation delay is proportionally negligible. UX: show "Analyzing your response..." animation during evaluation.

### Language-Aware Grading for Hebrew/Arabic Students

The LLM rubric must separate **content accuracy** from **linguistic fluency**. For Arabic-sector students especially, penalizing linguistic awkwardness when the mathematical reasoning is correct would be both pedagogically wrong and inequitable.

Implementation: per-subject rubric weight override:

- STEM subjects (Math, Physics, Chemistry, CS): `language_quality_weight: 0.0` — grade only on conceptual accuracy
- Language-heavy subjects (English, Biology explanations): `language_quality_weight: 0.3` — partial weight for expression

This maps to a field on the grading prompt, not a schema change on `QuestionState`.

### Data Model: Protocol-Embedded vs Standalone Open Questions

Open-ended responses serve two distinct purposes:

| Aspect | `QuestionType.FreeText` (standalone) | Protocol-embedded response (e.g., Feynman) |
| --- | --- | --- |
| Where it lives | `QuestionState` aggregate | `InteractiveSessionEnvelope` (not yet built) |
| Rubric | Authored per-question, stored in event stream | Generic per-methodology, or generated from metadata |
| BKT signal | The score IS the observation | MCQ answer is primary; explanation is supplementary |
| Quality gate | Evaluated during creation (8-dimension) | Not quality-gated (generated at runtime by actor) |
| Cost | Always requires LLM evaluation | Only when protocol activates it |
| Bloom's tag | Tagged on the question (typically 4-6) | Inherits from underlying question + protocol boost |

Both are needed. `QuestionType.FreeText` exists as a first-class question bank item for inherently open-ended questions ("Prove that...", "Explain the process of..."). Protocol-embedded responses are methodology behaviors that wrap ANY question with a writing component.

The `ProtocolStep` record (part of the future `InteractiveSessionEnvelope`):

```csharp
public record ProtocolStep(
    string StepType,          // "explain_first", "justify_after", "guided_question"
    string Prompt,            // text shown to student
    string? StudentResponse,  // their written response (hashed before persistence)
    double? EvaluationScore,  // LLM evaluation result (0-1)
    string? Feedback,         // feedback shown to student
    string EvaluationMethod); // "deterministic", "llm_classification", "llm_rubric"
```

This keeps the question bank clean (static, event-sourced, quality-gated) while allowing the actor layer to compose any question with any writing component.

---

## Research Questions

Before building the interactive layer, we need evidence on:

1. **Auto-generated vs hand-crafted hints**: Do students learn equally well from distractor-rationale-based hints vs pedagogist-written hints?

2. **Socratic questioning in digital environments**: What does the research say about automated Socratic dialogue effectiveness in math/science education?

3. **Worked examples effect**: How much of the "worked example effect" (Sweller) depends on the example being tailored to the specific question vs a related concept?

4. **Scaffolding granularity**: What's the optimal number of scaffold steps? Does auto-decomposition work as well as expert decomposition?

5. **Retrieval practice with delayed feedback**: Does the 2-phase (recall then MCQ) approach improve retention vs standard MCQ?

6. **Explain-first (Feynman) in MCQ context**: Does requiring explanation before answering improve learning outcomes in timed assessment contexts?

7. **Methodology switching overhead**: Is there a cognitive cost to changing interaction patterns between questions in the same session?

8. **Israeli Bagrut context**: Are there cultural or curricular factors that favor one approach for Hebrew/Arabic-speaking students?

9. **LLM grading reliability for Bagrut-level responses**: What inter-rater agreement (kappa) can LLMs achieve on Israeli math/science rubrics compared to human graders?

10. **Optimal mastery threshold for open-ended unlock**: Is 0.30 the right threshold for introducing justification prompts, or should it be calibrated per subject?

---

## Next Steps

### Phase 1: Extend Static Foundation for Mixed Format

1. **Type-aware `QuestionSelector`**: Extend selection algorithm to consider `QuestionType` based on mastery phase (see progression table above). File: `src/actors/Cena.Actors/Serving/QuestionSelector.cs`
2. **Seed open-ended questions**: Audit the 100+ seeded questions for type distribution. Add Bagrut-format Expression and FreeText items for Math and Physics.
3. **Implement `AnswerEvaluator` service**: Route answers by `QuestionType` — deterministic for MCQ/FillBlank/Ordering, LLM for TrueFalseJustification/FreeText/Expression.

### Phase 2: Interactive Protocols

1. **Prototype Socratic protocol**: Build the first methodology actor using existing `DistractorRationale` data — no new content needed
2. **Prototype Feynman protocol**: Explain-then-choose with dual-signal scoring (explanation -> Bloom's, MCQ -> BKT)
3. **"Justify your answer" lightweight protocol**: MCQ + 1-2 sentence justification routed to Kimi K2.5. Trigger for mastery 0.30-0.60.
4. **Design `InteractiveSessionEnvelope`**: Message format with `ProtocolStep[]` that wraps a static question and delivers via SignalR

### Phase 3: Infrastructure

1. **Define `ConceptResourceBank` aggregate**: Event-sourced per-concept resources (worked examples, analogies, scaffold templates)
2. **Implement dual-signal BKT update path**: Binary for `BktService`, continuous for Bloom's progression in `ConceptMasteryState`
3. **Language-aware rubric configuration**: Per-subject weight overrides for content vs linguistic quality

### Phase 4: Validate

1. **Measure**: A/B test actor-wrapped Socratic vs static delivery on a subset of students
2. **Measure**: Compare learning outcomes for MCQ-only vs mixed-format sessions at equivalent mastery levels
3. **Research**: Validate LLM grading kappa against human graders on actual Bagrut rubrics
