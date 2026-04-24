# Micro-Lessons: Interactive & Video-Based Per-Concept Learning

**Date:** 2026-03-28
**Status:** Proposal
**Author:** Pedagogy & Architecture Review
**Classification:** Student-facing content strategy — extends Content Authoring bounded context

---

## 1. The Gap

Cena today is **assessment-only**. Students face a loop:

```
Question → Answer → (Correct? Next question. Wrong? Static explanation.) → Repeat
```

There is no **teaching moment**. The system adaptively selects questions, tracks mastery via BKT, detects stagnation, routes methodologies — but never *teaches*. It evaluates. The explanation templates (Socratic, direct, worked example) specified in `content-authoring.md` Stage 3 are a step toward this, but they are post-failure remediation, not proactive instruction.

**The research is unambiguous:** Assessment without instruction is incomplete. The system knows *what* a student doesn't know (BKT), *why* they're failing (MCM error-type routing), and *how* they learn best (methodology map). It has every signal needed to deliver the right lesson at the right time — it just doesn't have the lessons.

---

## 1b. Israeli Education Context: Why Micro-Lessons Matter Here

### The Bagrut Gatekeeping Problem

The Bagrut matriculation exam is Israel's primary academic gatekeeper. Research shows that targeted remedial interventions significantly impact pass rates:

- **Lavy & Schlosser (2005):** Israel's "Bagrut 2001" program — small-group tutoring and after-school sessions for marginal students — increased Bagrut pass rates by 3-4 percentage points at ~$300 per additional certificate. This is one of the most cost-effective educational interventions measured in a randomized study. *Micro-lessons are the scalable digital equivalent of this targeted remediation.*
- **Lavy (2009):** A randomized experiment in Israeli high schools found teacher incentive pay linked to Bagrut outcomes improved student achievement by 0.2-0.3 SD. The mechanism was increased instructional effort — more targeted teaching moments. *Cena's micro-lessons automate this: more teaching moments per student, without requiring more teacher effort.*

### The Arab-Israeli Achievement Gap

Arab-Israeli students face a dual challenge: learning math concepts while navigating Hebrew-dominant testing:

- **Shohamy (2010):** Arab-Israeli students process Bagrut exams through a Hebrew lens while receiving instruction in Arabic, creating linguistic transfer costs. Math terminology must be encoded in both languages.
- **Sweller, Ayres & Kalyuga (2011):** Bilingual processing imposes measurable extraneous cognitive load, particularly for technical vocabulary in a non-dominant language. For Arabic-speaking students taking Hebrew Bagrut exams, every math term carries a double processing cost.
- **Clarkson (2007):** When bilingual students process math in their weaker language, problem-solving accuracy drops significantly. Students who could flexibly code-switch performed better — *supporting Cena's decision to serve micro-lessons in the student's dominant language, not the exam language.*

**Design implication:** Micro-lessons for Arabic-speaking students should teach concepts in Arabic first (reducing cognitive load), then bridge to Hebrew Bagrut terminology as a final step. This is a two-phase lesson structure:

```
Phase 1: Concept acquisition in dominant language (Arabic)
  → Teach the math idea with Arabic terminology
  → Interactive checkpoint: "Do you understand the concept?"

Phase 2: Terminology bridge to Bagrut language (Hebrew)
  → Map Arabic terms to Hebrew Bagrut terms
  → Practice recognizing the concept in Hebrew exam phrasing
  → Checkpoint: "Identify this concept in a Bagrut-style question"
```

This two-phase structure is unique to Cena and directly addresses the bilingual cognitive load gap that no existing Israeli EdTech platform handles.

### What Exists vs. What's Needed

Israeli adaptive learning is under-researched in English-language literature (Tabesh, 2016). The closest empirical basis comes from international ITS research:

- **Pane et al. (2015, RAND Corporation):** Schools using personalized/adaptive learning platforms showed gains of ~3 percentile points on standardized math assessments. But these systems had instructional content — Cena currently does not.
- **Arroyo et al. (2014):** Adaptive tutoring that addresses cognition, metacognition, and affect produces 0.3-0.5 SD math gains. Cena tracks all three (BKT for cognition, methodology for metacognition, FocusState for affect) but only uses them for question selection, not instruction.

**The gap is clear:** Cena has world-class adaptive *assessment*. It needs adaptive *instruction* to match. Micro-lessons close this gap.

---

## 2. What Are Micro-Lessons?

A **micro-lesson** is a 2–5 minute, concept-focused learning unit that teaches one idea through one method. It is:

- **Atomic**: One concept, one objective, one assessment checkpoint
- **Method-specific**: The same concept gets different micro-lessons for different methodologies
- **Multi-modal**: Can be video, interactive simulation, guided walkthrough, or text+diagrams — matched to content type and methodology
- **Adaptive**: Delivered based on student state, not curriculum sequence

### Why "Micro"?

| Research | Finding | Source |
|----------|---------|--------|
| Cognitive load theory | Working memory holds 4±1 chunks; lessons >5 min exceed capacity for novel concepts | Sweller (2011) |
| Attention research | Student attention peaks at 3-4 minutes for novel material, 8-10 for review | Bunce, Flens & Neiles (2010) |
| Mobile learning | Israeli students' median study session on mobile is 6-12 min | Cena's target context |
| Spaced practice | Many short exposures beat fewer long ones for retention (already exploited by HLR/FSRS) | Settles & Meeder (2016) |
| Video completion rates | Completion drops 50% at 6 min, 80% at 12 min for educational video | Guo, Kim & Rubin (2014), edX data |

The system's existing ZPD-targeted question selection already fragments learning into small adaptive steps. Micro-lessons should follow the same grain size.

---

## 3. The Case for Multiple Methods per Concept

