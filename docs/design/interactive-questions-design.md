# Interactive vs Static Questions — Design Decision Document

## Context

Cena's question bank currently supports **static questions** — fixed stem, 4 options, single correct answer, binary scoring. For learning mode (as opposed to assessment mode), we want questions that adapt to the student's active methodology and learning state.

## Decision Required

**Should interactive behavior be a property of the question itself, or should the actor layer wrap any static question with interactive behavior based on the active methodology?**

---

## Option A: Question-Level Interactivity

Each question is authored with explicit interactive content baked in.

### Data Model

```
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
- Hard to scale — 100 questions × 9 methodologies = 900 interaction variants
- Methodology changes require re-authoring questions
- Question bank becomes tightly coupled to specific methodologies

### Best For
- High-stakes, carefully curated content (e.g., Bagrut flagship questions)
- Subjects where the pedagogical approach is inherently part of the question (e.g., worked examples in math)

---

## Option B: Actor-Wrapped Interactivity

Questions remain static. The **QuestionPoolActor** and **SessionActor** wrap any question with interactive behavior at serve-time based on the student's active methodology.

### Architecture

```
Student requests question
  → QuestionPoolActor selects static question (difficulty, concept match)
  → SessionActor checks active methodology for this concept
  → SessionActor wraps question with methodology-specific interaction protocol
  → Student receives interactive session envelope
```

### Interaction Protocols by Methodology

| Methodology | Protocol | Actor Behavior |
|---|---|---|
| **Socratic** | Wrong → follow-up guiding question → re-attempt | Actor generates hint question from distractor rationale |
| **Worked Example** | Wrong → show similar solved problem → re-ask | Actor pulls worked example from concept's example bank |
| **Feynman** | Free-text explain → then show options → score both | Actor prompts explanation, evaluates with LLM, then shows MCQ |
| **Scaffolded** | Break into sub-steps → build to full question | Actor decomposes based on Bloom's level and prerequisite graph |
| **Retrieval Practice** | Show stem only (recall) → then show options | Actor serves in 2 phases: open recall, then MCQ |
| **Spaced Repetition** | Same question with parameter variation | Actor uses variation template or synonym substitution |
| **Drill & Practice** | Rapid-fire, timed, minimal feedback | Actor sets timer, shows correct/incorrect immediately |
| **Analogy** | Wrong → show analogous concept → re-ask | Actor maps concept to analogy bank |
| **Bloom's Progression** | Start at Remember → escalate to Apply/Analyze | Actor selects progressively harder questions on same concept |

### Data Model Addition (Minimal)

```
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

### Pros
- Every existing question becomes interactive with zero re-authoring
- New methodologies can be added without touching questions
- The MCM graph drives methodology selection, actors drive interaction
- Scales naturally: N questions × M methodologies handled by M protocol actors
- A/B testing methodology effectiveness is trivial (change protocol, same questions)
- Aligns with Cena's actor-based architecture

### Cons
- Runtime complexity in actors (but this is what actors are designed for)
- Generated interactions may be lower quality than hand-crafted
- Some methodologies need supplementary content (worked examples, analogies)
- Feynman mode requires LLM evaluation at serve-time

### Best For
- Scale — hundreds of questions across all methodologies
- Data-driven methodology optimization (the MCM graph learns what works)
- Rapid iteration on pedagogy without content bottleneck

---

## Option C: Hybrid

Base questions are static. A subset of high-value questions get hand-crafted interactive content. The actor layer provides a baseline interactive experience for all questions, but defers to authored content when available.

### Priority Matrix

| Content Type | Authoring | Actor Wrapping |
|---|---|---|
| Distractor rationales | Required (already exist) | Used by Socratic protocol |
| Hints | Optional (author can add) | Auto-generated from rationale if missing |
| Worked examples | Per-concept bank (shared) | Actor selects closest match |
| Scaffold sub-questions | Optional per question | Actor auto-decomposes if missing |
| Variation parameters | Optional per question | Actor uses synonym substitution fallback |
| Free-text prompts | Optional | Actor uses generic "Explain your reasoning" |

### Resolution Logic

```
if question.hasAuthoredInteractiveContent(methodology):
    use authored content (highest quality)
elif conceptBank.hasResource(concept, methodology):
    use concept-level resource (shared, curated)
else:
    use actor-generated protocol (automated, good-enough)
```

---

## Research Questions

Before deciding, we need evidence on:

1. **Effectiveness of auto-generated vs hand-crafted hints**: Do students learn equally well from distractor-rationale-based hints vs pedagogist-written hints?

2. **Socratic questioning in digital environments**: What does the research say about automated Socratic dialogue effectiveness in math/science education?

3. **Worked examples effect**: How much of the "worked example effect" (Sweller) depends on the example being tailored to the specific question vs a related concept?

4. **Scaffolding granularity**: What's the optimal number of scaffold steps? Does auto-decomposition work as well as expert decomposition?

5. **Retrieval practice with delayed feedback**: Does the 2-phase (recall then MCQ) approach improve retention vs standard MCQ?

6. **Explain-first (Feynman) in MCQ context**: Does requiring explanation before answering improve learning outcomes in timed assessment contexts?

7. **Methodology switching overhead**: Is there a cognitive cost to changing interaction patterns between questions in the same session?

8. **Israeli Bagrut context**: Are there cultural or curricular factors that favor one approach for Hebrew/Arabic-speaking students?

---

## Preliminary Recommendation

**Option C (Hybrid)** with actor-wrapping as the default and authored content as progressive enhancement.

Rationale:
- Gets interactive learning live immediately (zero authoring needed)
- Quality improves over time as content creators add per-question/per-concept resources
- Aligns with the event-sourced architecture (interactions are events, enabling A/B analysis)
- The MCM graph already decides methodology — actors just execute the protocol
- Doesn't block on content creation bottleneck

---

## Next Steps

1. **Research**: Validate approach against educational research literature
2. **Prototype**: Build one methodology protocol (Socratic) as actor proof-of-concept
3. **Measure**: A/B test actor-wrapped vs static delivery on a subset of students
4. **Iterate**: Add more protocols based on measured effectiveness
