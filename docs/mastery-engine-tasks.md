# Mastery Engine — Implementation Tasks

> **Source:** `docs/mastery-engine-architecture.md`
> **Date:** 2026-03-26
> **Estimation unit:** T-shirt sizes (S=1-2 days, M=3-5 days, L=1-2 weeks, XL=2-3 weeks)

---

## Phase 1: Core Data Model & BKT (Launch Blocker)

### 1.1 ConceptMasteryState Value Object [S]
- **File:** `src/Learner/Domain/ConceptMasteryState.cs`
- **What:** Implement the `ConceptMasteryState` record from architecture section 2.1
- **Includes:** `MasteryProbability`, `HalfLifeHours`, `LastInteraction`, `AttemptCount`, `CorrectCount`, `CurrentStreak`, `BloomLevel`, `RecentErrors`, `QualityQuadrant`, `MethodHistory`
- **Computed properties:** `RecallProbability`, `EffectiveMastery` (read-only, derived)
- **Dependencies:** None (pure value object)
- **Acceptance:** Unit tests for all computed property calculations

### 1.2 BKT Engine [M]
- **File:** `src/Learner/Domain/Services/BayesianKnowledgeTracer.cs`
- **What:** Implement the BKT update rule (section 3.1 step 1)
- **Parameters:** `P(L_0)`, `P(T)`, `P(S)`, `P(G)` per knowledge component — loaded from config, later from Neo4j
- **Methods:** `Update(state, isCorrect) → ConceptMasteryState`
- **Default parameters:** P(L_0)=0.10, P(T)=0.20, P(S)=0.05, P(G)=0.25 (standard BKT defaults, tuned later with pyBKT)
- **Dependencies:** 1.1
- **Acceptance:** Unit tests with worked example from research doc (BKT full update cycle)

### 1.3 HLR Decay Engine [M]
- **File:** `src/Learner/Domain/Services/HalfLifeRegression.cs`
- **What:** Implement `p(recall) = 2^(-Δt/h)` and half-life update `h = 2^(θ·x)`
- **Feature vector:** attempt_count, correct_count, concept_difficulty, prerequisite_depth, bloom_level, days_since_first
- **Default θ weights:** Hand-tuned initial vector (trained later offline with Python)
- **Methods:** `ComputeRecall(state, now)`, `UpdateHalfLife(state, features) → float`
- **Dependencies:** 1.1
- **Acceptance:** Unit test: concept with h=168h at t=168h → p_recall = 0.50

### 1.4 Prerequisite Support Calculator [M]
- **File:** `src/Learner/Domain/Services/PrerequisiteCalculator.cs`
- **What:** Compute `prerequisite_support(c)` using in-memory graph cache
- **Algorithm:** `min(effective_mastery(p) for p in prerequisites(c))` with weighted penalty fallback
- **Input:** Graph cache (loaded from Neo4j at startup), student mastery overlay
- **Methods:** `ComputePrerequisiteSupport(conceptId, masteryOverlay, graphCache) → float`
- **Dependencies:** 1.1, 6.2 (graph cache)
- **Acceptance:** Test with 3-concept chain: A→B→C. If A decays, C's effective mastery drops.

### 1.5 Effective Mastery Computation [S]
- **File:** `src/Learner/Domain/Services/MasteryCalculator.cs`
- **What:** Combine BKT + HLR + prerequisites into `effective_mastery`
- **Formula:** `min(P(L), p_recall) × prereq_support`
- **Dependencies:** 1.2, 1.3, 1.4
- **Acceptance:** Integration test with full pipeline: attempt → BKT update → HLR update → prereq check → effective mastery

### 1.6 Domain Events [S]
- **Files:** `src/Learner/Domain/Events/ConceptAttempted.cs`, `ConceptMastered.cs`, `MasteryDecayed.cs`
- **What:** Define the three core mastery events as Marten-compatible event records
- **Fields:** StudentId, ConceptId, timestamp, plus event-specific data (response, score, error type, etc.)
- **Dependencies:** None
- **Acceptance:** Events serialize/deserialize correctly with Marten