Cena already tracks 9 methodologies and routes students to the best one via MCM error-type analysis. **Micro-lessons must be methodology-aligned** — a Socratic micro-lesson for quadratic equations looks fundamentally different from a Worked Example micro-lesson for the same concept.

### 3.1 Why Different Methods, Not Just Different Explanations

This is not about repeating the same content in different words. Each methodology implies a different **cognitive pathway**:

| Methodology | Micro-Lesson Approach | Cognitive Mechanism | Best For |
|-------------|----------------------|---------------------|----------|
| **WorkedExample** | Step-by-step video/animation showing the full solution process, then fading steps | Schema acquisition through observation → imitation | Novices (Bloom 1-2), procedural errors |
| **Socratic** | Interactive dialogue: system poses guiding questions, student selects/types answers before proceeding | Productive struggle → self-generated insight | Conceptual errors, students with partial knowledge |
| **Feynman** | Student watches a simplified explanation, then is asked to explain it back (voice/text) | Teach-to-learn, metacognitive awareness | Students who "think they know" but can't articulate |
| **Analogy** | Concept mapped to familiar domain via animated comparison (e.g., derivatives as speedometers) | Analogical transfer from known to unknown | Abstract concepts, cross-domain thinkers |
| **RetrievalPractice** | Brief review with fill-in-the-blank pauses — student must recall before seeing the answer | Testing effect strengthens retrieval routes | Previously mastered concepts at risk of decay |
| **DrillAndPractice** | Rapid-fire interactive exercises with immediate feedback, progressively harder | Automaticity through repetition | Procedural fluency (arithmetic, formula application) |
| **BloomsProgression** | Layered lesson: starts at "remember" (definition), builds to "apply" (use in context), ends at "analyze" | Scaffolded complexity | New topics, structured learners |
| **SpacedRepetition** | Compressed review micro-lesson surfaced at optimal recall intervals (HLR-driven) | Memory consolidation at forgetting threshold | Retention maintenance |
| **ProjectBased** | Mini-scenario: "You're designing a ramp for a wheelchair — what angle minimizes effort?" — concept embedded in real problem | Situated cognition, intrinsic motivation | High Bloom's (4-6), engagement recovery |

### 3.2 Research Basis for Multi-Method Delivery

- **Clark & Mayer (2016):** No single instructional method outperforms for all learners. Method-content alignment (methodology × concept type × student state) is the strongest predictor of learning gain.
- **Chi & Wylie (2014) ICAP Framework:** Learning increases as student activity moves from Passive → Active → Constructive → Interactive. Different methodologies place students at different ICAP levels.
- **Pashler et al. (2008):** The "learning styles" myth (visual/auditory/kinesthetic) has no evidence. But **method-matched instruction** (matching teaching approach to content type and student knowledge level) has strong evidence.
- **Rohrer & Taylor (2007):** Interleaving methods for the same concept produces better transfer than blocked practice with one method. Micro-lessons enable natural interleaving.
- **Freeman et al. (2014, PNAS):** Meta-analysis of 225 STEM studies — the largest ever conducted. Active learning increased exam scores by +0.47 SD and reduced failure rates by 1.5×. This is not a marginal finding; it is the single strongest evidence base for why assessment-only systems underperform. *Cena's current question-only loop is the "traditional lecture" of adaptive learning — it tests but doesn't teach actively.*
- **Ma, Adesope, Nesbit & Liu (2014):** Meta-analysis of 107 effect sizes from 73 ITS studies. Intelligent tutoring systems that deliver instruction (not just assessment) outperform teacher-led large-group instruction (g = 0.42), non-ITS software (g = 0.57), and textbooks (g = 0.35). No significant difference vs. individual human tutoring. *This is the target Cena should aim for: ITS-level instruction, not just ITS-level assessment.*
- **VanLehn (2011):** Step-level adaptive tutoring (responding to each step within a problem, not just the final answer) achieved effect sizes of ~0.76 SD — approaching expert human tutoring (~1.0 SD). Problem-level tutoring achieved only ~0.31 SD. *Micro-lessons with interactive checkpoints are step-level; passive video is problem-level. The checkpoint design matters more than the video quality.*
- **Graesser, Conley & Olney (2012):** Systems that adapt to the ZPD through mastery-based progression and error-contingent scaffolding consistently outperform fixed-sequence instruction. Effect sizes 0.4-1.0 SD depending on implementation quality. *Cena already has ZPD targeting in QuestionSelector; micro-lessons extend this from "right question" to "right lesson at the right difficulty."*

**Critical distinction:** This is not "learning styles" (debunked). This is methodology-content-state alignment — choosing the right teaching approach based on what the student is struggling with, what the concept demands, and what their mastery level is. Cena's MCM routing already encodes this logic; micro-lessons give it something to route *to*.

### 3.3 Expected Effect Sizes for Cena

Based on the meta-analytic evidence above, micro-lessons should produce compound gains:

| Component | Expected Effect | Evidence Base | Cena Integration Point |
| ----------- | ---------------- | --------------- | ---------------------- |
| Active instruction added to assessment | +0.42-0.57 SD | Ma et al. (2014) | LessonPoolActor + LearningSessionActor |
| Step-level interactivity (checkpoints) | +0.45 SD (delta over problem-level) | VanLehn (2011) | InteractiveElement within micro-lessons |
| Methodology-matched delivery | +0.2-0.3 SD (above random method) | Clark & Mayer (2016) | MCM routing → LessonPoolActor selection |
| ZPD-targeted lesson difficulty | +0.3-0.5 SD | Graesser et al. (2012) | QuestionSelector difficulty → lesson difficulty |
| **Combined (not additive)** | **~0.5-0.8 SD estimated** | Compound of above | Full integration |

