# Student ↔ System AI Interaction: Cache / RAG / Strategy Discussion

**Date**: 2026-03-28
**Status**: Draft — Open for Discussion
**Last Validated Against Codebase**: 2026-03-28 (10 autoresearch iterations)

---

## Context

Students interact with Cena through a **SignalR WebSocket** session that is entirely pre-computed and actor-cached today. There is **no direct student-to-LLM interaction**. The only production LLM integration is `GeminiOcrClient` for OCR. The `AiGenerationService` has 4 provider stubs (Anthropic, OpenAI, Google, Azure) but **all return mock data** — no real API calls. A routing config (`contracts/llm/routing-config.yaml`) maps Claude Sonnet 4.6 as primary tutoring model and Kimi K2.5 for structured tasks (diagram generation, error classification), but no Kimi/Moonshot client exists yet. The `AnswerEvaluated` and `HintDelivered` SignalR responses have `explanation` and `hintText` fields, but these are **placeholder strings** — no LLM service populates them yet.

This document explores what AI/cache/RAG methods are needed in the student loop, how each connects to learning outcomes, and in what order to build them.

---

## Current Architecture (What Exists)

### Data Layer

| Layer | Component | What It Holds | Latency |
|-------|-----------|---------------|---------|
| Actor memory | `StudentActor` (virtual grain per student, ~500KB) | Mastery overlay, BKT, HLR, streaks, hierarchical methodology, PSI, quality quadrant, Bloom levels | <1ms read |
| Actor memory | `LearningSessionActor` (child, session-scoped) | Fatigue score, rolling accuracy/response times, hint tracking, question count | <1ms read |
| Utility | `IQuestionPool` (per subject, in-memory) | All published questions, indexed by concept, sorted by Bloom + difficulty | <10ms selection |
| Singleton | `CurriculumGraphActor` → `IConceptGraphCache` | Prerequisite graph (from Neo4j), topic hierarchy, intrinsic load, Bagrut weights | O(1) lookup |
| Singleton | `McmGraphActor` | MCM: (ErrorType, ConceptCategory) → [(Methodology, confidence)] | O(1) lookup |
| Singleton | `LlmGatewayActor` → `LlmCircuitBreakerActor` | Per-model circuit breakers (Kimi: 5/60s, Sonnet: 3/90s, Opus: 2/120s, Redis: 5/30s) | <1ms |
| Redis | Messaging subsystem | Thread streams (`cena:thread:{id}`), unread counters, webhook dedup (5min TTL), offline sync dedup (72h TTL) | ~1ms |
| Marten/Postgres | Event store + snapshots | Source of truth — 50+ event types, snapshots every 100 events | ~5-20ms |
| NATS + JetStream | Event bus (15+ subjects, 8 durable streams) | Commands: `cena.session.*`, `cena.mastery.*`; Events: `cena.events.*`; Messaging: `cena.messaging.*`. JetStream streams: LEARNER_EVENTS, PEDAGOGY_EVENTS, ENGAGEMENT_EVENTS, OUTREACH_EVENTS, CURRICULUM_EVENTS, ANALYTICS_EVENTS, SCHOOL_EVENTS, DEAD_LETTER (90-day retention). `NatsOutboxPublisher` uses core NATS pub/sub; JetStream consumers provide durable replay. | Async |

### Student Communication Protocol (SignalR)

The student learning session uses **bidirectional WebSocket** (not REST) via a SignalR hub. Contract: `contracts/frontend/signalr-messages.ts`.

**Client → Server Commands:**

| Command | Key Payload Fields |
|---------|-------------------|
| `StartSession` | subjectId, conceptId (optional), device context |
| `SubmitAnswer` | questionId, answer, responseTimeMs, confidence (1-5), behavioralSignals: { backspaceCount, answerChangeCount } |
| `RequestHint` | questionId, hintLevel (1-3) |
| `SkipQuestion` | questionId |
| `AddAnnotation` | conceptId, text, kind: 'note' / 'question' / 'confusion' / 'insight' |
| `SwitchApproach` | methodology |
| `EndSession` | reason: 'completed' / 'tired' / 'out-of-time' / 'app-background' |

**Server → Client Events:**

| Event | Key Payload Fields |
|-------|-------------------|
| `QuestionPresented` | questionText (LaTeX), format (MCQ/free-text/numeric/proof), difficulty, methodology |
| `AnswerEvaluated` | correct, score (0-1), **explanation** (Markdown+LaTeX — PLACEHOLDER), errorType, nextAction, updatedMastery, xpEarned |
| `HintDelivered` | hintLevel, **hintText** (Markdown+LaTeX — PLACEHOLDER), hasMoreHints |
| `MasteryUpdated` | masteryLevel, attemptsToMastery, unlockedConcepts |
| `CognitiveLoadWarning` | recommendation: 'take-break' / 'reduce-difficulty' / 'switch-to-review' / 'end-session' |
| `StagnationDetected` | signal type, recommendation, human-readable message |
| `MethodologySwitched` | previous, new, reason, trigger |

The `explanation` and `hintText` fields are where Tier 1 and 2 outputs will be delivered. The SignalR contract is ready; the generation service is not.

### What the System Already Knows Per Student

The `ConceptMasteryState` record (per concept) holds:

| Signal | Type | Source |
|--------|------|--------|
| `MasteryProbability` | float | BKT: P_L0=0.10, P_T=0.20, P_S=0.05, P_G=0.25 |
| `HalfLifeHours` | float | HLR: 6-feature regression (attempts, correct, difficulty, depth, Bloom, days). Clamped [1h, 8760h] |
| `RecallProbability(now)` | computed | `2^(-elapsed_hours / halfLife)` |
| `BloomLevel` | int 0-6 | Revised Bloom's taxonomy |
| `RecentErrors[]` | ErrorType[10] | Circular buffer: Procedural, Conceptual, Motivational, Careless, Systematic, Transfer |
| `QualityQuadrant` | enum | Mastered (fast+correct), Effortful (slow+correct), Careless (fast+wrong), Struggling (slow+wrong) |
| `AttemptCount`, `CorrectCount` | int | Lifetime counters per concept |
| `CurrentStreak` | int | Consecutive correct per concept |
| `Stability`, `Difficulty` | float | FSRS-compatible spaced repetition signals |
| `MethodHistory[]` | MethodAttempt[] | Which methodologies were tried, session count, outcome |
| `SelfConfidence` | float | Student self-report (1-5 via SubmitAnswer confidence field) |

**Effective mastery** = `min(BKT_probability, recall_probability) * prerequisite_support` where PSI = average mastery of direct prerequisites.

### Affect & Attention System (Feeds Into Hint/Explanation Timing)

The system has a sophisticated real-time affect detection pipeline that determines **when** to intervene — directly relevant to Tier 1-2 delivery timing:

**CognitiveLoadService** — 3-factor weighted fatigue model:
- Accuracy drop from baseline (weight 0.4) + RT increase (0.3) + session time fraction (0.3)
- Thresholds: Low (<0.3), Moderate (0.3-0.6), High (0.6-0.8), Critical (0.8+)
- Outputs `DifficultyAdjustment`: Ease / Maintain / Increase

**FocusDegradationService** (615 lines) — 8-signal composite focus score:
- 4 behavioral signals: Attention (RT variance), Engagement (hint/annotation rate), Accuracy Trend (linear regression slope), Vigilance Decrement (Warm 1984 logarithmic decay)
- 4 mobile sensor signals (when available): Motion Stability, App Focus, Touch Pattern, Environment
- 7 focus levels: Flow (0.8+) → Engaged → Drifting → Fatigued → Disengaged → DisengagedBored → DisengagedExhausted
- Outputs: `PredictRemainingProductiveQuestions()`, `RecommendBreak()` (proactive microbreak 60-90s vs reactive recovery 5-30min)
- Culturally-adjusted resilience weights (FOC-012): Arabic-dominant contexts weight recovery 30% (collectivist)

**DisengagementClassifier** — distinguishes boredom from fatigue (opposite interventions!):
- `Bored_TooEasy`: Fast+correct, low engagement → **increase difficulty, don't offer hints**
- `Bored_NoValue`: Can't see task value → **change topic**
- `Fatigued_Cognitive`: Slow+inaccurate, long session → **take break, offer simpler scaffolding**
- `Fatigued_Motor`: Physical fatigue → **rest**

