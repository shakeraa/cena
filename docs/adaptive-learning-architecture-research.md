# Adaptive Learning Platform Architecture Research
**Date:** March 2026
**Purpose:** Architectural patterns from production adaptive learning systems, synthesized for Cena's design decisions

---

## 1. Squirrel AI Architecture

### What Is Publicly Known

Squirrel AI (Yixue Group) is the most technically documented Chinese adaptive learning platform. Their published research provides more architecture detail than their press releases.

#### Knowledge Graph Structure

The knowledge decomposition follows a strict hierarchical model:

- **Level 0**: Curriculum standard (e.g., "Fractions")
- **Level 1**: Topic (e.g., "Addition and subtraction of fractions")
- **Level 2**: Knowledge point (~300 per subject at middle school level)
- **Level 3–9**: Nano-granularity sub-skills — a single Level 2 point decomposes into up to 9 finer layers

For junior high school mathematics alone: ~300 standard knowledge points decompose into **~30,000 fine-grained knowledge components**. This is the publicly confirmed figure for one subject. Their "10,000+ nodes" claim refers to the knowledge points before full nano-decomposition.

Each node stores:
- Item pool (text, animation, slides, video)
- Prerequisite edges (which nodes must be mastered first)
- Difficulty distribution over the item pool
- Aggregate mastery statistics across all students

Graph edges are **empirically derived**, not hand-authored: they use real student learning data to stabilize the prerequisite graph. An edge is added when performance on node A consistently predicts performance on node B; edges are removed if data contradicts the assumed prerequisite relationship. The graph "iterates until stable."

#### Probabilistic Knowledge State (PKS)

The PKS model is a multi-tiered student capability model with **more than 20,000 parameters per student**. What this means architecturally:

- Each student has a probability distribution over mastery states for every knowledge component — not just a binary mastered/not-mastered
- The 20,000 parameters encode the student's estimated ability across all knowledge components, plus learned difficulty calibrations
- The model is updated in real time after each item response
- It serves both individual diagnosis ("what does this student not know?") and group analysis ("where is cohort understanding weakest?")

This is architecturally similar to a **multidimensional Item Response Theory (MIRT)** model — each knowledge component is a latent dimension, and the student's response to each item updates the posterior over all relevant dimensions simultaneously.

#### Large Adaptive Model (LAM) — 2024 Architecture

Squirrel AI's 2024 LAM integrates three layers:

1. **Foundation model layer**: LLM for language understanding, multimodal parsing (text, image, handwritten input, video)
2. **Advanced RAG layer**: The knowledge graph and PKS are the retrieval corpus — when the LLM generates an explanation, it retrieves pedagogically-validated content aligned to the student's current knowledge state
3. **Educational AI Agent layer**: Orchestrates the diagnostic agent, forecasting agent (predicts next mastery state), and problem-solving agent

The **MCM graph** (Mode of Thinking, Capability, Methodology) is a second graph layered on top of the knowledge graph — it maps which teaching methodologies and cognitive strategies are most effective for which knowledge components and student profiles. This is exactly the "methodology layer" Cena's stagnation switching system needs.

#### Real-Time Difficulty Adaptation

Not fully public, but inferred from papers:
- Difficulty adaptation uses the PKS posterior to compute expected score on each candidate item
- Item selection targets the zone of proximal development: items where predicted correctness probability is ~0.70–0.75 (not too easy, not too hard)
- This is a form of **Computerized Adaptive Testing (CAT)** applied continuously during learning, not just during assessment
- Items are pre-scored by difficulty offline using IRT parameter estimation across the full student population

#### Scale Architecture (Inferred)

Squirrel AI operates at 24M students. Their system must handle:
- Batch PKS updates (offline, after session) for most state changes
- Real-time item selection (must be fast — computed during the pause between questions)
- Knowledge graph is read-heavy and likely cached aggressively; graph structure changes are infrequent (stabilized through data, not daily updates)

**Key architectural pattern**: the knowledge graph is **static on the hot path** — it's pre-computed and cached. Only the student's PKS vector is dynamic per request.

---