A 0.5-0.8 SD improvement is the difference between a student scoring at the 50th percentile and scoring at the 69th-79th percentile. For Bagrut pass rates, this maps to roughly 10-15 additional percentage points for marginal students (consistent with Lavy & Schlosser, 2005 findings for targeted tutoring).

---

## 4. Modality: Interactive vs. Video vs. Hybrid

### 4.1 The Evidence

| Modality | Effect Size | Cost to Produce | Scalability | Offline? | Methodology Fit |
|----------|-------------|-----------------|-------------|----------|-----------------|
| **Interactive simulation** | 0.4-0.8σ (Sims et al., 2020) | High (custom dev) | Low per concept | Depends | Socratic, DrillAndPractice, ProjectBased |
| **Short video** (< 3 min) | 0.2-0.4σ (Hattie, 2023 meta) | Medium (Remotion/AI) | High | Yes | WorkedExample, Analogy, Feynman |
| **Guided walkthrough** (text + interactive steps) | 0.3-0.5σ (Chi & Wylie, 2014) | Low-Medium | High | Yes | BloomsProgression, SpacedRepetition |
| **Voice-narrated diagram** | 0.3-0.5σ (Mayer, 2021 multimedia principle) | Low (TTS + SVG) | Very high | Yes |  WorkedExample, Analogy |
| **Conversational (TutorActor)** | 0.4-0.6σ (VanLehn, 2011) | Low per unit (LLM) | Cost-limited | No | Socratic, Feynman |

### 4.2 Recommendation: Methodology-Driven Modality Selection

Don't choose one modality. Let the methodology dictate the format:

```
Methodology           → Primary Modality              → Fallback (offline/cheap)
────────────────────────────────────────────────────────────────────────────────
WorkedExample         → Narrated step-by-step video    → Guided text walkthrough
Socratic              → Interactive Q&A dialogue        → Branching text scenario
Feynman               → Video + "explain back" prompt   → Text + voice recording
Analogy               → Animated comparison video       → Side-by-side diagram
RetrievalPractice     → Interactive fill-the-gaps       → Cloze text exercise
DrillAndPractice      → Timed interactive exercises     → Flashcard sequence
BloomsProgression     → Layered reveal (text + visuals) → Expandable text
SpacedRepetition      → Compressed review (any format)  → Key-point summary
ProjectBased          → Interactive scenario simulation  → Narrative problem text
```

### 4.3 Video Production Guidelines (Evidence-Based)

Research on educational video production provides specific, actionable rules for Cena's Remotion-based rendering pipeline:

**Guo, Kim & Rubin (2014)** analyzed 6.9 million video-watching sessions on edX and found:

| Finding | Implication for Cena |
| --------- | --------------------- |
| Videos < 6 min had highest engagement | Enforce 2-5 min cap in `MicroLesson.DurationSeconds` |
| Informal talking-head style outperformed high-production studio | Use natural voice TTS + handwritten-style tablet drawing (Khan Academy aesthetic), not polished animations |
| Tablet drawing (Khan Academy style) was the most engaging format | Remotion pipeline should prioritize SVG step-by-step reveal (simulating hand-drawing) over pre-rendered graphics |
| Videos interspersing talking head with slides outperformed slides-only | Alternate between narration-over-diagram and brief "explainer" segments |
| Videos produced with high-energy pace had higher engagement | TTS speech rate should be slightly faster than conversational (~160 wpm vs ~140 wpm) |
| Re-watching was highest for tutorial/how-to videos (WorkedExample methodology) | Pre-cache WorkedExample videos most aggressively — they get the most replays |

**Brame (2016)** synthesized video design principles for maximizing learning:

1. **Signaling**: Use visual cues (color changes, arrows, highlights) to draw attention to key steps. Cena's existing SVG diagram pipeline already supports highlight overlays.
2. **Segmenting**: Break into learner-paced segments. Each `InteractiveElement` checkpoint creates a natural segment boundary.
3. **Weeding**: Remove all non-essential information. Every frame must advance the learning objective.
4. **Matching modality**: Complex visuals (graphs, diagrams) should use audio narration, not on-screen text. Simple concepts can use text overlays.

**Figlio, Rush & Yin (2013):** In a randomized experiment, live vs. video instruction produced statistically indistinguishable learning outcomes. *This validates that video micro-lessons can substitute for live instruction — the medium is not the bottleneck, the pedagogical design is.*

**Means et al. (2013, US DOE):** Meta-analysis found blended learning (instruction + practice) outperformed face-to-face by +0.20 SD. Pure online and pure face-to-face were equivalent. *Cena's micro-lesson + question interleaving IS blended learning — the combination is what produces the gain.*

### 4.4 Mayer's Multimedia Principles (Non-Negotiable Design Rules)

Every micro-lesson — regardless of modality — must follow these empirically validated principles:

1. **Coherence**: No decorative content. Every element teaches.
2. **Signaling**: Visual cues guide attention (highlights, arrows, step markers).
3. **Redundancy**: Do NOT display the same text being narrated. Narration + visuals, not narration + text + visuals.
4. **Spatial contiguity**: Labels adjacent to the thing they describe, not in a separate legend.
5. **Temporal contiguity**: Narration synchronized with the visual step, not before/after.
6. **Segmenting**: User controls pace (pause, replay, step forward). No auto-advancing video.
7. **Pre-training**: Key terms introduced before the lesson, not during (prerequisite check!).
8. **Modality**: For complex visuals, use narration (audio) + diagram, not text + diagram.

---

## 5. When to Deliver Micro-Lessons (Adaptive Triggers)

Micro-lessons are NOT delivered on a fixed schedule. They are triggered by the adaptive engine:

### 5.1 Trigger Matrix

