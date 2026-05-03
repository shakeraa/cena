# Interactive & Open-Ended Questions — Implementation Tasks

**Source**: `docs/design/interactive-questions-design.md` (Decisions 1 & 2)
**Date**: 2026-03-28
**Status**: Ready for implementation

---

## Overview

Two adopted decisions drive this work:

1. **Decision 1 (Hybrid Interactivity)**: Actor-wrapped interactive protocols with authored content as progressive enhancement
2. **Decision 2 (Mixed Format)**: Mastery-gated open-ended questions alongside MCQ — not MCQ-only

The static question foundation is complete (event-sourced aggregate, 3 creation paths, 8-dimension quality gate, adaptive serving, BKT grading). These tasks build the interactive and open-ended layers on top.

---

## Phase 1: Extend Static Foundation for Mixed Format

### TASK-IQ-01: Type-Aware Question Selection

**Goal**: Extend `QuestionSelector` to consider `QuestionType` when selecting questions based on student mastery phase.

**Files to modify**:
- `src/actors/Cena.Actors/Serving/QuestionSelector.cs`
- `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` (index may need type dimension)

**Requirements**:
- Novice (mastery < 0.30): prefer MultipleChoice, FillBlank
- Developing (0.30-0.60): add Numeric, TrueFalseJustification
- Proficient (0.60-0.85): add Expression, Ordering
- Mastered (> 0.85): prefer FreeText, Expression, Feynman protocol
- ExamPrep goal: override to match Bagrut format distribution per subject (Math: 100% open, Physics: 30-40% open, Biology: 40-60% open)
- Fallback: if no questions of the preferred type exist for a concept, fall back to any available type

**Depends on**: Nothing (existing code)
**Effort**: 1-2 days

---

### TASK-IQ-02: Seed Open-Ended Questions

**Goal**: Audit existing 100+ seeded questions for type distribution. Add Bagrut-format Expression and FreeText items.