## 2. Duolingo Architecture

Duolingo has published more engineering detail than any other adaptive learning company. The following comes from official engineering blog posts, the IEEE Spectrum piece, and the ACL 2016 HLR paper.

### Session Generator (Scala Rewrite)

The core lesson engine was rewritten from Python to Scala. The architectural decisions reveal how Duolingo structures their hot path:

**Before (Python)**:
- Multiple network dependencies in the hot path
- 750ms average latency
- 99.9% availability

**After (Scala on JVM)**:
- Two-tier data model:
  - **Course data** (static per course): processed offline, serialized to AWS S3, loaded into local in-process cache per service instance
  - **User data** (dynamic per user): injected per request via API
- 14ms average latency (98% reduction)
- 100% availability in initial months
- **HTTP server**: Finatra
- **DI**: Guice
- **Deployment**: AWS Elastic Beanstalk (rolling deploys, autoscaling, load balancing)

**The insight**: The lesson engine is stateless at the service level. User state is a parameter injected per request, not resident in the service. Course content is a pre-built artifact loaded from S3. This makes horizontal scaling trivial.

### Birdbrain — Proficiency Estimation Model

Birdbrain is Duolingo's ML model for estimating per-learner proficiency per language concept. Published architecture:

- **Model type**: Flavor of logistic regression inspired by **Item Response Theory (IRT)**
- **Specifically**: A generalization of the **Elo rating system** — the same math used to rank chess players
- **Models two variables**: item difficulty and learner ability
- **Probability of correct response** = f(learner_ability - item_difficulty)
- Updates happen after each lesson, ingesting every individual exercise response

The proficiency scores are stored as **single rows in DynamoDB** — one row per user, containing the full proficiency vector. This is a deliberate choice to minimize API latency on reads (single key lookup = single DynamoDB read with consistent single-digit millisecond latency).

**Write optimization**: Duolingo reduced write frequency by **buffering user state changes in memory**, exploiting shard-level guarantees — "the same thread will always see every record for a specific user." They added LRU cache eviction. This made dual A/B testing of grammar and vocabulary models 50% cheaper than even the original single model.

**Birdbrain is modular**: it runs as a separate service that the Session Generator calls. There is also a separate vocabulary memory model (HLR — see below) that runs in parallel. They are explicitly described as complementary systems.

### Half-Life Regression (HLR) — Memory Model

Published in full at ACL 2016, open-sourced on GitHub.

**Core equation**:
```
p(t) = 2^(-Δ/h)
```
Where:
- `p(t)` = probability of correct recall at time t
- `Δ` = elapsed time since last review
- `h` = memory half-life (time for memory strength to decay to 50%)

**Half-life computation**:
```
h = 2^(Θ · x)
```
Where:
- `Θ` = learned regression weights (trained offline)
- `x` = feature vector encoding student's history with this word: times seen, times correct, session-level counts, lag time

**What makes this different from vanilla Leitner/SM-2**:
- Half-life is **trainable** — `Θ` is fit from data, so hard words (irregular verbs, false cognates) get longer half-lives than easy words
- Word-level features (length, morphological complexity, cognate status) are regression variables — the model learns that cognates decay slower than irregular forms
- Validated against 12M+ practice sessions; 50% lower error rate vs. Leitner system

**Scale**: The student model database has **billions of entries updated 3,000 times per second**. Each entry = one (user, lexeme) pair with its statistics.

**Architectural implication for Cena**: HLR is directly implementable. The key insight is to track `(h, t_last_review)` per (student, knowledge_component) pair and compute recall probability on-demand. Spaced repetition scheduling is then `t_next = t_last + h` (review when predicted recall drops below 0.9).

### Duolingo Tech Stack (Confirmed)

| Layer | Technology |
|-------|-----------|
| Cloud | AWS |
| Primary DB | DynamoDB (~31B objects) |
| Session Engine | Scala (Finatra HTTP), deployed on Elastic Beanstalk |
| Backend (legacy/other services) | Python (Pyramid framework), Java |
| Async tasks | Celery |
| AWS interface | boto |
| Mobile | React Native (iOS/Android) |
| Web | React |
| ML training | Python |
| DynamoDB access pattern | Per-user state: single row lookup by user_id |