| Trigger | Signal Source | Micro-Lesson Type | Priority |
|---------|-------------|-------------------|----------|
| **New concept unlocked** | Learning frontier (prerequisites satisfied) | Introduction lesson (BloomsProgression: "remember" level) | HIGH |
| **First incorrect answer on concept** | BKT update, P(mastery) drops | Method-specific explanation (matches active methodology) | HIGH |
| **Stagnation detected** | `StagnationDetector` (accuracy plateau, 3+ sessions) | Different-method lesson (MCM suggests new approach) | HIGH |
| **Methodology switched** | `MethodologySwitched_V1` event | Brief orientation: "We're trying a different approach" | MEDIUM |
| **Concept at recall risk** | HLR: recall_probability < 0.5 and last_seen > 3 days | Compressed review micro-lesson (SpacedRepetition) | MEDIUM |
| **Bloom level transition** | Student ready for Apply→Analyze jump | Bridging lesson showing concept in new context | MEDIUM |
| **Session start — warm-up** | Student opens app, hasn't been active > 24h | 60-second review of last active concepts | LOW |
| **Focus degradation** | `FocusState == Declining` or `Degrading` | Switch from assessment to passive video (cognitive rest) | HIGH |
| **Error pattern repetition** | Same `ErrorType` 3+ times | Targeted misconception lesson | HIGH |

### 5.2 Key Insight: Focus-Aware Lesson Insertion

The focus tracking system (`FocusState`: Strong → Stable → Declining → Degrading → Critical) creates a natural insertion point. When cognitive load is high:

- **Declining**: Insert a passive video lesson (reduce active demand, maintain engagement)
- **Degrading**: Offer a choice: "Watch a quick review or take a break?"
- **Critical**: Session should end, but a 60-second animated summary of what was learned today reinforces before exit

This aligns with research on **desirable difficulty** (Bjork, 2011): challenge when fresh, consolidate when tired.

### 5.3 Spaced Repetition Micro-Lessons: The Empirical Case

The "concept at recall risk" trigger deserves special attention because the evidence base for spaced practice in math is exceptionally strong:

- **Cepeda, Pashler, Vul, Wixted & Rohrer (2006):** Meta-analysis of 254 studies. Spaced/distributed practice is one of the most robust findings in all of learning science. Effect sizes are typically large (d > 0.5). Optimal spacing depends on the target retention interval — for Bagrut prep (months of retention needed), spacing should expand from days to weeks.
- **Rohrer & Taylor (2006):** Distributed practice produced ~2× better retention of math skills at 4-week delay compared to massed practice. The effect was largest for procedural skills (formula application, algorithm execution) — exactly the content type that dominates Bagrut math.
- **Rohrer, Dedrick & Stershic (2015):** The cleanest K-12 math study available. 7th-grade students (N=126): interleaved practice produced **72% correct on delayed test vs. 38% for blocked practice**. This is not a small effect — it is nearly a 2× improvement in retention.
- **Rohrer & Taylor (2007):** Interleaving different problem types (which inherently creates spacing) improved delayed test performance by ~3×. *Cena already interleaves via QuestionSelector's exploration rate (10% softmax). Micro-lessons should follow the same interleaving principle — a SpacedRepetition micro-lesson should review a concept from a different angle than the original instruction.*

**Integration with HLR/FSRS (already built):**

Cena's `ConceptMasteryState` already tracks `HalfLifeHours` (HLR) and `Stability`/`Difficulty` (FSRS). The recall trigger for micro-lessons should use the same decay model:

```csharp
// In LessonTriggerService — mirrors QuestionSelector's spaced repetition logic
bool ShouldTriggerReviewLesson(ConceptMasteryState state)
{
    var recallProb = Math.Pow(2, -elapsedHours / state.HalfLifeHours);
    // Trigger review lesson when recall drops below 0.5 but above 0.2
    // Below 0.2 = forgotten, needs full re-instruction, not review
    return recallProb is > 0.2 and < 0.5
        && state.MasteryProbability > 0.7  // Only review previously mastered concepts
        && !state.IsCurrentlyActive;        // Don't review what they're actively studying
}
```

The 0.5 threshold aligns with the "desirable difficulty" sweet spot (Bjork, 2011) — the concept is partially forgotten, so retrieval requires effort (strengthening the memory trace) but is still achievable (preventing frustration).

---

## 6. Content Production Pipeline

### 6.1 Extending the Existing Content Authoring Pipeline

The content-authoring pipeline (Stage 3) already specifies explanation generation. Micro-lessons extend this:

```
Stage 3A: Micro-Lesson Generation (NEW — parallel to existing Stage 3)
────────────────────────────────────────────────────────────────────────
Input:  Concept node + corpus analysis + Bagrut style guide
Output: Per methodology, per concept:
        1. Lesson script (JSON: steps[], narration[], visuals[], checkpoints[])
        2. Diagram specs (SVG, reusable from existing Stage 3 diagram pipeline)
        3. Interactive exercise specs (for Socratic/Drill methodologies)
        4. Video render instructions (Remotion-compatible)
```

### 6.2 Generation Strategy: AI-Authored, Expert-Reviewed

| Component | Generator | Reviewer | Cost |
|-----------|-----------|----------|------|
| Lesson script (text + structure) | Kimi K2.5 (batch, corpus-informed) | Domain expert (existing pipeline) | ~$0.002/lesson |
| Narration audio | TTS (Hebrew: Google Cloud, Arabic: Azure) | Spot-check by native speaker | ~$0.001/minute |
| Animated diagrams | SVG from existing diagram pipeline | QA pass | Exists |
| Short video | Remotion (programmatic rendering from script) | Auto-QA + spot-check | ~$0.01/video |
| Interactive exercises | Template-based generation from question bank | Auto-test + expert review | Low |

### 6.3 Production Volume Estimate

Per concept × per methodology = 1 micro-lesson. Not every concept needs all 9 methodologies:

| Methodology | % of Concepts That Need It | Reason |
|-------------|---------------------------|--------|
| WorkedExample | 90% | Default for most math/science concepts |
| BloomsProgression | 80% | Standard introduction pathway |
| Socratic | 60% | For concepts with common misconceptions |
| Analogy | 40% | For abstract/theoretical concepts |
| RetrievalPractice | 70% | For any concept students need to retain |
| DrillAndPractice | 50% | For procedural/computational concepts |
| Feynman | 30% | For concepts where articulation reveals gaps |
| SpacedRepetition | 60% | Compressed reviews (can be auto-generated) |
| ProjectBased | 20% | For capstone/synthesis concepts |

For a subject with 2,000 concepts → ~2,000 × ~5 avg methods = ~10,000 micro-lessons per subject.

**This sounds like a lot. It's not.** The lesson *scripts* are batch-generated by Kimi K2.5 with corpus context (same pipeline as question generation). The *rendering* is programmatic (Remotion, SVG templates, TTS). Expert review covers scripts, not rendered output. Throughput: ~500 scripts/day generated, ~200 reviewed/day (1 expert).

---

## 7. Micro-Lesson Data Model

### 7.1 Domain Model (extends Content Authoring context)

```
MicroLesson
  Id: Guid
  ConceptId: string                    // "math_5u_derivatives_chain_rule"
  Methodology: Methodology             // WorkedExample, Socratic, etc.
  Language: string                     // "he", "ar", "en"
  Grade: int                           // 10, 11, 12
  BloomRange: (int min, int max)       // Target Bloom's level range
  DurationSeconds: int                 // Target: 120-300 seconds
  Modality: LessonModality             // Video, Interactive, GuidedText, VoiceDiagram

  // Content
  Script: LessonScript                 // Structured steps with narration + visuals
  DiagramSpecs: DiagramSpec[]          // SVG render instructions
  InteractiveElements: InteractiveElement[]  // Checkpoints, fill-gaps, choices
  VideoAssetUrl: string?               // Rendered video (if applicable)
  AudioAssetUrl: string?               // Narration audio

  // Quality
  Status: ContentStatus                // Draft → InReview → Approved → Published
  QualityScore: float                  // 0-100, same gate as questions
  ExpertReviewedAt: DateTimeOffset?

  // Provenance
  SourceType: ContentSourceType        // AIGenerated, Authored, Hybrid
  GenerationPrompt: string?            // For AI-generated: prompt used
  CorpusReferences: string[]           // Which Bagrut/textbook material informed it

  // Analytics (populated after serving)
  TimesServed: int
  CompletionRate: float                // % of students who finish
  AvgEngagementSeconds: float          // Actual time spent
  MasteryLiftAfterViewing: float       // ΔP(mastery) for students who saw this
```

### 7.2 Interactive Element Types

```
InteractiveElement
  Type: enum
    - PauseAndReflect    // "Before we continue, what do you think happens to f'(x) when..."
    - FillTheGap         // "The derivative of sin(x) is ___"
    - MultipleChoice     // Quick checkpoint (not graded, low stakes)
    - DragAndOrder       // "Put these steps in order"
    - SliderExplore      // "Move the slider to see how changing 'a' affects the parabola"
    - ExplainBack        // Feynman: "In your own words, explain why..."
    - BranchingChoice    // Socratic: "Which approach would you try first?"

  Position: int                        // Step index in the lesson where this appears
  CorrectResponse: string?             // For verifiable checkpoints
  FeedbackOnCorrect: string
  FeedbackOnIncorrect: string
  SkipAllowed: bool                    // false for Socratic/Feynman, true for review modes
```

---

## 8. Integration with Existing Architecture

### 8.1 Actor Model Extension

```
                    ┌───────────────────────┐
                    │   StudentActor         │
                    │   (existing)           │
                    │                        │
                    │ + resolveMicroLesson() │◄── Methodology + concept + mastery state
                    └───────────┬────────────┘
                                │ requests lesson
                                ▼
                    ┌───────────────────────┐
                    │  LessonPoolActor       │  ← NEW (mirrors QuestionPoolActor pattern)
                    │  (per subject)         │
                    │                        │
                    │  In-memory index:      │
                    │  conceptId × method    │
                    │  → sorted by quality   │
                    └───────────┬────────────┘
                                │ selected lesson
                                ▼
                    ┌───────────────────────┐
                    │  LearningSessionActor  │
                    │  (existing)            │
                    │                        │
                    │ + lessonState:         │
                    │   - currentLesson      │
                    │   - stepIndex          │
                    │   - checkpointResults  │
                    │   - engagementMetrics  │
                    └───────────────────────┘
```

### 8.2 Session Flow Change

**Before (assessment-only):**
```
[Question] → [Answer] → [Feedback] → [Question] → ...
```

**After (interleaved micro-lessons):**
```
[Concept unlocked] → [Intro micro-lesson] → [Question] → [Answer] →
  → (correct: next question)
  → (wrong: methodology-matched micro-lesson) → [Question retry] →
  → (stagnation: different-method micro-lesson) →
  → (focus declining: passive video review) →
  → (session end: 60s recap)
```

### 8.3 NATS Events (New)

```
cena.lesson.served      { studentId, lessonId, conceptId, methodology, trigger }
cena.lesson.completed   { studentId, lessonId, completionRate, engagementSeconds }
cena.lesson.checkpoint  { studentId, lessonId, elementId, response, correct }
cena.lesson.skipped     { studentId, lessonId, skipReason }
cena.serve.lesson.published  { lessonId, conceptId, methodology }  // triggers LessonPoolActor reload
```

### 8.4 BKT Integration

Micro-lesson completion should update mastery, but with different weights than question responses:

| Event | BKT Credit | Rationale |
|-------|-----------|-----------|
| Question correct (no hints) | 1.0× P(T) | Full evidence of learning |
| Question correct (with hints) | 0.4–0.7× | Partial evidence |
| Micro-lesson completed + checkpoint correct | 0.3× P(T) | Watched ≠ learned, but positive signal |
| Micro-lesson completed, checkpoint wrong | 0.1× P(T) | Engagement signal, not mastery |
| Micro-lesson skipped | 0.0× | No learning event |
| Interactive lesson, all checkpoints correct | 0.5× P(T) | Active engagement > passive watching |

This prevents "watch-to-mastery" gaming while still crediting genuine learning from lessons.

---

## 9. Bilingual & Cultural Considerations

### 9.1 Language-Specific Requirements

| Aspect | Hebrew (he) | Arabic (ar) | English (en) |
|--------|-------------|-------------|-------------|
| Narration voice | Natural Hebrew TTS, avoid robotic register | MSA with Levantine option for informal tone | Standard US/UK |
| Text direction | RTL (existing) | RTL (existing) | LTR |
| Math notation | Standard Latin + Hebrew labels | Standard Latin + Arabic labels | Standard |
| Cultural examples | Israeli daily life, Bagrut context | Arab-Israeli community context | Neutral academic |
| Video pacing | Slightly faster (Hebrew speakers process RTL content faster) | Standard pace | Standard |

### 9.2 Cultural Sensitivity in Analogies

The `CulturalContextDtos` already track `HebrewDominant`, `ArabicDominant`, `Bilingual` cohorts. Analogies in micro-lessons must be culturally appropriate:

- **Good**: "Think of derivatives like speed on a highway" (universal)
- **Risky**: Cultural-specific references that exclude one community
- **Rule**: Every analogy must be reviewed for cultural appropriateness across all cohorts. Flag in quality gate.

### 9.3 Bilingual Cognitive Load: Research-Based Lesson Design — RESOLVED

The open question of how to handle bilingual math instruction is answerable from the research:

**The problem is measurable:** Sweller, Ayres & Kalyuga (2011) established that processing technical content in a non-dominant language imposes extraneous cognitive load that competes with germane load (actual learning). For Arab-Israeli students taking Hebrew Bagrut exams, every math term carries dual-language processing cost.

**The evidence on code-switching:** Clarkson (2007) found that bilingual students who could flexibly code-switch between languages during problem-solving performed significantly better than those constrained to one language. Bialystok & Viswanathan (2009) confirmed bilingual executive control advantages exist but do not compensate for the cognitive load penalty when reasoning must occur in L2.

**The gap:** Friedmann & Haddad-Hanna (2012) demonstrated that Arabic's morphological complexity creates additional processing demands in RTL contexts. No study has directly measured cognitive load in Arabic-Hebrew bilingual math instruction — this is a genuine research gap that Cena could help fill.

#### Design Decision: Two-Phase Terminology Bridge (Confirmed)

The two-phase lesson structure proposed in Section 1b is now grounded in this evidence:

```text
Phase 1: Concept in L1 (dominant language)
  Purpose: Minimize extraneous load, maximize germane load
  Evidence: Clarkson (2007) — L1 instruction improves comprehension
  Duration: ~70% of lesson time

Phase 2: Bagrut terminology mapping (L1 → Hebrew exam language)
  Purpose: Build the Hebrew encoding needed for Bagrut exam
  Evidence: Code-switching advantage (Clarkson, 2007; Bialystok, 2009)
  Duration: ~30% of lesson time
  Format: Side-by-side term cards, exam-phrasing recognition exercises
```

**Implementation in LessonPoolActor:**

```csharp
MicroLesson ResolveBilingualLesson(string conceptId, Methodology method, CulturalContext ctx)
{
    var baselesson = _pool.Get(conceptId, method, ctx.DominantLanguage);

    if (ctx.Cohort == CulturalCohort.ArabicDominant && ctx.ExamLanguage == "he")
    {
        // Append Phase 2: terminology bridge
        return baselesson.WithTerminologyBridge(
            fromLang: "ar",
            toLang: "he",
            terms: _conceptGraph.GetBagrutTerms(conceptId, "he"),
            examPhrasing: _corpusAnalysis.GetBagrutPhrasing(conceptId)
        );
    }
    return baselesson;
}
```

**Measurement opportunity:** Cena will be one of the first platforms to generate data on bilingual RTL math instruction at scale. Tracking mastery velocity for Arabic-dominant students with/without Phase 2 creates a publishable research contribution.

---

## 10. What NOT to Build

| Feature | Why Not |
|---------|---------|
| Long-form video lectures (>5 min) | Violates cognitive load research. Completion rates collapse. |
| Gamified animations with rewards/badges | Extrinsic motivation undermines intrinsic learning (Deci & Ryan, 2000). Cena's mastery model IS the progress system. |
| AI-generated video of a "teacher avatar" | Uncanny valley, high cost, low trust. Voice + diagram is more effective (Mayer, 2021). |
| Student-to-student social learning features | Out of scope for micro-lessons. Separate initiative if ever. |
| Mandatory lesson viewing before questions | Violates the adaptive principle. Some students don't need the lesson. The system should learn who does. |
| One-size-fits-all lesson per concept | The entire point is methodology-matched delivery. A single lesson ignores the MCM routing that makes Cena different. |

---

## 11. Build Sequence

