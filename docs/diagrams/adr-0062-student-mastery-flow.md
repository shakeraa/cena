# Student session → concept mastery flow (ADR-0062 + ADR-0039 + ADR-0050)

End-to-end picture of a student answering a question and how the multi-concept tagging from Phase 1 feeds the BKT mastery loop, while keeping ADR-0039 (single-skill BKT) intact.

## Mermaid diagram

```mermaid
flowchart TD
    subgraph Student
        S0["Student opens Cena<br/>(SPA / future PWA)"]
        S1["Student answers question"]
        S2["Sees feedback +<br/>distractor rationale"]
        S3["Next question"]
    end

    subgraph SessionStart["Session start"]
        StartEp["POST /api/me/sessions/start<br/>(SessionEndpoints)"]
        ExamTarget[("Active ExamTarget<br/>per ADR-0050:<br/>(student, examTarget, skill)")]
        Plan["StudentPlan reader<br/>QuestionPaperCodes filter"]
    end

    subgraph QuestionDelivery["Question delivery (Student API host)"]
        Adaptive["AdaptiveQuestionPool"]
        Pool["MartenQuestionPool<br/>queries QuestionReadModel<br/>policy: AllowReferenceItems=false<br/>+ QuestionPaperCodes intersect"]
        ReadModel[("QuestionReadModel<br/>Concepts: List[SkillCode]<br/>(populated by Phase 1<br/>extraction projection)")]
        Selector["Selector picks question<br/>by Bloom + Elo difficulty<br/>(target ~0.85 success)"]
        Deliver["Question delivered<br/>(stem, options, figures,<br/>scaffolding rung)"]
    end

    subgraph AnswerHandling["Answer handling"]
        AnswerEp["POST /sessions/{id}/answer"]
        Verify["CAS-verified answer match<br/>(SymPy oracle, ADR-0002)"]
        Feedback["Feedback engine<br/>L1 explanation +<br/>per-distractor rationale"]
        Misc["Misconception detector<br/>(session-scoped, ADR-0003<br/>30-day retention)"]
    end

    subgraph MasteryUpdate["Mastery update — BKT keys on PRIMARY only (ADR-0039)"]
        Attempted["ConceptAttempted_V3<br/>conceptId = PrimaryConceptId<br/>(first element of question.Concepts)"]
        BKT["BktService.UpdateMastery<br/>Koedinger defaults:<br/>P(L0)=0.3, P(T)=0.1,<br/>P(G)=0.2, P(S)=0.1"]
        MasteryDoc[("StudentMasteryDocument<br/>keyed (student, examTarget,<br/>skill) per ADR-0050")]
    end

    subgraph SupportingNudge["Supporting concepts (Phase 2 — gated, OFF until N≥10)"]
        StabilityGate{"IConceptItemPublicationCounter<br/>≥10 published items<br/>per supporting leaf?"}
        Nudge["MasterySignalEmitted_V1<br/>(positive-only, ½ post-reflection delta,<br/>never decrements)"]
        SkipNudge["Skip — leaf below floor;<br/>posteriors too noisy<br/>(van de Sande 2013)"]
    end

    subgraph SchedulingLoop["Adaptive scheduling loop"]
        Stagnation["StagnationDetector<br/>3+ wrong on _sessionPrimaryConceptId<br/>→ scaffolding bump"]
        NextPick["AdaptiveQuestionPool.GetNext<br/>(BKT mastery + PSI<br/>prerequisite check<br/>+ ExamTarget filter)"]
    end

    S0 --> StartEp
    StartEp --> ExamTarget
    StartEp --> Plan
    Plan -->|paperCodes| Pool

    StartEp --> Adaptive
    Adaptive --> Pool
    Pool --> ReadModel
    Pool --> Selector
    Selector --> Deliver
    Deliver --> S1

    S1 --> AnswerEp
    AnswerEp --> Verify
    Verify --> Feedback
    Verify --> Misc
    Feedback --> S2

    AnswerEp --> Attempted
    Attempted --> BKT
    BKT --> MasteryDoc

    AnswerEp -.->|"phase 2:<br/>for each supporting concept"| StabilityGate
    StabilityGate -->|yes| Nudge
    StabilityGate -->|no| SkipNudge
    Nudge --> MasteryDoc

    MasteryDoc -.->|read| NextPick
    AnswerEp --> Stagnation
    Stagnation --> NextPick
    NextPick --> Adaptive
    S2 --> S3
    S3 --> NextPick

    classDef event fill:#fff3cd,stroke:#856404,stroke-width:1px;
    classDef doc fill:#d1ecf1,stroke:#0c5460,stroke-width:1px;
    classDef gate fill:#f8d7da,stroke:#721c24,stroke-width:2px;
    classDef phase2 fill:#e2e3e5,stroke:#6c757d,stroke-width:1px,stroke-dasharray: 5 5;
    classDef adr fill:#d4edda,stroke:#155724,stroke-width:2px;

    class Attempted event;
    class MasteryDoc,ReadModel,ExamTarget doc;
    class Verify,StabilityGate gate;
    class Nudge,SkipNudge phase2;
    class BKT adr;
```