**Files to modify**:
- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs`

**Requirements**:
- Add at minimum 10 Expression questions (Math, Physics, Chemistry) with correct answers and partial-credit rubric metadata
- Add at minimum 10 FreeText questions (Math proofs, Biology explanations, Physics derivations) with rubric criteria
- Add at minimum 5 TrueFalseJustification questions
- All seeded in Hebrew primary, with Arabic translations where possible
- Each must have Bloom's level 4+ tags (Analyze, Evaluate, Create)
- Store rubric criteria in a new `RubricCriteria` field on the question (or as metadata — decide in implementation)

**Depends on**: Nothing
**Effort**: 2-3 days

---

### TASK-IQ-03: Implement AnswerEvaluator Service

**Goal**: Create a service that routes answer evaluation by `QuestionType` — deterministic for MCQ/FillBlank/Ordering, LLM for FreeText/Expression/TrueFalseJustification.

**Files to create**:
- `src/actors/Cena.Actors/Evaluation/IAnswerEvaluator.cs` (interface)
- `src/actors/Cena.Actors/Evaluation/AnswerEvaluatorService.cs` (router)
- `src/actors/Cena.Actors/Evaluation/DeterministicEvaluator.cs` (MCQ, FillBlank, Ordering, Numeric)
- `src/actors/Cena.Actors/Evaluation/LlmRubricEvaluator.cs` (FreeText, Expression)
- `src/actors/Cena.Actors/Evaluation/LlmClassificationEvaluator.cs` (TrueFalseJustification)

**Files to modify**:
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` (wire up evaluator instead of inline `p.Answer == "correct"`)
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` (replace binary `CorrectAttempts` logic)

**Requirements**:
- Deterministic evaluator: exact match for MCQ (option label), numeric tolerance for Numeric, expression equivalence for Expression (basic)
- LLM classification evaluator: route to Kimi K2.5, structured JSON output, temperature=0
- LLM rubric evaluator: route to Claude Sonnet, 4-dimension rubric (content accuracy, reasoning depth, completeness, mathematical correctness), temperature=0, structured JSON
- Return `EvaluationResult(bool IsCorrect, double Score, string Feedback, string EvaluationMethod)`
- `IsCorrect` for LLM-evaluated: threshold at score >= 0.6
- Circuit breaker: if LLM fails, fall back to "pending human review" state (don't guess)

**Depends on**: TASK-IQ-02 (needs non-MCQ questions to test against)
**Effort**: 3-5 days

---

### TASK-IQ-04: Dual-Signal BKT Update Path

**Goal**: Keep binary `BktService.Update()` unchanged. Add a parallel continuous-score path for Bloom's level progression.

**Files to modify**:
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` (after BKT update, also update Bloom's)
- `src/actors/Cena.Actors/Events/LearnerEvents.cs` (add `BloomProgressionUpdated_V1` event if needed)

**Files to create**:
- `src/actors/Cena.Actors/Services/BloomProgressionService.cs`

**Requirements**:
- Input: `(conceptId, currentBloomLevel, evaluationScore, questionBloomLevel)`
- Logic: if `evaluationScore >= 0.7` and `questionBloomLevel > currentBloomLevel`, advance Bloom's level for that concept
- Emit event: `BloomProgressionUpdated_V1(StudentId, ConceptId, PreviousBloom, NewBloom, TriggeringQuestionId, EvaluationScore)`
- No change to `BktService` — it continues to take `bool IsCorrect`

**Depends on**: TASK-IQ-03 (needs continuous scores)
**Effort**: 1-2 days

---

### TASK-IQ-05: Language-Aware Rubric Configuration

**Goal**: Configure LLM grading rubrics to separate content accuracy from linguistic quality, per subject.

**Files to create**:
- `src/actors/Cena.Actors/Evaluation/RubricConfiguration.cs`

**Requirements**:
- Per-subject weight map: `Dictionary<string, SubjectRubricConfig>`
- STEM subjects (Math, Physics, Chemistry, CS): `LanguageQualityWeight = 0.0` — grade only conceptual accuracy
- Language-heavy subjects (English, Biology explanations): `LanguageQualityWeight = 0.3`
- Inject into `LlmRubricEvaluator` prompt construction
- Configurable via admin settings (future: expose in Admin UI)

**Depends on**: TASK-IQ-03
**Effort**: 0.5-1 day

---

## Phase 2: Interactive Protocols

### TASK-IQ-06: Design InteractiveSessionEnvelope

**Goal**: Define the message format that wraps a static question with interactive protocol steps, delivered via SignalR.

**Files to create**:
- `src/actors/Cena.Actors/Sessions/InteractiveSessionEnvelope.cs`
- `contracts/backend/interactive-contracts.cs` (or extend `actor-contracts.cs`)

**Requirements**:
- `InteractiveSessionEnvelope` record containing:
  - `QuestionId`, `QuestionType`, base question data
  - `ProtocolType` (Socratic, Feynman, Scaffolded, etc.)
  - `Steps: ProtocolStep[]` — ordered list of interaction steps
  - `CurrentStepIndex` — which step the student is on
- `ProtocolStep` record:
  - `StepType` ("explain_first", "justify_after", "guided_question", "show_hint", "present_mcq")
  - `Prompt` (text shown to student)
  - `StudentResponse?` (their written response, hashed before persistence)
  - `EvaluationScore?` (0-1 from LLM)
  - `Feedback?` (shown to student after evaluation)
  - `EvaluationMethod` ("deterministic", "llm_classification", "llm_rubric", "skipped")
- Event: `InteractiveStepCompleted_V1` for audit trail

**Depends on**: Nothing (design task)
**Effort**: 1-2 days

---

### TASK-IQ-07: Prototype Socratic Protocol Actor

**Goal**: Build the first methodology protocol actor. Uses existing `DistractorRationale` to generate guiding questions when a student answers incorrectly.

**Files to create**:
- `src/actors/Cena.Actors/Protocols/SocraticProtocolActor.cs`

**Requirements**:
- On wrong answer: extract `DistractorRationale` for the chosen option
- Generate a guiding question from the rationale (template-based first, LLM fallback)
- Allow one re-attempt after the guiding question
- If still wrong: show full explanation
- Emit `InteractiveStepCompleted_V1` for each step
- BKT credit: first attempt wrong + second attempt correct = partial credit (0.5x weight)

**Depends on**: TASK-IQ-06 (envelope format)
**Effort**: 2-3 days

---

### TASK-IQ-08: Prototype Feynman Protocol Actor

**Goal**: Explain-then-choose protocol. Student writes explanation first, then answers MCQ. Dual-signal scoring.

**Files to create**:
- `src/actors/Cena.Actors/Protocols/FeynmanProtocolActor.cs`

**Requirements**:
- Step 1: Show question stem WITHOUT options. Prompt: "Explain your thinking about this concept before seeing the choices."
- Step 2: Evaluate explanation via `LlmRubricEvaluator` (lenient rubric — student hasn't seen options yet, score for conceptual direction)
- Step 3: Present MCQ options
- Step 4: Student selects answer (standard MCQ evaluation)
- Dual signal: explanation score -> Bloom's progression (via TASK-IQ-04), MCQ answer -> BKT
- Show explanation feedback alongside MCQ result

**Depends on**: TASK-IQ-03, TASK-IQ-04, TASK-IQ-06
**Effort**: 2-3 days

---

### TASK-IQ-09: "Justify Your Answer" Lightweight Protocol

**Goal**: MCQ + 1-2 sentence justification. Cheaper than full Feynman, activated at mastery 0.30-0.60.

**Files to create**:
- `src/actors/Cena.Actors/Protocols/JustifyProtocolActor.cs`

**Requirements**:
- Step 1: Present MCQ (standard)
- Step 2: After MCQ answer, prompt: "In 1-2 sentences, explain why you chose this answer."
- Step 3: Evaluate justification via `LlmClassificationEvaluator` (Kimi K2.5 — classify as: sound reasoning / partial understanding / guessed)
- If MCQ correct but justification = "guessed": reduce BKT credit to 0.5x (catches lucky guesses)
- If MCQ wrong but justification = "sound reasoning": log as near-miss for analytics
- Activate when: methodology is not Drill&Practice or SpacedRepetition AND mastery is 0.30-0.60

**Depends on**: TASK-IQ-03, TASK-IQ-06
**Effort**: 1-2 days

---

### TASK-IQ-10: Protocol Router in SessionActor

**Goal**: Wire up the protocol actors so `LearningSessionActor` selects and activates the right protocol based on active methodology and mastery.

**Files to modify**:
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Requirements**:
- Check active methodology from MCM graph
- Check mastery level for current concept
- Select protocol:
  - Socratic methodology + any mastery -> SocraticProtocolActor
  - Feynman methodology + mastery > 0.60 -> FeynmanProtocolActor
  - Any non-drill methodology + mastery 0.30-0.60 -> JustifyProtocolActor
  - Drill&Practice or SpacedRepetition -> no protocol (plain question)
  - No active methodology -> no protocol
- If authored interactive content exists on the question, prefer it over actor-generated protocol (Hybrid Option C resolution logic)
- Deliver via `InteractiveSessionEnvelope` through SignalR

**Depends on**: TASK-IQ-07, TASK-IQ-08, TASK-IQ-09
**Effort**: 2-3 days

---

## Phase 3: Infrastructure

### TASK-IQ-11: Define ConceptResourceBank Aggregate

**Goal**: Event-sourced aggregate for per-concept learning resources (worked examples, analogies, scaffold templates).

**Files to create**:
- `src/actors/Cena.Actors/Concepts/ConceptResourceBankState.cs`
- `src/actors/Cena.Actors/Events/ConceptResourceEvents.cs`
- `src/api/Cena.Admin.Api/ConceptResourceDtos.cs`
- Admin API endpoints for CRUD

**Requirements**:
- Aggregate per concept (stream: `cena.concept-resources.{conceptId}`)
- Resource types: `WorkedExample`, `Analogy`, `ScaffoldTemplate`
- Events: `WorkedExampleAdded_V1`, `AnalogyAdded_V1`, `ScaffoldTemplateAdded_V1`
- Each resource: content (text + HTML), language, Bloom's level, quality score
- Used by Worked Example protocol actor, Analogy protocol actor, Scaffolded protocol actor

**Depends on**: Nothing
**Effort**: 3-4 days

---

### TASK-IQ-12: Add workedExampleRef and variationParams to QuestionState

**Goal**: Optional fields on the question aggregate for hand-crafted interactive content.

**Files to modify**:
- `src/actors/Cena.Actors/Questions/QuestionState.cs`
- `src/actors/Cena.Actors/Events/QuestionEvents.cs` (new events)
- `src/api/Cena.Admin.Api/QuestionBankDtos.cs`
- `src/api/Cena.Admin.Api/QuestionBankService.cs`

**Requirements**:
- `QuestionState.WorkedExampleRef: string?` — optional link to a worked example in `ConceptResourceBank`
- `QuestionState.VariationParams: List<string>?` — parameterizable parts of the stem for spaced repetition variation
- Events: `WorkedExampleLinked_V1`, `VariationParamsSet_V1`
- Admin UI: expose in question edit form

**Depends on**: TASK-IQ-11
**Effort**: 1-2 days

---

## Phase 4: Validate

### TASK-IQ-13: A/B Test Framework for Protocols

**Goal**: Measure learning outcomes for actor-wrapped interactive vs static delivery.

**Requirements**:
- Random assignment: 50% of students get interactive protocols, 50% get plain questions
- Track per-group: mastery velocity (time to reach 0.85), retention at 7 days, session duration, hint usage
- Segment by: methodology, subject, mastery starting level
- Dashboard in Admin UI showing A/B results
- Run for minimum 2 weeks with minimum 100 students per group

**Depends on**: TASK-IQ-10 (protocols working)
**Effort**: 3-5 days

---

### TASK-IQ-14: A/B Test MCQ-Only vs Mixed Format

**Goal**: Compare learning outcomes for MCQ-only sessions vs mixed-format sessions at equivalent mastery levels.

**Requirements**:
- Same A/B framework as TASK-IQ-13
- Control: MCQ-only (current behavior)
- Treatment: mixed format with mastery-gated type progression
- Key metrics: Bloom's level advancement, exam-format readiness score, student engagement (session length, return rate)

**Depends on**: TASK-IQ-01, TASK-IQ-03, TASK-IQ-13
**Effort**: 1-2 days (framework reuse)

---

### TASK-IQ-15: LLM Grading Validation

**Goal**: Validate LLM grading reliability against human graders on actual Bagrut-format responses.

**Requirements**:
- Collect 100+ student responses to open-ended questions (once live)
- Have 2 human graders score each response using Bagrut rubrics
- Run same responses through `LlmRubricEvaluator`
- Compute inter-rater kappa: LLM vs Human1, LLM vs Human2, Human1 vs Human2
- Target: LLM kappa >= 0.65 (comparable to human inter-rater agreement)
- If below target: iterate on rubric prompt design

**Depends on**: TASK-IQ-03, live student data
**Effort**: 3-5 days (mostly manual grading coordination)

---

## Summary

| Phase | Tasks | Total Effort |
| --- | --- | --- |
| Phase 1: Mixed Format Foundation | IQ-01 through IQ-05 | 8-13 days |
| Phase 2: Interactive Protocols | IQ-06 through IQ-10 | 8-13 days |
| Phase 3: Infrastructure | IQ-11, IQ-12 | 4-6 days |
| Phase 4: Validate | IQ-13 through IQ-15 | 7-12 days |
| **Total** | **15 tasks** | **27-44 days** |

### Dependency Graph

```text
                    IQ-01 (Type-aware selector)
                    IQ-02 (Seed questions) ──────┐
                         │                       │
                         v                       v
                    IQ-03 (AnswerEvaluator) ◄────┘
                    /    |    \
                   v     v     v
             IQ-04   IQ-05   IQ-06 (Envelope design)
          (Bloom's) (Rubric)      |
                                  v
                    ┌─── IQ-07 (Socratic) ───┐
                    ├─── IQ-08 (Feynman) ────┤
                    ├─── IQ-09 (Justify) ────┤
                    │                        │
                    v                        v
                    IQ-10 (Protocol Router)
                         │
                         v
                    IQ-13 (A/B framework)
                    /              \
                   v                v
             IQ-14 (MCQ vs mixed)  IQ-15 (LLM validation)

         IQ-11 (ConceptResourceBank) ── independent
              │
              v
         IQ-12 (workedExampleRef)
```

### Critical Path

IQ-02 -> IQ-03 -> IQ-06 -> IQ-07/08/09 -> IQ-10 -> IQ-13

Phase 1 (IQ-01 through IQ-05) and Phase 3 (IQ-11, IQ-12) can run in parallel.