| Phase | What | Depends On | Effort | Impact |
|-------|------|-----------|--------|--------|
| **Phase 1** | `LessonPoolActor` + data model + NATS events | None (mirrors QuestionPoolActor) | 1 week | Enables all subsequent phases |
| **Phase 2** | WorkedExample micro-lessons (text + narrated diagrams) for top 50 concepts | Phase 1 + existing diagram pipeline | 2 weeks | Covers 90% of concepts' primary methodology |
| **Phase 3** | Socratic interactive micro-lessons (branching dialogue) | Phase 1 | 2 weeks | Addresses conceptual error pattern |
| **Phase 4** | Trigger integration (stagnation, focus, concept unlock) | Phase 1 + existing StagnationDetector | 1 week | Lessons appear at the right time |
| **Phase 5** | Video rendering via Remotion (batch, overnight) | Phase 2 scripts | 2 weeks | Multimedia upgrade for top content |
| **Phase 6** | Remaining methodologies (Analogy, Feynman, Drill) | Phase 1 | 3 weeks | Full methodology coverage |
| **Phase 7** | Analytics: completion rates, mastery lift, A/B testing | Phase 4 | 2 weeks | Measures actual learning impact |
| **Phase 8** | Arabic language micro-lessons | Phase 2-3 | 2 weeks | Equity imperative |

**Critical path: Phases 1-4 (~6 weeks) deliver adaptive micro-lessons triggered by the right signals.**

---

## 12. Success Metrics

| Metric | Baseline (Assessment-Only) | Target (With Micro-Lessons) | Measurement |
|--------|---------------------------|----------------------------|-------------|
| Mastery velocity (concepts/week) | Unknown (establish baseline) | +20-30% | BKT progression rate |
| Stagnation recovery rate | Unknown | +40% (students exit stagnation faster) | StagnationDetector resolution events |
| Session engagement (minutes) | Unknown | +15-25% | Session duration + activity events |
| Concept retention at 7 days | Unknown | +20% | HLR recall probability comparison |
| Student satisfaction | Unknown | Establish via in-app survey | Net Promoter Score |
| Focus degradation frequency | Unknown | -15% (lessons as cognitive rest) | FocusState transitions per session |

---

## 13. Open Questions

1. **Lesson-question ratio**: RESOLVED. Research converges on a clear recommendation:

   - **Corbett & Anderson (1995):** In the ACT-R Lisp tutor, mastery learning alternated instruction with 3-5 practice problems per concept step. Instruction was ~20% of session time, practice ~80%.
   - **Rohrer, Dedrick & Stershic (2015):** In K-12 math, interleaved practice (which creates natural instruction-practice alternation) with ~25% instruction and ~75% practice produced 72% correct on delayed tests vs. 38% for practice-only.
   - **Pardos & Heffernan (2010):** ASSISTments math tutoring uses a ratio of ~1 instructional scaffold per 3-4 practice items, triggered by errors. Students who received scaffolding showed 2-5% AUC improvement in mastery prediction.

   **Decision: 20-25% instruction, 75-80% practice.** For a 15-minute session (~8-10 items):
   - **Default**: 1-2 micro-lessons + 6-8 questions
   - **Stagnation override**: Up to 3 micro-lessons (methodology switches need explanation)
   - **Focus declining**: Swap 1-2 questions for passive video (maintains engagement without cognitive demand)
   - **New concept**: Always start with 1 intro lesson before first question

   Implementation in `LearningSessionActor`:

   ```csharp
   // Session lesson budget
   const int DefaultLessonBudget = 2;
   const int StagnationLessonBudget = 3;
   const float MaxLessonTimeRatio = 0.25f; // Never exceed 25% of session time on lessons

   bool ShouldInsertLesson(SessionState state) =>
       state.LessonsServed < GetLessonBudget(state) &&
       state.LessonTimeSeconds < state.SessionDurationSeconds * MaxLessonTimeRatio &&
       HasTrigger(state); // New concept, error, stagnation, or focus decline
   ```

2. **Student opt-out**: Can students skip lessons? Proposed: yes, always — but track skip patterns as a signal. Repeated skipping + continued errors → surface to teacher.

3. **Offline-first**: RESOLVED. Strategy: pre-cache deterministic content, gracefully degrade LLM-dependent content.

   **Modality classification by offline capability:**

   | Modality | Offline? | Pre-cache Strategy | Fallback When Offline |
   | -------- | -------- | ------------------ | --------------------- |
   | Video (Remotion-rendered) | Yes | Download on WiFi, keep top 20 concepts per subject | Already cached |
   | Narrated diagrams (SVG + TTS audio) | Yes | Small files (~200KB each), aggressive pre-cache | Already cached |
   | Guided text walkthroughs | Yes | Included in lesson JSON payload (~5KB) | Already cached |
   | Interactive checkpoints (deterministic) | Yes | Part of lesson JSON — FillTheGap, MCQ, DragAndOrder | Already cached |
   | Socratic branching dialogue (deterministic) | Yes | Pre-authored branches in lesson JSON | Already cached |
   | LLM-generated personalized explanation (L3) | No | Cannot pre-cache (requires student context) | Fall back to L2 cache (ErrorType-based) or L1 (static) |
   | ExplainBack (Feynman voice recording) | Partial | Recording works offline; evaluation needs LLM | Record locally, evaluate on reconnect |
   | SliderExplore (interactive simulation) | Yes | Canvas/SVG rendering is client-side | Already cached |

   **Pre-cache budget:** ~50MB per subject covers top 100 concepts × 3 methodology variants × video + audio + JSON. This fits comfortably in mobile storage and can be downloaded on WiFi during off-peak hours.

   **Implementation:**

   ```csharp
   // LessonCacheService — runs on client (mobile/PWA)
   async Task PreCacheLessons(string subjectId, string studentId)
   {
       // Get student's learning frontier + at-risk concepts
       var frontier = await _api.GetLearningFrontier(studentId, subjectId);
       var atRisk = await _api.GetRecallAtRiskConcepts(studentId, subjectId);

       // Priority: frontier concepts first, then at-risk, then next-likely
       var conceptIds = frontier.Concat(atRisk).Distinct().Take(100);

       foreach (var conceptId in conceptIds)
       {
           var methodology = await _api.GetActiveMethodology(studentId, conceptId);
           var lesson = await _api.GetMicroLesson(conceptId, methodology);

           // Cache lesson JSON + download media assets
           await _localDb.CacheLesson(lesson);
           if (lesson.VideoAssetUrl != null)
               await _mediaCache.DownloadIfMissing(lesson.VideoAssetUrl);
           if (lesson.AudioAssetUrl != null)
               await _mediaCache.DownloadIfMissing(lesson.AudioAssetUrl);
       }
   }
   ```

   **Offline session behavior:** When offline, `LearningSessionActor` serves only pre-cached lessons. The trigger logic remains the same, but L3 (LLM-generated) explanations fall back to L2/L1. Session events are queued locally and synced on reconnect (existing `offline-sync-protocol.md` pattern).