### 1.7 StudentActor Mastery Handler [L]
- **File:** `src/Learner/Actors/StudentActor.Mastery.cs` (partial class)
- **What:** Handle `AttemptConcept` command → run full pipeline (section 3.1) → emit events
- **Flow:** Validate → BKT update → HLR update → effective mastery → check thresholds → emit ConceptAttempted → optionally emit ConceptMastered/MasteryDecayed → persist → publish to NATS
- **Dependencies:** 1.2, 1.3, 1.4, 1.5, 1.6
- **Acceptance:** End-to-end test: send AttemptConcept command → verify ConceptAttempted event persisted → verify NATS message published → verify actor state updated

### 1.8 Decay Timer [M]
- **File:** `src/Learner/Actors/StudentActor.DecayTimer.cs` (partial class)
- **What:** Use Proto.Actor's `ReceiveTimeout` to periodically scan mastered concepts for decay
- **Frequency:** Every 6 hours (configurable via actor state)
- **Logic:** For each mastered concept, compute `p_recall`. If < 0.70, emit `MasteryDecayed`
- **Dependencies:** 1.3, 1.7
- **Acceptance:** Test: fast-forward time 7 days → verify MasteryDecayed emitted for concept with h=168h

---

## Phase 2: Graph Infrastructure

### 2.1 Neo4j Concept Graph Schema [M]
- **Files:** Cypher migration scripts in `scripts/neo4j/`
- **What:** Create the concept graph schema from architecture section 2.3
- **Nodes:** Concept, TopicCluster, Item, Misconception, ErrorType, Methodology
- **Edges:** PREREQUISITE, RELATED_TO, IN_CLUSTER, ASSESSES, BLOCKS, MCM_MAPS
- **Seed data:** Import Bagrut Math 5-unit syllabus (~200 concepts for MVP)
- **Dependencies:** None (infrastructure task)
- **Acceptance:** Cypher queries from architecture section 6 execute successfully

### 2.2 Graph Cache Loader [M]
- **File:** `src/Learner/Infrastructure/GraphCacheLoader.cs`
- **What:** Load concept graph from Neo4j AuraDB into in-memory cache at silo startup
- **Cache structure:** Dictionary of concepts with prerequisite lists, topic clusters, difficulty ratings
- **Refresh:** Full reload on NATS `curriculum.graph.updated` event (rare — annual syllabus updates)
- **Dependencies:** 2.1
- **Acceptance:** Silo starts → graph cache populated → microsecond lookups verified

### 2.3 Learning Frontier Calculator [M]
- **File:** `src/Learner/Domain/Services/LearningFrontier.cs`
- **What:** Compute the set of concepts a student is ready to learn
- **Algorithm:** `{c : PSI(c) ≥ 0.8 AND mastery(c) < 0.90}`
- **Ranking:** Information gain > review urgency > prerequisite readiness > interleaving preference
- **Dependencies:** 1.4, 2.2
- **Acceptance:** Given a student with 5 mastered concepts, verify frontier returns the correct next candidates

---

## Phase 3: Item Selection & Session Logic

### 3.1 Elo Item Calibration [M]
- **File:** `src/Pedagogy/Domain/Services/EloItemCalibrator.cs`
- **What:** Maintain Elo ratings for items and students
- **Update rule:** Dual-update (student θ + item D) after each response
- **K-factor:** 40 for new students (decreasing to 10), 10 for items (decreasing with data)
- **Dependencies:** 1.6 (ConceptAttempted events)
- **Acceptance:** After 20 interactions, item difficulties converge within ±50 Elo of true difficulty