### Gamification Backend

No engineering posts specifically about the gamification implementation exist. What is confirmed from product documentation and growth analysis:

**Streaks**:
- Track consecutive days with at least one completed exercise
- Streak-saver notifications fire when a user has not practiced and the day is ending
- "Protect the channel" principle: notification volume is constrained; quality (timing, copy, images) is A/B tested
- Bandit algorithm (likely Thompson Sampling or UCB) used to optimize notification templates

**XP and Leagues**:
- Users are bucketed into leagues of ~30 people, matched competitively (not socially — strangers, not friends)
- League tier progression: Bronze → Silver → Gold → ... (8 tiers)
- Weekly XP determines tier advancement
- The league system launched in 2019 and is credited with significant DAU growth

**Growth model**:
- Users segmented by engagement: New, Current (CURR), Reactivated (RURR), Resurrected (SURR)
- CURR (Current User Retention Rate) identified as 5x more impactful than any other growth lever
- Measurement: "Time Spent Learning Well" (TSLW) — a proprietary metric ensuring A/B tests improve learning quality, not just raw engagement metrics

**Inferred backend pattern for gamification**: Streaks and XP are almost certainly separate DynamoDB tables with simple counter updates. Streak state = (current_streak_count, last_practice_timestamp). XP = running counter. Leagues require a weekly batch job to compute rankings and assign users to league groups for the next week.

---

## 3. Minecraft Education Edition — Learning as Gameplay

Minecraft Education does not publish technical architecture. What is architecturally relevant is the **design pattern**, not the implementation.

### The Constructionist Architecture

Minecraft Education implements **Papert's constructionism** — students learn by building, not by receiving. The architectural implication:

- There is no "student model" in the traditional ITS sense. Mastery is inferred from what the student builds, not from quiz responses.
- Learning objectives are encoded as **world constraints and challenges**, not as question-and-answer sequences.
- Assessment is embedded in task completion, not separate from it.

### Technical Learning Affordances

- **Code Builder**: In-game coding via block-based (MakeCode) or text-based (Python/JavaScript) programming that controls game entities — students "learn coding by building a zoo" rather than answering "what does a for loop do?"
- **Classroom Mode**: Teacher has a god-view map, can teleport students, monitor chat, push instructions via chalkboards — this is a teacher-facing dashboard layer on top of the game
- **Standards alignment**: Curriculum mapping to CSTA, ISTE standards is done at the lesson design level, not in the engine. The engine has no awareness of standards.

### The Pattern Cena Should Borrow

The Minecraft model demonstrates that **environmental immersion replaces direct instruction effectively for procedural knowledge** (coding, lab procedures, engineering). For Bagrut STEM subjects:
- Physics: Minecraft-style simulations of force/motion would be more engaging than text problems
- Chemistry: Lab simulation where students mix compounds and observe results
- Math: Geometric construction tools, proof-building environments

The constraint is that Minecraft Education is teacher-led, not student-adaptive. There is no system that says "this student struggles with loops, show them a loop-heavy challenge." That's the gap Cena can fill.

---

## 4. Open-Source Adaptive Learning Platforms

### OATutor (University of California, Berkeley — CAHLR Lab)

**The best publicly available open-source ITS with clean architecture. Published at CHI 2023.**

**Tech stack**: React JS frontend, Firebase (Firestore) for optional persistence, BKT-brain.js for knowledge tracing.

**Knowledge component model**:
```json
// skillModel.json
{
  "problemID1a": ["skill1", "skill2"],
  "problemID1b": ["skill2", "skill3"]
}
```
Each problem step maps to one or more knowledge components. This many-to-many mapping allows skills to be isolated and tracked independently.

**BKT parameters** (`bktParams.json` per content source):
```json
{
  "skill1": {
    "pLearn": 0.3,   // probability of transitioning from unknown to known after practice
    "pSlip": 0.1,    // probability of getting it wrong despite knowing
    "pGuess": 0.2,   // probability of getting it right without knowing
    "pKnown": 0.4    // initial probability of already knowing this skill
  }
}
```