## How concept mastery actually moves

### What's live today (Phase 1)

- The question's full concept set (primary + supporting) lives on the event stream and is projected onto `QuestionReadModel.Concepts`.
- `MartenQuestionPool` reads that field and indexes published questions by **every** concept in the set, so concept-keyed selectors (PSI, CAT) see the full coverage.
- BKT continues to fire on **`PrimaryConceptId` only** — the primary is the first element of `QuestionState.ConceptIds` after replay. This preserves `ADR-0039` single-skill BKT semantics so the existing identifiability bounds (Koedinger defaults, ≥10-items-per-skill stability floor) are not violated.

### What's deferred (Phase 2, gated)

- `MasterySignalEmitted_V1` nudge channel for **supporting** concepts. Half the post-reflection delta, positive-only, never decrements — keeps the math from over-claiming on weak evidence.
- Gated by `IConceptItemPublicationCounter`: a leaf only starts receiving nudges once **≥10 published items** carry it. Below the floor, posteriors are too noisy to defend (van de Sande 2013).
- Default impl is `NullConceptItemPublicationCounter` → returns 0 → gate stays CLOSED. Phase 2 turn-on is a one-line registration swap to a Marten-backed counter, never accidental.

### Why BKT keys on `(student, examTarget, skill)` not `(student, skill)`

ADR-0050: a student preparing for two different Bagrut tracks (e.g. 4u in 11th + 5u in 12th) needs SEPARATE mastery rows because the same `SkillCode` ("math.calculus.derivative-rules") shows up at both tracks with different difficulty distributions. Collapsing across tracks contaminates the posterior. The exam-target key is the lever that lets the student have multiple "in-flight" mastery profiles.

## Where each step lives in code

| Step | File |
|---|---|
| Session start endpoint | [SessionEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs) |
| Adaptive question selection | [AdaptiveQuestionPool.cs](../../src/actors/Cena.Actors/Serving/AdaptiveQuestionPool.cs) |
| Pool query (Marten-backed) | [MartenQuestionPool.cs](../../src/actors/Cena.Actors/Serving/MartenQuestionPool.cs) |
| Concept set projection | [QuestionListProjection.cs](../../src/actors/Cena.Actors/Questions/QuestionListProjection.cs) |
| Aggregate replay (event-sourced) | [QuestionState.cs](../../src/actors/Cena.Actors/Questions/QuestionState.cs) |
| Answer endpoint | [SessionEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs) |
| BKT update | [BktService.cs](../../src/actors/Cena.Actors/Mastery/BktService.cs) |
| Stagnation tracker | [StudentActor.Commands.cs](../../src/actors/Cena.Actors/Students/StudentActor.Commands.cs) |
| Phase 2 stability gate (placeholder) | [IConceptItemPublicationCounter.cs](../../src/actors/Cena.Actors/Mastery/Extraction/IConceptItemPublicationCounter.cs) |

## How to import to draw.io

Same as the PDF flow:
1. https://app.diagrams.net.
2. **Arrange → Insert → Advanced → Mermaid…**
3. Paste the ` ```mermaid ` block above.
4. **Insert** — diagram becomes fully editable native shapes.

Importable .drawio XML at [adr-0062-student-mastery-flow.drawio](./adr-0062-student-mastery-flow.drawio).