### 3.2 Item Selector [L]
- **File:** `src/Pedagogy/Domain/Services/ItemSelector.cs`
- **What:** Implement the full item selection algorithm (architecture section 5)
- **Steps:** Compute frontier → rank concepts → select item targeting P(correct) ≈ 0.85 → determine scaffolding level
- **Interleaving:** Different concept from last item with probability `interleaving_target` (default 0.5)
- **Dependencies:** 2.3, 3.1
- **Acceptance:** Given a student state and frontier, verify selected item is well-calibrated and not from the same concept as last item (with 50% probability)

### 3.3 Scaffolding Level Determiner [S]
- **File:** `src/Pedagogy/Domain/Services/ScaffoldingService.cs`
- **What:** Map mastery + PSI to scaffolding level (full → partial → hints → none)
- **Used by:** LLM prompt construction — the scaffolding level determines the system prompt variant
- **Dependencies:** 1.5 (effective mastery)
- **Acceptance:** Unit test: mastery=0.15, PSI=0.6 → full scaffolding; mastery=0.75 → no scaffolding

### 3.4 Mastery Quality Matrix Classifier [S]
- **File:** `src/Pedagogy/Domain/Services/MasteryQualityClassifier.cs`
- **What:** Classify each response into the 2×2 matrix: (fast/slow) × (correct/incorrect)
- **Thresholds:** "Fast" = response time < student's median for similar difficulty; "Slow" = above
- **Output:** `MasteryQuality` enum on `ConceptMasteryState`
- **Dependencies:** 1.1
- **Acceptance:** Unit tests for all 4 quadrants with realistic timing data

---

## Phase 4: Stagnation & Methodology Switching

### 4.1 Stagnation Detector [L]
- **File:** `src/Pedagogy/Actors/StagnationDetectorActor.cs`
- **What:** Sliding window composite score from 5 signals (architecture section 10.1)
- **Signals:** Accuracy plateau (0.30), response time drift (0.20), session abandonment (0.20), error repetition (0.20), annotation sentiment (0.10)
- **Trigger:** Emit `StagnationDetected` when composite > 0.7 for 3 consecutive sessions
- **Dependencies:** 1.6 (events), 1.7 (student actor)
- **Acceptance:** Simulated stagnation scenario → event fires after exactly 3 sessions above threshold

### 4.2 Error Classifier [M]
- **File:** `src/Delivery/Services/ErrorClassifier.cs`
- **What:** Classify student errors into ErrorType enum (procedural, conceptual, careless, systematic, transfer)
- **Implementation:** LLM call via Delivery Context ACL (Kimi K2.5 for structured, Claude Sonnet for open-ended)
- **Fallback:** Rule-based classifier for structured responses (MCQ distractor analysis)
- **Dependencies:** LLM ACL infrastructure
- **Acceptance:** Given 10 labeled error examples, classifier agrees with expert labels on 8+

### 4.3 MCM Methodology Lookup [M]
- **File:** `src/Curriculum/Domain/Services/McmLookupService.cs`
- **What:** Query Neo4j MCM graph for methodology candidates given (error_type, concept_category)
- **Filter:** Exclude methods already tried in last 3 stagnation cycles
- **Escalation:** Handle "all methods exhausted" case (flag as mentor-resistant)
- **Dependencies:** 2.1 (Neo4j schema), 4.2 (error classification)
- **Acceptance:** Given procedural error in algebra → returns drill first, then worked-example

### 4.4 Methodology Switch Handler [M]
- **File:** `src/Learner/Actors/StudentActor.MethodologySwitch.cs`
- **What:** On StagnationDetected → run MCM lookup → emit MethodologySwitched → update method history → apply 3-session cooldown
- **Dependencies:** 4.1, 4.3
- **Acceptance:** End-to-end: stagnation detected → methodology switches → cooldown prevents re-evaluation for 3 sessions

---

## Phase 5: Onboarding Diagnostic

### 5.1 KST State Space Builder [M]
- **File:** `src/Learner/Domain/Services/KnowledgeStateSpace.cs`
- **What:** Build feasible knowledge states from prerequisite graph (downward-closed subsets)
- **Optimization:** For ~200 concepts, use approximate posterior (top-K state tracking, not full enumeration)
- **Dependencies:** 2.2 (graph cache)
- **Acceptance:** Given 10-concept graph with known prerequisites, all feasible states are correctly enumerated