**BKT update rule** (standard Bayes):
```
P(known | correct) = P(correct | known) * P(known) / P(correct)
                   = (1 - pSlip) * P(known) / [(1-pSlip)*P(known) + pGuess*(1-P(known))]

P(known | incorrect) = P(incorrect | known) * P(known) / P(incorrect)
                     = pSlip * P(known) / [pSlip*P(known) + (1-pGuess)*(1-P(known))]

After update, apply learning transition:
P(known_t+1) = P(known_t) + (1 - P(known_t)) * pLearn
```

**Adaptive problem selection heuristic**: Choose problem with **lowest average P(known)** across all its skills. This greedily targets the weakest knowledge component.

**Firestore schema pattern**: Events are partitioned into time-bucketed collections to manage Firestore document limits and costs. Logged events: answer submissions (with correctness), hint access, session focus changes.

**Content pool structure**:
```
content-pool/
  problem-id/
    problem-id.json        # metadata, answer verification
    steps/
      problem-ida/
        problem-ida.json   # step definition
        tutoring/
          defaultPathway.json   # hint sequence
```

**Directly usable for Cena**: OATutor's skill model and BKT implementation are clean enough to adapt directly. The Firebase schema pattern maps well to Cena's Firebase stack.

GitHub: `https://github.com/CAHLR/OATutor`

### pyBKT (CAHLR Lab)

Python library implementing BKT and its major extensions. Published at EDM 2021, actively maintained.

Extensions beyond standard BKT:
- **Forgetting**: adds P(forget) — probability of transitioning back to unknown
- **Item effects**: different slip/guess per item, not just per skill
- **Multiple subskills**: student can be modeled as having multiple latent skills per problem

Parameter fitting uses **Expectation Maximization (EM)**. Input: student interaction logs in standard format (student_id, skill_name, correct). Output: fitted parameters, mastery predictions, cross-validation scores.

**For Cena**: pyBKT is the offline training tool. You train BKT parameters on your interaction data, then deploy the resulting parameters into a runtime BKT update (which is just the update rule above — no library needed at runtime).

GitHub: `https://github.com/CAHLR/pyBKT`

### ALEKS — Knowledge Space Theory (KST) Architecture

ALEKS is proprietary but its mathematical model is fully published in *Learning Spaces* (Doignon & Falmagne, 2011).

**The key concept — Knowledge State**: A subset of all knowledge components that is "feasible" — i.e., if you know everything in the set, that's a coherent state. Not all subsets are feasible: you can't know calculus without knowing limits.

**Knowledge Space**: The set of all feasible knowledge states for a domain. For ALEKS Algebra 1 (~350 concepts), there are **millions** of feasible states.

**How assessment works**:
- Adaptive questioning using **Markov procedures**: each answer updates posterior probability over knowledge states
- Despite millions of states, only **25–30 questions** are needed to accurately classify a student's state
- This is because the prerequisite structure dramatically constrains feasible states

**ALEKS vs. BKT — the fundamental difference**:
- BKT tracks each skill independently (no relationship between skills)
- KST tracks the student as being in one of many joint states, where skills are correlated through prerequisite structure
- KST is more accurate for subjects with strong prerequisites (mathematics); BKT is simpler and works for more independent skills (vocabulary)

**For Cena's diagnostic**: The onboarding diagnostic uses an "ALEKS-inspired Knowledge Space approach" (already specified in system-overview.md). This means: each answer eliminates a cluster of knowledge states from possibility, achieving rapid state estimation in 10–15 questions.

---

## 5. Domain-Driven Design for Learning Platforms

### The Standard ITS Four-Component Model as Bounded Contexts

The academic ITS literature's four-component model maps directly to DDD bounded contexts:

| ITS Component | DDD Bounded Context | Aggregate Root | Key Invariants |
|--------------|---------------------|----------------|----------------|
| Domain Model | **Curriculum** | `KnowledgeGraph` | A KnowledgeComponent must have all prerequisites already in the graph |
| Student Model | **Learner** | `StudentProfile` | Mastery state must be consistent with observed response history |
| Tutoring Model | **Pedagogy** | `LearningSession` | A session must have one active methodology at a time |
| Interface Model | **Delivery** | `ExercisePresentation` | An exercise must match the student's current ability band |

A fifth context is needed for production systems:

| Context | Aggregate Root | Responsibility |
|---------|---------------|----------------|
| **Engagement** | `EngagementRecord` | Streaks, XP, achievements, league membership |
| **Analytics** | (read models only) | CQRS read side: cohort reports, progress dashboards |

### Aggregate Root Question: Student or LearningSession?

**The answer: both, at different granularities, for different reasons.**

`StudentProfile` is the long-lived aggregate:
- Owns the PKS/BKT mastery vector (persistent state)
- Owns the methodology effectiveness history (which methods worked, which triggered stagnation)
- Owns the spaced repetition schedule (next review dates per knowledge component)
- Invariant: mastery state can only be updated by appending a completed learning interaction

`LearningSession` is the transactional aggregate:
- Scoped to one session (30–60 minutes)
- Owns: session-level item responses, active methodology, real-time difficulty adaptations, cognitive load signals
- On session close: emits `SessionCompleted` domain event with aggregate statistics
- `StudentProfile` subscribes to `SessionCompleted` and updates its mastery vector

**Why not just Student?** Because sessions need transactional consistency on their own: you need to guarantee that item responses within a session are processed atomically, that methodology switching mid-session is consistent, and that the session can be abandoned without corrupting the student model. Making `LearningSession` its own aggregate with its own lifecycle enables this.

**Why not just LearningSession?** Because cross-session state (spaced repetition schedules, methodology effectiveness tracking, the knowledge graph) has a lifecycle longer than any single session. It belongs in `StudentProfile`.

### Modeling Methodology Switching as a Domain Concept