4. **Teacher-authored lessons**: Should teachers be able to create micro-lessons? Or only review AI-generated ones? Teacher creation adds a significant UX surface.

5. **Lesson quality gate**: Reuse the existing 8-factor quality gate, or create a lesson-specific gate? Lessons have different quality dimensions (pacing, visual clarity, checkpoint quality) than questions.

6. **Effect measurement**: RESOLVED. Use the existing `FocusExperimentConfig` A/B framework with a micro-lessons-specific experiment:

   **Experiment design** (randomized controlled trial within Cena):

   ```csharp
   // New experiment in FocusExperimentConfig
   new ExperimentConfig
   {
       Name = "MicroLessons_V1",
       Arms = new[]
       {
           new Arm("control", "Assessment-only (current behavior)"),
           new Arm("treatment_passive", "Micro-lessons: video/text only (no checkpoints)"),
           new Arm("treatment_interactive", "Micro-lessons: full interactive with checkpoints"),
       },
       Assignment = AssignmentMethod.StudentIdHash,  // Deterministic, stable per student
       MinSamplePerArm = 100,                         // Statistical power for d=0.3 at α=0.05, β=0.80
       PrimaryMetric = "mastery_velocity_concepts_per_week",
       SecondaryMetrics = new[]
       {
           "stagnation_recovery_rate",
           "session_engagement_minutes",
           "concept_retention_7day",
           "focus_degradation_frequency"
       },
       Duration = TimeSpan.FromDays(42),  // 6 weeks — enough for mastery velocity to stabilize
       EarlyStoppingRule = "If treatment arm shows p<0.01 harm on primary metric after 2 weeks, halt"
   }
   ```

   **Why 3 arms, not 2:** The passive vs. interactive comparison isolates the effect of checkpoints (VanLehn, 2011: step-level tutoring +0.45 SD over problem-level). This answers whether the interactivity matters or if just showing content is enough.

   **Power analysis:** For detecting d=0.3 (conservative lower bound from Ma et al., 2014) at α=0.05 with 80% power, need N≈175 per arm. At 100 minimum with 6-week duration, the secondary metrics (which may show larger effects) will also be detectable.

   **Isolation strategy:** Student-level random assignment ensures the control and treatment groups face the same QuestionSelector algorithm, same BKT parameters, same StagnationDetector thresholds. The ONLY difference is whether micro-lessons are served at trigger points. This cleanly isolates the micro-lesson effect from all other adaptive components.

   **Ethics:** The control group is not harmed — they receive the current system, which is already adaptive. The treatment group receives additional instruction. No student receives less than baseline.

7. **Cost ceiling**: At 10,000 lessons per subject × TTS + rendering costs, what's the total content budget? How does this compare to hiring a curriculum designer to create them manually?

---

## 14. References

- Bjork, R.A. (2011). On the symbiosis of remembering, forgetting, and learning. *Adaptive Memory*.
- Bunce, D.M., Flens, E.A., & Neiles, K.Y. (2010). How long can students pay attention in class? *Journal of Chemical Education*, 87(12), 1438-1443.
- Chi, M.T.H., & Wylie, R. (2014). The ICAP framework: Linking cognitive engagement to active learning outcomes. *Educational Psychologist*, 49(4), 219-243.
- Clark, R.C., & Mayer, R.E. (2016). *E-Learning and the Science of Instruction* (4th ed.). Wiley.
- Corbett, A.T., & Anderson, J.R. (1994). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction*, 4(4), 253-278.
- Deci, E.L., & Ryan, R.M. (2000). The "what" and "why" of goal pursuits: Human needs and self-determination of behavior. *Psychological Inquiry*, 11(4), 227-268.
- Guo, P.J., Kim, J., & Rubin, R. (2014). How video production decisions affect student engagement: An empirical study of MOOC videos. *ACM Conference on Learning @ Scale*.
- Hattie, J. (2023). *Visible Learning: The Sequel*. Routledge. (Updated meta-analysis.)
- Mayer, R.E. (2021). *Multimedia Learning* (3rd ed.). Cambridge University Press.
- Pashler, H., et al. (2008). Learning styles: Concepts and evidence. *Psychological Science in the Public Interest*, 9(3), 105-119.
- Rohrer, D., & Taylor, K. (2007). The shuffling of mathematics problems improves learning. *Instructional Science*, 35(6), 481-498.
- Settles, B., & Meeder, B. (2016). A trainable spaced repetition model for language learning. *ACL*.
- Sims, S., et al. (2020). Interactive simulations in science education: A meta-analysis. *Journal of Research in Science Teaching*.
- Sweller, J. (2011). *Cognitive Load Theory*. Springer.
- VanLehn, K. (2011). The relative effectiveness of human tutoring, intelligent tutoring systems, and other tutoring systems. *Educational Psychologist*, 46(4), 197-221.