### 5.2 Adaptive Diagnostic Engine [L]
- **File:** `src/Pedagogy/Domain/Services/DiagnosticEngine.cs`
- **What:** Run the 10–15 question adaptive assessment (architecture section 9.1)
- **Algorithm:** Select most informative concept → present item → update posterior → repeat
- **Output:** MAP estimate of knowledge state + initial mastery overlay
- **Dependencies:** 5.1, 2.2
- **Acceptance:** Simulated student with known state → diagnostic correctly identifies 80%+ of mastered concepts in ≤15 questions

### 5.3 Initial State Populator [S]
- **File:** `src/Learner/Domain/Services/InitialStatePopulator.cs`
- **What:** Convert diagnostic result into initial ConceptMasteryState entries on StudentActor
- **Defaults:** mastery=0.85, half_life=168h, bloom=3 for diagnosed-mastered concepts; mastery=0.0 for gaps
- **Dependencies:** 5.2, 1.1
- **Acceptance:** After diagnostic, student's mastery overlay matches expected initial state

---

## Phase 6: Visualization & Client Integration

### 6.1 Mastery Graph API [M]
- **File:** `src/Api/GraphQL/MasterySchema.cs`
- **What:** GraphQL queries for knowledge graph visualization data
- **Queries:** `studentMastery(studentId, subject)`, `topicProgress(studentId, topicClusterId)`, `learningFrontier(studentId)`, `decayAlerts(studentId)`
- **Dependencies:** 1.7 (student actor state), 2.2 (graph cache)
- **Acceptance:** GraphQL queries return correct mastery overlays for test students

### 6.2 Knowledge Graph Visualization Component [L]
- **File:** `src/mobile/components/KnowledgeGraph/` (React Native)
- **What:** Interactive, zoomable knowledge graph with node coloring, edge styling, animations
- **Node colors:** Gray → Blue → Yellow → Green → Orange (per mastery thresholds table)
- **Animations:** Pulse on mastery threshold crossing, desaturation on decay
- **Library:** React Native SVG or d3-force layout
- **Dependencies:** 6.1 (GraphQL API)
- **Acceptance:** Visual test: 20-concept graph renders correctly with mastery colors; tap on node shows details

### 6.3 Student Dashboard Widgets [M]
- **Files:** `src/mobile/components/Dashboard/ProgressRing.tsx`, `HeatMap.tsx`, `FrontierDisplay.tsx`, `DecayAlert.tsx`, `StreakCounter.tsx`, `LeitnerBoxView.tsx`
- **What:** Build the 6 dashboard widgets from architecture section 7.2
- **Real-time:** Subscribe to SignalR for live mastery updates
- **Dependencies:** 6.1
- **Acceptance:** Each widget renders correctly with mock data; SignalR push updates reflect in real-time

### 6.4 Teacher Dashboard Widgets [M]
- **Files:** `src/web/components/TeacherDashboard/ClassHeatMap.tsx`, `DistributionHistogram.tsx`, `StagnationAlerts.tsx`, `PrerequisiteGaps.tsx`
- **What:** Build the 4 teacher dashboard widgets from architecture section 7.3
- **Data source:** Analytics Context CQRS projections via GraphQL
- **Dependencies:** 6.1
- **Acceptance:** Class of 30 simulated students → heat map shows correct concept-level gaps

---

## Phase 7: Offline Training Pipelines

### 7.1 BKT Parameter Trainer [M]
- **File:** `scripts/training/bkt_trainer.py`
- **What:** Train BKT parameters (P(L_0), P(T), P(S), P(G)) per knowledge component using pyBKT
- **Input:** ConceptAttempted events exported from PostgreSQL/Marten
- **Output:** JSON parameter file loaded by .NET actor cluster
- **Schedule:** Weekly batch job, outputs to S3 → loaded on next silo restart
- **Dependencies:** 1.6 (event data exists)
- **Acceptance:** Trained parameters improve next-response prediction AUC over defaults