This is a first-class domain concept in Cena (it's already specified). The DDD model:

**Value Object: `TeachingMethodology`**
```
TeachingMethodology {
  type: Enum (Socratic | SpacedRepetition | Feynman | ProjectBased | DrillPractice | ...)
  activatedAt: Timestamp
  activationReason: Enum (Initial | StagnationDetected | StudentRequest | EffectivenessThreshold)
  targetErrorType: Enum (Procedural | Conceptual | Motivational | null)
}
```

**Value Object: `StagnationSignal`**
```
StagnationSignal {
  accuracyPlateauScore: Float       // 0-1
  responseTimeDriftScore: Float     // 0-1
  sessionAbandonmentScore: Float    // 0-1
  errorRepetitionScore: Float       // 0-1
  annotationSentimentScore: Float   // 0-1
  compositeScore: Float             // weighted average, trigger at 0.7
  consecutiveTriggeredSessions: Int // trigger switch at 3
}
```

**Domain Event: `MethodologySwitchTriggered`**
```
MethodologySwitchTriggered {
  studentId: String
  fromMethodology: TeachingMethodology
  toMethodology: TeachingMethodology
  stagnationSignal: StagnationSignal
  switchedAt: Timestamp
}
```

**The switching rule as a domain service**:
```
MethodologySwitchingService.evaluate(
  currentMethodology: TeachingMethodology,
  recentSessions: List<SessionSummary>,
  studentProfile: StudentProfile
) -> Option<TeachingMethodology>
```

This is a domain service (not on the aggregate) because it reads from multiple sessions and a profile — it crosses aggregate boundaries.

### Event Sourcing Applicability

Event sourcing is well-matched to the `StudentProfile` aggregate because:
1. The full learning history is valuable (replay to reconstruct mastery at any point in time)
2. Audit trail is inherently required (regulatory, parental oversight)
3. The mastery state derivation is complex — being able to recompute it from events is a correctness guarantee
4. Events are the natural domain language: `ExerciseAttempted`, `SessionCompleted`, `MethodologySwitched`, `MasteryThresholdCrossed`

**Event stream per student** (append-only log):
```
ExerciseAttempted      { knowledgeComponentId, itemId, correct, responseTimeMs, timestamp }
HintRequested          { knowledgeComponentId, hintLevel, timestamp }
SessionStarted         { methodology, targetKnowledgeComponents[], timestamp }
SessionAbandoned       { minutesElapsed, lastKnowledgeComponentId, timestamp }
SessionCompleted       { sessionSummary, bktUpdates[], stagnationSignal, timestamp }
MethodologySwitched    { from, to, reason, timestamp }
SpacedRepetitionScheduled { knowledgeComponentId, nextReviewAt, timestamp }
DiagnosticCompleted    { knowledgeStates[], timestamp }
```

**CQRS read models** (materialized from the event stream):
- `MasteryView`: current BKT probability per knowledge component (hot read for lesson generation)
- `ProgressDashboardView`: session history, XP, streaks (for the student-facing UI)
- `StagnationAnalysisView`: rolling composite stagnation scores (for methodology switching decisions)
- `SpacedRepetitionQueueView`: ordered list of items due for review

**Practical constraint for solo architect**: Full event sourcing with a custom event store is non-trivial. A pragmatic hybrid: use a standard database with an append-only `events` table + current-state tables that are updated on write. This gives you the audit log and replay capability without the full complexity of an event store.

### Context Map

```
Curriculum [upstream] ---> Learner [downstream]
  (KG structure informs what can be assessed)

Learner [upstream] ---> Pedagogy [downstream]
  (StudentProfile drives LearningSession creation)

Pedagogy [upstream] ---> Engagement [downstream]
  (SessionCompleted events drive XP/streak updates)

Learner ---> Analytics [conformist]
  (Analytics subscribes to all domain events, builds read models)

Delivery [ACL around external] <--- Pedagogy
  (Pedagogy issues exercise selection commands to Delivery)
```

The **Anti-Corruption Layer (ACL)** sits between Delivery and external LLM providers (Claude, GPT-4o). The Pedagogy context issues a `GenerateSocraticQuestion(context)` command; the ACL translates that to a specific Claude API call with the appropriate system prompt, injects the anonymized student context, handles rate limiting and fallback to GPT-4o, and returns the result in the Pedagogy context's language.

---

## 6. Synthesis: Patterns for a Solo Architect Building Cena

### What to Take Directly

| Pattern | Source | Apply to Cena |
|---------|--------|--------------|
| Two-tier data split (static course data vs. dynamic user state) | Duolingo Scala rewrite | Curriculum graph = pre-built JSON artifact loaded at startup; only StudentProfile is dynamic per request |
| Single-row user state in DynamoDB/Firestore | Duolingo Birdbrain | Store the full BKT mastery vector as one Firestore document per student; avoids multi-document reads on hot path |
| HLR for spaced repetition | Duolingo | Track (h, t_last_review) per (student, KC) pair; schedule next review when predicted recall drops below 0.9 |
| BKT with pre-fitted parameters | OATutor / pyBKT | Fit parameters offline on interaction data; only run the update rule at runtime |
| Nine-layer nano-decomposition | Squirrel AI | Start coarser (3–4 levels), but design the schema to support deeper decomposition without migration |
| MCM graph (methodology ↔ KC mapping) | Squirrel AI | Encode which methodologies are effective for which KC types; start as a lookup table, evolve to a learned model |
| LearningSession as its own aggregate | DDD ITS pattern | Transactional boundary for a single session; emits events that update StudentProfile |
| Events as the student record | Event sourcing | Append-only interaction log per student; materialized views for hot reads |

### What to Avoid

| Anti-pattern | Why |
|-------------|-----|
| Storing mastery state in a relational join query | Duolingo explicitly moved to single-row DynamoDB reads to minimize latency; join-based lookups don't scale |
| Hand-authoring all prerequisite edges | Squirrel AI iterates the graph from data; start hand-authored but design for data-driven edge refinement |
| Running BKT parameter fitting at runtime | BKT parameters (pLearn, pSlip, pGuess) are offline-fitted; only the update rule runs per interaction |
| Full event sourcing from day one | Build append-only events table + current-state materialized view; don't build a full event store until you have the scale to justify it |
| Exposing methodology labels to students | Squirrel AI and Cena's spec both specify that methodology switching is invisible to the student |

### The Critical Path for Implementation

For a solo architect building with AI coding agents, the correct order is:

1. **Curriculum bounded context first**: Knowledge graph schema (nodes, edges, difficulty metadata). This is static data and the foundation everything else reads from. Can be built and loaded without any user-facing code.

2. **Learner bounded context second**: StudentProfile aggregate with BKT update rule. One Firestore document per student containing the full mastery vector. This is the most critical hot-path object in the system.

3. **LearningSession aggregate third**: Session creation, item selection (using BKT mastery vector + KC difficulty to target 0.7 correctness probability), response handling, BKT update trigger on session close.

4. **Methodology switching fourth**: StagnationSignal computation, MethodologySwitchingService, TeachingMethodology value object. This reads from LearningSession summaries and writes to StudentProfile.

5. **Engagement context last**: Streaks (simple counter + timestamp), XP (running total), spaced repetition queue (ordered list of KCs by next review date). These are the simplest components but are high-visibility to users.

---

## Sources

- [Squirrel AI Wikipedia](https://en.wikipedia.org/wiki/Squirrel_AI)
- [Squirrel AI at Harvard+MIT Symposium (2024)](https://www.prnewswire.com/news-releases/squirrel-ai-speaks-at-harvard--mit-joint-symposium-on-the-future-of-ai-based-adaptive-learning-302086461.html)
- [Squirrel AI Large Multimodal Adaptive Model (July 2024)](https://www.prnewswire.com/news-releases/squirrel-ai-debuts-enhanced-large-multimodal-adaptive-model-revolutionizing-its-educational-software-and-hardware-systems-302186596.html)
- [Foundation Models for Education (arXiv 2405.10959)](https://arxiv.org/html/2405.10959v1)
- [Duolingo Birdbrain Introduction](https://blog.duolingo.com/learning-how-to-help-you-learn-introducing-birdbrain/)
- [Duolingo Session Generator Rewrite in Scala](https://blog.duolingo.com/rewriting-duolingos-engine-in-scala/)
- [Duolingo Unique Engineering Problems (2024)](https://blog.duolingo.com/unique-engineering-problems/)
- [Duolingo Adaptive Lessons Blog](https://blog.duolingo.com/keeping-you-at-the-frontier-of-learning-with-adaptive-lessons/)
- [How Duolingo Learns How You Learn (HLR)](https://blog.duolingo.com/how-we-learn-how-you-learn/)
- [Duolingo Half-Life Regression (GitHub)](https://github.com/duolingo/halflife-regression)
- [HLR Paper: A Trainable Spaced Repetition Model (ACL 2016)](https://research.duolingo.com/papers/settles.acl16.pdf)
- [How Duolingo Reignited User Growth (Lenny's Newsletter)](https://www.lennysnewsletter.com/p/how-duolingo-reignited-user-growth)
- [OATutor GitHub](https://github.com/CAHLR/OATutor)
- [OATutor Paper (CHI 2023)](https://dl.acm.org/doi/10.1145/3544548.3581574)
- [pyBKT GitHub](https://github.com/CAHLR/pyBKT)
- [pyBKT Introduction (MDPI 2023)](https://www.mdpi.com/2624-8611/5/3/50)
- [ALEKS Knowledge Space Theory](https://www.aleks.com/about_aleks/knowledge_space_theory)
- [ALEKS Practical KST Perspective (ScienceDirect)](https://www.sciencedirect.com/science/article/abs/pii/S0022249621000134)
- [Deep Knowledge Tracing + Cognitive Load (Nature 2025)](https://www.nature.com/articles/s41598-025-10497-x)
- [Duolingo Tech Stack (Stackshare)](https://stackshare.io/duolingo/duolingo)
- [Intelligent Tutoring System Architecture Overview](https://cseweb.ucsd.edu/~zzhai/blog/intelligent-tutoring-system-overview.html)