**ConfusionDetector + ConfusionResolutionTracker** — adaptive patience before intervening:
- 4 confusion signals: unexpected error on mastered concept, elevated RT + correct answer, answer changed, hint cancelled
- Adaptive patience window: 3 questions (low resolvers, rate <0.3) → 5 (default) → 7 (high resolvers, rate >0.7)
- Key insight (D'Mello & Graesser 2012): **confusion that resolves leads to deeper learning — don't interrupt too early**
- `ConfusionState`: NotConfused → Confused → ConfusionResolving (DON'T intervene) → ConfusionStuck (scaffold)

**Why this matters for AI interaction design**: The hint/explanation system must integrate with ConfusionDetector to avoid interrupting productive struggle. A hint delivered during ConfusionResolving state could actually harm learning.

### Existing Infrastructure That Feeds Into This Work

**ScaffoldingService** (`ScaffoldingService.cs`) — already maps effective mastery + PSI to instructional support level:

| Level | Condition | Hints | Reveal Answer | Style |
|-------|-----------|-------|---------------|-------|
| Full | mastery < 0.20 AND PSI < 0.80 | 3 max | Yes | Worked example |
| Partial | mastery < 0.40 | 2 max | Yes | Faded example |
| HintsOnly | mastery < 0.70 | 1 max | No | Hints only |
| None | mastery >= 0.70 | 0 | No | Independent |

This metadata is **ready for LLM prompt construction** but no LLM service consumes it yet.

**Hint Event Infrastructure** — `HintRequested_V1(StudentId, SessionId, ConceptId, QuestionId, HintLevel: 1|2|3)` is already emitted by `LearningSessionActor` via `RequestHintMessage`. The `HintDelivered` SignalR event is sent back with `hintText` (currently placeholder). The event plumbing and 3-level hint model exist. What's missing is **hint content generation** and **BKT credit adjustment**.

**Annotation System** — `AddAnnotation` SignalR command lets students mark confusion/questions/insights on concepts. Persisted as `AnnotationAdded_V1` domain events with sentiment analysis routing. This is a natural **entry point for conversational tutoring** — a "confusion" annotation is the student saying "I need help."

**Methodology System** — 9 methodologies with hierarchical 5-layer resolution:

| Methodology | Primary Use |
|-------------|-------------|
| Socratic | Guided questioning |
| Feynman | Teach-back for conceptual clarity |
| WorkedExample | Procedural demonstration |
| SpacedRepetition | Spacing + retrieval |
| BloomsProgression | Cognitive level scaffolding |
| ProjectBased | Applied, motivation-driven |
| Analogy | Comparison to known concepts |
| RetrievalPractice | Testing effect |
| DrillAndPractice | Repetitive execution |

Resolution cascade: Concept teacher override > Concept data-driven (N>=30) > Topic (N>=30) > Subject (N>=50) > Bloom's default. Confidence gated by Wilson score lower bound.

**MCM Graph** — When stagnation is detected (3+ repeated ErrorType in RecentErrors), `MethodologySwitchService` queries the MCM graph: `(ErrorType, ConceptCategory) → [(Methodology, confidence)]`. Cooldown: 5 sessions / 7 days between switches. Exhaustion escalation: connect tutor > suggest skip concept.

**A/B Testing Framework** — `FocusExperimentConfig` with 6 predefined experiments (microbreaks, boredom-fatigue split, confusion patience, peak time adaptation, solution diversity, sensor-enhanced). `FocusExperimentCollector` captures per-student per-session metrics with hash-based deterministic arm assignment. Export to CSV/Parquet for offline analysis.

**Quality Gate (LLM opportunity)** — `QualityGateService` evaluates questions across 8 dimensions. Three are currently **stubbed at default scores** awaiting LLM integration: FactualAccuracy (default 80), LanguageQuality (default 80), PedagogicalQuality (default 75). The `ILlmClient` abstraction needed for Tier 1 also unblocks these quality gate dimensions.

**The gap is not data — it's using that data to personalize the response, not just the question selection.**

### Research Foundation: BKT for Instruction, Not Just Assessment

The literature strongly supports using mastery models to drive instructional content delivery, not just question selection:

- **Corbett & Anderson (1995):** The original BKT paper demonstrated that mastery-based sequencing in the ACT-R Lisp tutor reduced time to criterion by ~30%. Crucially, the tutor alternated *instruction* with practice — BKT determined when to teach, not just when to test. Cena currently uses BKT only for assessment sequencing.
- **Yudelson, Koedinger & Gordon (2013):** Individualizing BKT priors per student significantly improved both prediction accuracy and content sequencing quality in Carnegie Learning's math platform. The key insight: BKT parameters (P(L₀), P(T), P(S), P(G)) should condition *what type of instruction* to deliver, not just *which question* to ask next. A student with high P(G) (guessing) needs conceptual instruction; a student with high P(S) (slipping) needs procedural drilling.
- **Pardos & Heffernan (2010):** ASSISTments' individualized BKT improved AUC by 2-5% and enabled better-targeted scaffolding delivery. The scaffold decisions were driven by the same mastery estimates that drove problem selection — proving the model serves both purposes.
- **Doignon & Falmagne (2012):** Knowledge Space Theory (KST, already implemented in Cena's `KnowledgeState`) provides the theoretical foundation for using mastery state estimation to determine optimal *learning paths*, not just assessment sequences. ALEKS (10M+ students) uses this for instruction selection.

**Implication for Cena:** The BKT parameters already computed inline (~100ns per update in `LearningSessionActor`) should be consumed by the explanation/hint generation system:

```
BKT Parameter → Instructional Decision
─────────────────────────────────────────────────────
High P(G), low P(L)  → Student guessing → Needs conceptual instruction (Socratic/Feynman)
High P(S), high P(L) → Student knows but slips → Needs procedural drill (WorkedExample/Drill)
Low P(T)             → Learning rate slow → Switch methodology (MCM routing already does this)
P(L) near threshold  → Almost mastered → Light reinforcement (SpacedRepetition micro-lesson)
```

This mapping connects the existing BKT engine to the explanation/micro-lesson system. It is not a new model — it is a new *consumer* of the existing model.

---

## Tier 1 — Adaptive Explanations (Cache-First + LLM Fallback)

### The Problem

When a student answers incorrectly, the `AnswerEvaluated` SignalR response has an `explanation` field that is currently a **placeholder string**. No static explanations are stored per question — the `QuestionState` aggregate has no explanation field. The `AiGeneratedQuestion` DTO includes an `Explanation` from the AI generation step, but **it is discarded and never persisted** to `QuestionState` or any Marten event.

Additionally, the `DistractorRationale` field exists per question option, but this is metadata for the question author, not a student-facing explanation.

The current explanation doesn't account for:

- What the student already understands (mastery overlay + PSI)
- What specific misconception they have (ErrorType classification already exists: 6 types)
- What methodology is active (Socratic should ask back, not tell — 9 methodologies resolved hierarchically)
- What Bloom level they're at (0-6 tracked per concept)
- What scaffolding level applies (ScaffoldingService already computes Full/Partial/HintsOnly/None)
- What confusion state the student is in (ConfusionDetector tracks ConfusionResolving vs ConfusionStuck)

### Proposed Solution

A **3-layer explanation cache** with personalization:

```
L1: Persisted explanation per question — NEW (currently generated by AI but discarded)
L2: Cached explanation per (question × ErrorType cluster) — NEW
L3: On-demand LLM generation with full student context — NEW
```

#### L1 — Persist AI-Generated Explanations

The `AiGeneratedQuestion` DTO already returns an `Explanation` field from the AI generation service. Fix:
1. Add `Explanation` field to `QuestionState` aggregate
2. Add `ExplanationUpdated_V1` Marten event
3. Persist explanation from `AiGeneratedQuestion` during `QuestionAiGenerated_V1` event handling
4. Include explanation in `PublishedQuestion` serving model
5. Backfill existing questions via batch re-generation

This is the **cheapest win** — the data is already being generated but thrown away.

#### L2 — ErrorType-Based Explanation Cache

The system already classifies errors into 6 types. For a given question, most wrong answers cluster into 2-3 ErrorType categories. For example, a quadratic equation question might produce:
- **Procedural** cluster — right approach, execution mistake (sign error, arithmetic)
- **Conceptual** cluster — fundamental misunderstanding (mixed up with linear formula)
- **Careless** cluster — fast + wrong (quality quadrant: Careless)

**Generation**: Batch-generate at question publish time or on first occurrence per ErrorType.
**Storage**: Redis `explain:{questionId}:{errorType}` with 30-day TTL.
**Hit rate**: Expected 80-90% after warm-up — most students make the same category of mistake.

#### L3 — Personalized Generation

When L2 misses (novel error pattern or Transfer/Systematic errors), generate on-demand:

```
Input (all available from StudentActor + LearningSessionActor):
  - Question stem + correct answer + student's answer
  - ConceptMasteryState: BKT probability, Bloom level, recent errors, quality quadrant
  - PSI for this concept (prerequisite readiness)
  - ScaffoldingService level (Full/Partial/HintsOnly/None) — determines explanation depth
  - Active methodology from MethodologyResolver — determines tone/style
  - MethodHistory for this concept — avoids repeating failed approaches
  - ConfusionState from ConfusionDetector — skip if ConfusionResolving (let student work through it)
  - FocusLevel from FocusDegradationService — adjust verbosity
  - DisengagementType — if Bored_TooEasy, don't over-explain
  - ResponseTimeBaseline — calibrates expected engagement time
  - Behavioral signals: backspaceCount, answerChangeCount (indicate uncertainty)

Output:
  - Explanation calibrated to THIS student at THIS scaffolding level
  - Methodology-compliant tone (Socratic asks, Feynman teaches back, etc.)
  - Cached back to L2 if ErrorType is classifiable
```

**Model**: Haiku-class for cost (this is high-volume). Sonnet fallback for complex STEM.
**Rate limit**: Max 3 LLM explanation calls per student per minute.
**Circuit breaker**: Add student-facing model config to existing `LlmCircuitBreakerActor` (already supports per-model thresholds: Kimi 5/60s, Sonnet 3/90s, Opus 2/120s).
**LLM abstraction needed**: Current codebase only has hardcoded Gemini endpoints (`GeminiOcrClient`). Need `ILlmClient` abstraction with Anthropic/OpenAI adapters before L3 can work.

**Confusion-aware delivery**: Do NOT deliver L3 explanations when `ConfusionState == ConfusionResolving`. The ConfusionResolutionTracker's adaptive patience window (3-7 questions) should gate explanation delivery. Only deliver when `ConfusionStuck` or no confusion detected.

### Learning Impact

ScaffoldingService levels map directly to explanation depth:
- **Full** (mastery < 0.20): "Here's the complete worked example: step 1... step 2... step 3..."
- **Partial** (mastery < 0.40): "You got step 1 right. For step 2, consider..."
- **HintsOnly** (mastery < 0.70): "Think about how [prerequisite] applies here"
- **None** (mastery >= 0.70): No explanation needed (independent)

Methodology constrains tone:
- Socratic: "What would happen if you applied [prerequisite concept] here?"
- Feynman: "Try explaining this concept in simple terms — where does your reasoning break?"
- WorkedExample: "Watch this approach: [step-by-step], then try a similar problem"
- Analogy: "This is like [known concept] — what's the same? What's different?"
- RetrievalPractice: "Before I show you, try to recall — what do you remember about [concept]?"

**Effect size estimate**: ~0.3-0.5 sigma improvement (personalized feedback is one of the strongest known interventions in education research).

### Priority: HIGH

Single biggest learning multiplier missing from the system. ScaffoldingService metadata is already computed; SignalR response fields exist; the gap is an LLM service that populates them.

---

## Tier 2 — Hint Content Generation (Infrastructure Exists, Content Missing)

### The Problem

The hint **infrastructure** exists (`HintRequested_V1` events, `RequestHintMessage` with 3 levels, `ScaffoldingService` determining max hints per level, `HintDelivered` SignalR event with `hintText` placeholder). What's missing is:
1. **Hint content** — no service generates the actual text for each hint level
2. **BKT credit adjustment** — hint usage doesn't yet reduce mastery credit (though `BusConceptAttempt.HintCountUsed` is already transmitted from client)
3. **Concept-graph-aware nudges** — Level 1 hints should point to prerequisites via `IConceptGraphCache`
4. **Confusion-state gating** — hints should respect `ConfusionResolutionTracker` patience window

### What Already Exists

- `LearningSessionActor` handles `RequestHintMessage(conceptId, questionId, hintLevel)` and emits `HintRequested_V1`
- `HintDelivered` SignalR event sent back to client with `hintText` (placeholder) and `hasMoreHints`
- `ScaffoldingService` determines max hints: Full=3, Partial=2, HintsOnly=1, None=0
- `IConceptGraphCache.GetPrerequisites(conceptId)` returns prerequisite edges with strength weights
- `QuestionSelector` already uses focus-state adaptation (Strong/Stable/Declining/Degrading/Critical) — can extend with hint patterns
- `BusConceptAttempt` already transmits `HintCountUsed`, `BackspaceCount`, `AnswerChangeCount` from client
- `ConfusionDetector` + `ConfusionResolutionTracker` track whether student is working through confusion

### Proposed Additions

**Hint content generation** — 3-step ladder per question, derived from existing data:

| Step | Hint Type | Source | AI Needed? |
|------|-----------|--------|------------|
| 1 | **Nudge** — "Think about [prerequisite concept]" | `IConceptGraphCache.GetPrerequisites()`, sorted by `Strength` descending | No |
| 2 | **Scaffold** — eliminate one distractor or show partial approach | Aggregate error stats from `ConceptMasteryState.RecentErrors[]` + question metadata | No |
| 3 | **Reveal** — full worked solution | L1 persisted explanation (Tier 1) or L2 cache | No (uses Tier 1 cache) |

**BKT credit weighting** — modify `BktTracer.Update()` call in `StudentActor`:
- No hints = full credit (existing behavior)
- 1 hint = 0.7x P_T adjustment
- 2 hints = 0.4x P_T adjustment
- 3 hints = 0.1x P_T adjustment

**Confusion-state gating** — when `ConfusionState == ConfusionResolving`, delay hint delivery and show: "Take your time — you're working through this." Only deliver hints when `ConfusionStuck` or after patience window expires.

**Boredom-aware suppression** — when `DisengagementType == Bored_TooEasy`, suppress hints entirely and increase difficulty instead. Offering hints to a bored student signals the system thinks they're struggling, which is counterproductive.

**QuestionSelector integration** — track hint patterns in session state to influence next-item difficulty.

### Learning Impact

Hints keep students in the Zone of Proximal Development (ZPD) that `QuestionSelector` already targets with its 5-step pipeline (concept selection → Bloom's range → difficulty range [mastery-0.15, mastery+0.25] → filter → score). Without hints, a hard question is binary (right/wrong). With hints, it becomes a **learning event** even when the student ultimately fails.

The `FocusExperimentConfig` already has a "Confusion Patience" experiment (control: intervene at 3 wrong answers; treatment: wait for 5-question resolution window). Hint progression is the natural intervention mechanism for this experiment.

Research: Hint-based scaffolding shows ~0.2-0.4 sigma effect size. Combined with adaptive question selection (already built), this compounds.

### Priority: HIGH

Low effort (~2-3 days — infrastructure exists, just need content generation + BKT weight adjustment), zero AI cost, directly extends existing ZPD and scaffolding logic.

---

## Tier 2b — Interactive Simulations for Conceptual Understanding

### The Research Case

Interactive simulations represent a distinct instructional modality with strong evidence — distinct from both passive video (Tier 1) and conversational tutoring (Tier 3):

- **Freeman et al. (2014, PNAS):** The landmark meta-analysis of 225 STEM studies found active learning (including simulations) increased exam scores by +0.47 SD and reduced failure rates by 1.5×. This is the single largest effect in the STEM education literature.
- **Wieman, Adams & Perkins (2008):** PhET interactive simulations at University of Colorado produced learning gains significantly above traditional instruction in physics and math. The key: students explore cause-effect relationships by manipulating variables directly.
- **Deslauriers, Schelew & Wieman (2011):** Interactive engagement (simulations + peer discussion) in a large physics class produced learning gains nearly 2× those of traditional lecture. The effect was driven by the *interactive* element, not the technology.
- **Rutten, van Joolingen & van der Veen (2012):** Meta-review of 51 studies found simulations enhanced learning vs. traditional instruction in the majority of cases. Critical moderator: **guided inquiry scaffolding**. Unguided simulations sometimes performed worse. *This validates Cena's methodology-driven approach — simulations must be scaffolded by the active methodology (Socratic guidance, WorkedExample walkthrough, etc.).*

### Where Simulations Fit in Cena

Simulations are most valuable for concepts where the relationship between variables is non-obvious:

| Math Concept Category | Simulation Type | Example |
| ---------------------- | ---------------- | --------- |
| Functions & graphs | Slider-controlled graph exploration | "Move `a` to see how y=ax² changes" |
| Derivatives | Tangent line animation | "See the slope change as you move along the curve" |
| Probability | Monte Carlo visual | "Run 1000 coin flips — watch the distribution emerge" |
| Geometry | Manipulable constructions | "Drag vertex C — what happens to the angle?" |
| Sequences/series | Step-by-step accumulation | "Watch the partial sums approach the limit" |

### Implementation Note

Simulations can be built as parameterized SVG/Canvas components rendered by the existing diagram pipeline. They don't require a game engine — slider + redraw is sufficient for most math concepts. The `InteractiveElement.SliderExplore` type in the micro-lessons data model already accommodates this.

**Priority: MEDIUM** — High learning impact but higher production cost per concept. Build for top 20-30 hardest-to-master concepts first (identified by lowest avg mastery velocity in `ConceptMasteryState` analytics).

---

## Tier 3 — Conversational Tutoring (Full RAG)

### The Problem

Students can't ask "why?" or "I don't understand this part" during a session. The interaction is strictly question → answer → feedback. The `AddAnnotation` command (kind: 'confusion' / 'question') is the closest thing — students can flag confusion, but get no response.

The `ConversationThreadActor` and Redis messaging system exist but are for admin/teacher messaging — not student-concept-level tutoring.

### Proposed Solution

A conversational interface where students ask follow-up questions about the current concept/question. Entry point: extend `AddAnnotation(kind: 'question')` to trigger a tutoring response via new `TutoringResponse` SignalR event.

### Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Student Client  │────▶│  TutorActor      │────▶│  RAG Pipeline   │
│  (chat widget)   │◀────│  (per session)   │◀────│                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
                              │                        │
                              ▼                        ▼
                        ┌──────────┐           ┌──────────────┐
                        │ Student  │           │ Vector Store │
                        │ Actor    │           │ (pgvector)   │
                        │ (context)│           │              │
                        └──────────┘           └──────────────┘
```

**Retrieval corpus** (what needs to exist first):
- Question explanations — **NOT currently persisted** (L1 from Tier 1 must be built first)
- Concept descriptions from curriculum graph (`CurriculumGraphActor` has names, topic clusters, intrinsic load — but not explanatory text)
- Textbook/reference excerpts — **NOT YET INGESTED** (pipeline `PipelineItemDocument` has 10 stages but only extracts questions, not explanatory content. Options are empty at ingestion — Bagrut math is open-ended.)

**Vector store**: pgvector in existing Postgres (Marten already runs there). `DeduplicationService` already has a TODO for Level 3 semantic embedding (mE5-large + pgvector/Redis VSS) — deferred until corpus > 10K items. No separate Pinecone/FAISS needed.

**Context window per turn**:
- Current question + student answer
- `ConceptMasteryState` for this concept + prerequisites (PSI, Bloom, errors, quality quadrant)
- `ScaffoldingService` level (determines response depth)
- `FocusLevel` from `FocusDegradationService` (determines verbosity — shorter responses when fatigued)
- `ConfusionState` from `ConfusionDetector` (context for what the student is struggling with)
- `DisengagementType` from `DisengagementClassifier` (bored vs fatigued = different tutoring approach)
- Last 3 conversation turns
- Top 3 retrieved passages from corpus
- Active methodology from `MethodologyResolver` (determines response style)
- `MethodHistory` for this concept (what's been tried)

**Methodology-aware responses** (all 9 methodologies):
- Socratic: Ask questions back, never give direct answers
- WorkedExample: Walk through step-by-step
- Feynman: Ask student to explain, then correct gaps
- RetrievalPractice: Prompt recall before revealing
- Analogy: Connect to something the student already knows
- BloomsProgression: Calibrate cognitive demand to current Bloom level
- DrillAndPractice: Provide varied repetitions
- SpacedRepetition: Reference what was learned previously
- ProjectBased: Connect to real-world applications

### Blockers

1. **No explanatory content corpus**: The ingestion pipeline (`PipelineItemDocument`) extracts questions only, not explanatory text. Need a parallel `ContentExtracted` pipeline stage (new Marten event). Even question options are empty at ingestion (Bagrut math is open-ended).
2. **No persisted explanations**: L1 from Tier 1 must be built first — currently `QuestionState` has no explanation field, and AI-generated explanations are discarded.
3. **No LLM abstraction layer**: Current LLM integration is hardcoded Gemini endpoints (`GeminiOcrClient.cs`). Need `ILlmClient` with Anthropic/OpenAI adapters. This is also a Tier 1 prerequisite. Also unblocks 3 stubbed quality gate dimensions (FactualAccuracy, LanguageQuality, PedagogicalQuality).
4. **Cost**: LLM call per student message. At scale, this is the dominant cost center.
5. **Safety**: Student-facing LLM needs content filtering, topic guardrails (stay on curriculum — `IConceptGraphCache` can validate concept scope), and methodology enforcement.
6. **Rate limiting**: Add per-student quotas to existing `LlmCircuitBreakerActor`.

### Learning Impact

Conversational tutoring has ~0.4 sigma effect size (Bloom's 2-sigma problem research). But:
- Bad RAG is **worse** than no RAG — confidently explaining wrong things destroys trust
- Requires good retrieval corpus (doesn't exist yet)
- Value compounds only after Tier 1 and 2 are in place
- Must integrate with ConfusionDetector — tutoring during ConfusionResolving may interrupt productive struggle

### Priority: MEDIUM-LOW

High value but high cost, high risk, and blocked on content corpus + LLM abstraction + L1 explanation persistence. Build after Tier 1-2 are validated.

---

## What's NOT Needed (Yet)

| Technology | Why Not |
|------------|---------|
| Dedicated vector DB (Pinecone, Weaviate, FAISS) | pgvector in existing Postgres is sufficient for the corpus size. `DeduplicationService` already plans for this. Reassess at 1M+ documents. |
| Embedding cache / semantic search for questions | Questions have explicit concept IDs via `IQuestionPool` index. Structured `conceptId → questions` lookup beats semantic search here. |
| Real-time streaming (WebSocket/SignalR for mastery) | Already decided: NATS + polling. Not reopening. (Note: SignalR IS used for the session interaction loop — but mastery subscriptions are NATS-based.) |
| Knowledge graph DB for RAG | `CurriculumGraphActor` already loads full graph from Neo4j at startup into `IConceptGraphCache`. Neo4j is query-only at startup, all runtime reads are O(1) in-memory. |
| Fine-tuned model | Pre-prompt with methodology + mastery context via `ScaffoldingService` metadata. Fine-tuning only if prompt engineering hits ceiling. |
| Separate A/B testing framework | `FocusExperimentConfig` + `FocusExperimentCollector` already operational with 6 experiments and per-session metrics. Extend this for explanation/hint experiments. |
| Separate confusion/affect detection | `ConfusionDetector`, `DisengagementClassifier`, `FocusDegradationService`, `CognitiveLoadService` all exist and are operational. Use their outputs as inputs to hint/explanation timing. |

---

## Cache Strategy (All Tiers)

| Layer | What | Store | TTL | Invalidation | Status |
|-------|------|-------|-----|-------------|--------|
| Actor memory | Student mastery state (~500KB) | `StudentActor` virtual grain | 30min idle passivation | Event replay on reactivation, snapshot every 100 events | Exists |
| Actor memory | Session state (fatigue, hints, confusion) | `LearningSessionActor` child | Session lifetime | Destroyed on `EndSession` | Exists |
| Actor memory | Question pool per subject | `IQuestionPool` in-memory index | Permanent | NATS `item.published` hot-reload | Exists |
| Singleton | Concept graph + MCM | `CurriculumGraphActor` + `McmGraphActor` | App lifetime | Hot-reload on `CurriculumPublished` | Exists |
| Singleton | Circuit breaker state | `LlmCircuitBreakerActor` per-model FSM | App lifetime | Auto-recovery (HalfOpen → probe → Closed) | Exists |
| Redis | Messaging threads | `cena:thread:{id}` streams (max 10K msgs) | 30 days | Archival by `MessageArchivalWorker` | Exists |
| Marten | L1 persisted explanation per question | `QuestionState.Explanation` field | Permanent | Question edit triggers re-generation | Tier 1 (NEW) |
| Redis | L2 ErrorType explanations | `explain:{qId}:{errorType}` | 30 days | Question version change | Tier 1 (NEW) |
| Redis | LLM response cache | `llm:{promptHash}` | 7 days | Model version change | Tier 1 (NEW) |
| Postgres | Conversation history | Marten document | Permanent | N/A | Tier 3 (NEW) |
| Postgres | Content embeddings | pgvector column | Permanent | Re-embed on content change | Tier 3 (NEW) |

**Note**: Session context (fatigue, confusion state, focus level) is NOT in Redis — it lives in `LearningSessionActor` memory. Redis is used for messaging threads, dedup, and (proposed) explanation caching.

---

## Recommended Build Order

| # | What | Depends On | Effort | AI Cost | Learning Impact |
|---|------|-----------|--------|---------|-----------------|
| 0 | **Implement real LLM API calls**: `AiGenerationService` has 4 provider stubs returning mock data. Implement real Anthropic SDK calls (Claude Sonnet 4.6 = primary tutoring model per routing config). Also unblocks 3 quality gate stubs. | Nothing (stubs exist in `AiGenerationService.cs`, routing config in `contracts/llm/routing-config.yaml`) | 2-3 days | $0 | Enables Tier 1 + 3 + quality gate |
| 1a | **Persist L1 explanations**: Add `Explanation` to `QuestionState`, stop discarding AI-generated explanations | Nothing. Verified path: (1) add `string? Explanation` to `QuestionAiGenerated_V1`, (2) update `QuestionState.Apply()`, (3) add to `PublishedQuestion` record, (4) hydrate in `QuestionPoolActor.InitializeAsync()`, (5) wire frontend to pass explanation through `CreateQuestionRequest` | 1 day | $0 | Foundation for all tiers |
| 1b | Hint **content generation** + BKT credit weighting + confusion-state gating | Hint infrastructure (exists), `IConceptGraphCache` (exists), `ConfusionDetector` (exists) | 2-3 days | $0 | High |
| 2 | ErrorType-based L2 explanation cache | `ILlmClient` (step 0), ErrorType classification (exists), L1 as fallback | 3-5 days | One-time batch | High |
| 3 | L3 personalized explanation generation | L2 cache (step 2), `ScaffoldingService` (exists), `MethodologyResolver` (exists), `ConfusionDetector` (exists) | 3-4 days | ~$0.001/miss | High |
| 4 | A/B experiment for Tier 1-2 validation | `FocusExperimentCollector` (exists — add explanation/hint experiments) | 1-2 days | $0 | Measures impact |
| 5 | Content extraction pipeline stage | Existing `PipelineItemDocument` (add `ContentExtracted` event) | 5-7 days | OCR cost (exists) | Enables Tier 3 |
| 6 | pgvector + embedding pipeline | Content extraction (step 5), connects to `DeduplicationService` Level 3 TODO | 3-4 days | Embedding cost | Enables Tier 3 |
| 7 | Conversational tutoring (`TutorActor`) | Everything above | 7-10 days | ~$0.01/turn | Medium-High |

**New step 1a is the cheapest possible win** — the AI already generates explanations during question creation but they're thrown away. Persisting them gives every published question an L1 explanation with zero additional AI cost.

---

## Open Questions for Discussion

1. **Hint credit weighting curve**: Proposed BKT credit reduction (1.0 → 0.7 → 0.4 → 0.1) modifies `P_T` in `BktTracer.Update()`. Should it vary by concept `IntrinsicLoad` (available from `IConceptGraphCache`)? High-load concepts might warrant gentler penalties for hint usage.

2. **ErrorType-to-misconception mapping**: The 6 `ErrorType` categories (Procedural, Conceptual, Motivational, Careless, Systematic, Transfer) are already classified. Should L2 explanations key on `(questionId, ErrorType)` or on finer-grained answer pattern clusters? The former is simpler and maps to existing infrastructure.

3. **Methodology enforcement in LLM responses**: How strict? The `MethodologySwitchService` already has an exhaustion escalation path (all 8 non-default methodologies tried → connect tutor / suggest skip). Should Socratic mode *never* give the answer, or soften after the MCM graph signals exhaustion?

4. **Cost budget**: What's the per-student per-month AI budget? L2 cache with 80-90% hit rate makes L3 calls rare (~$0.001/miss x ~10 misses/session x 20 sessions/month = ~$0.20/student/month for explanations). Tier 3 conversational is ~$0.01/turn x unknown turns.

5. **Content corpus source**: The ingestion pipeline (`PipelineItemDocument`) has 10 stages but extracts questions only (options are empty for Bagrut math). Textbook PDFs through a parallel `ContentExtracted` pipeline stage? Teacher-authored content? External curriculum resources? Need to define the Marten document type and NATS events.

6. **Offline support**: Hint content generation (Tier 2) works offline if hints are pre-generated per question. L3 explanations and Tier 3 tutoring need network. The existing `OfflineSyncHandler` (three-tier classification: Unconditional weight=1.0, Conditional weight=0.75-1.0 based on methodology freshness, ServerAuthoritative weight=0.0) can queue hint requests for sync.

7. **Confusion-state integration**: The `ConfusionResolutionTracker` has an adaptive patience window (3-7 questions). Should hint/explanation delivery always respect this window, or should the student be able to override it ("just tell me the answer")? The existing `RequestHint` command could serve as the override.

8. **WhatsApp/Telegram integration**: `OutreachSchedulerActor` is outbound-only with full channel routing (WhatsApp > Push > Telegram > Voice), quiet hours (22:00-07:00 IST), and per-student throttling (max 3/day). The WhatsApp contract already defines interactive message types with buttons (inline quiz). Should conversational tutoring extend to these channels, or stay in-app? The webhook endpoints (`POST /api/webhooks/whatsapp`, `/telegram`) currently route to NATS — could route to a `TutorActor` instead.

9. **LLM provider implementation priority**: `AiGenerationService` already has stubs for Anthropic, OpenAI, Google, and Azure — but ALL return mock data. The routing config (`contracts/llm/routing-config.yaml`) maps Claude Sonnet 4.6 as primary tutoring model and Kimi K2.5 for structured tasks. Implementing real Anthropic SDK calls is the prerequisite for Tier 1 L3, Tier 3, and the 3 stubbed quality gate dimensions. Start with Anthropic (primary tutoring model) or implement multiple providers?

10. **Behavioral signals in LLM context**: `BusConceptAttempt` transmits `BackspaceCount` and `AnswerChangeCount` from the client. High values indicate uncertainty. Should these feed into L3 explanation personalization (e.g., "I see you reconsidered your answer — let's look at why both options seemed plausible")?

---

## Appendix: Key Source Files

| File | What It Does |
|------|-------------|
| `contracts/frontend/signalr-messages.ts` | Student SignalR command/event contract (AnswerEvaluated, HintDelivered, etc.) |
| `src/actors/Cena.Actors/Students/StudentActor.cs` | Virtual grain per student, event-sourced state |
| `src/actors/Cena.Actors/Students/StudentState.cs` | In-memory state + Apply() projections |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Session lifecycle, fatigue, hint handling |
| `src/actors/Cena.Actors/Mastery/BktTracer.cs` | BKT update rule (<1us, zero alloc) |
| `src/actors/Cena.Actors/Mastery/HlrCalculator.cs` | Half-life regression (6-feature) |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | Rich per-concept state record |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Mastery+PSI → scaffolding level |
| `src/actors/Cena.Actors/Mastery/StagnationDetector.cs` | 6-signal stagnation detection |
| `src/actors/Cena.Actors/Mastery/MasteryPipeline.cs` | 6-step mastery computation flow |
| `src/actors/Cena.Actors/Mastery/PrerequisiteSatisfactionIndex.cs` | PSI = avg(mastery of prerequisites) |
| `src/actors/Cena.Actors/Mastery/MasteryQualityClassifier.cs` | 2x2 quality quadrant classification |
| `src/actors/Cena.Actors/Serving/QuestionSelector.cs` | 5-step adaptive selection with ZPD targeting |
| `src/actors/Cena.Actors/Methodology/MethodologyResolver.cs` | 5-layer hierarchical methodology resolution |
| `contracts/actors/methodology_switch_service.cs` | MCM graph lookup + exhaustion escalation |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | Per-model 3-state circuit breaker |
| `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` | Gemini 2.5 Flash OCR (only LLM integration) |
| `src/actors/Cena.Actors/Services/FocusExperimentConfig.cs` | A/B testing: 6 experiments |
| `src/actors/Cena.Actors/Services/FocusExperimentCollector.cs` | Per-session metrics collection |
| `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | 3-factor fatigue model |
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | 8-signal composite focus (615 lines) |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | Bored vs Fatigued classification |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 4-signal confusion detection |
| `src/actors/Cena.Actors/Services/ConfusionResolutionTracker.cs` | Adaptive patience window (3-7 questions) |
| `src/actors/Cena.Actors/Outreach/OutreachSchedulerActor.cs` | HLR timers, channel routing, throttling |
| `src/actors/Cena.Actors/Questions/QuestionState.cs` | Question aggregate (NO explanation field — this is the gap) |
| `src/api/Cena.Admin.Api/AiGenerationService.cs` | AI generation returns Explanation (currently discarded) |
| `src/api/Cena.Admin.Api/QualityGate/QualityGateService.cs` | 8-dimension quality gate (3 LLM stubs) |
| `src/actors/Cena.Actors/Sync/OfflineSyncHandler.cs` | 3-tier offline event classification |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | NATS command/event payloads (incl. behavioral signals) |
| `src/api/Cena.Admin.Api/QuestionBankService.cs` | Question creation — where explanation is discarded |
| `src/actors/Cena.Actors/Events/QuestionEvents.cs` | Question event records (QuestionAiGenerated_V1 needs Explanation field) |
| `contracts/llm/routing-config.yaml` | Model routing: Claude Sonnet 4.6 (tutoring), Kimi K2.5 (structured), task→model mappings |
| `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs` | Transactional outbox: Marten → core NATS (5s poll, 100/cycle, 10 retries) |
| `src/infra/docker/nats-setup.sh` | JetStream stream definitions (8 streams, 90-day retention) |
| `src/actors/Cena.Actors/Messaging/ConversationThreadActor.cs` | Student-teacher messaging (potential Tier 3 extension point) |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | Event types, document types, projections |

---

## References

- Bloom, B.S. (1984). The 2 Sigma Problem — tutoring effect sizes
- Corbett & Anderson (1994). Knowledge Tracing — BKT foundations (implemented: `BktTracer.cs`)
- Settles & Meeder (2016). Half-Life Regression — spaced repetition (implemented: `HlrCalculator.cs`)
- VanLehn (2011). The Relative Effectiveness of Human Tutoring — hint/scaffold research
- Anderson et al. (1995). Cognitive Tutors — adaptive explanation research
- D'Mello & Graesser (2012). Dynamics of Affective States During Complex Learning — confusion resolution (implemented: `ConfusionDetector.cs`)
- Warm, J.S. (1984). Sustained Attention in Human Performance — vigilance decrement (implemented: `FocusDegradationService.cs`)
- Baker et al. (2010). Better to Be Frustrated than Bored — boredom vs fatigue (implemented: `DisengagementClassifier.cs`)
- Kapur, M. (2008). Productive Failure — don't interrupt confusion that resolves (implemented: `ConfusionResolutionTracker.cs`)