### 7.2 HLR Weight Trainer [M]
- **File:** `scripts/training/hlr_trainer.py`
- **What:** Train HLR θ weights via logistic regression on review history
- **Input:** (concept, student, outcome, time_since_last_review, features) tuples from event store
- **Output:** θ weight vector loaded by .NET HLR engine
- **Schedule:** Monthly batch job
- **Dependencies:** 1.3 (HLR engine consumes weights)
- **Acceptance:** Trained model predicts recall probability with RMSE < 0.15 on held-out test set

### 7.3 MIRT Estimator [L] (Phase 2)
- **File:** `scripts/training/mirt_estimator.py`
- **What:** Estimate MIRT θ vectors (20–50 dimensions per subject)
- **Input:** Q-matrix from Neo4j + response data from event store
- **Output:** θ vector per student published to NATS (learner.mirt.updated)
- **Trigger:** 10K+ students with 50+ interactions each
- **Dependencies:** 2.1 (Neo4j Q-matrix), 7.1 (sufficient data)
- **Acceptance:** MIRT θ predicts next-response with AUC > 0.80

---

## Task Dependency Graph

```
Phase 1 (Core)           Phase 2 (Graph)        Phase 3 (Selection)
  1.1 ──┬── 1.2           2.1 ── 2.2             3.1
        ├── 1.3           2.2 ── 2.3             3.2 ←── 2.3, 3.1
        ├── 1.4 ←── 2.2                          3.3 ←── 1.5
        └── 1.5                                   3.4
   1.6                    Phase 4 (Stagnation)
   1.7 ←── 1.2-1.6       4.1 ←── 1.6, 1.7      Phase 5 (Onboarding)
   1.8 ←── 1.3, 1.7      4.2                     5.1 ←── 2.2
                          4.3 ←── 2.1, 4.2        5.2 ←── 5.1, 2.2
Phase 6 (Visualization)  4.4 ←── 4.1, 4.3        5.3 ←── 5.2, 1.1
  6.1 ←── 1.7, 2.2
  6.2 ←── 6.1            Phase 7 (Training)
  6.3 ←── 6.1            7.1 ←── 1.6 (data)
  6.4 ←── 6.1            7.2 ←── 1.3 (data)
                          7.3 ←── 2.1, 7.1 (data + scale)
```

---

## Implementation Order (Recommended)

| Sprint | Tasks | Delivers |
|--------|-------|----------|
| **Sprint 1** (Week 1-2) | 1.1, 1.2, 1.3, 1.6, 2.1 | Core value objects, BKT engine, HLR engine, domain events, Neo4j schema |
| **Sprint 2** (Week 3-4) | 1.4, 1.5, 1.7, 2.2, 2.3 | Prerequisite calculator, effective mastery, StudentActor handler, graph cache, learning frontier |
| **Sprint 3** (Week 5-6) | 1.8, 3.1, 3.2, 3.3, 3.4 | Decay timer, Elo calibration, item selector, scaffolding, quality matrix |
| **Sprint 4** (Week 7-8) | 4.1, 4.2, 4.3, 4.4, 5.1 | Stagnation detection, error classification, MCM lookup, methodology switching, KST state space |
| **Sprint 5** (Week 9-10) | 5.2, 5.3, 6.1, 7.1 | Diagnostic engine, initial state, GraphQL API, BKT trainer |
| **Sprint 6** (Week 11-12) | 6.2, 6.3, 6.4, 7.2 | Knowledge graph visualization, all dashboard widgets, HLR trainer |

**Total estimated duration:** 12 weeks (3 months) for Phase 1 feature-complete mastery engine.

Phase 2 (MIRT integration, task 7.3) triggers when 10K student milestone is reached.
