# Mastery Engine Architecture — Implementation Specification

> **Status:** Ready for implementation
> **Date:** 2026-03-26
> **Source:** Synthesized from `architecture-design.md` + `mastery-measurement-research.md`
> **Scope:** Everything needed to build the mastery tracking, decay, scoring, and visualization pipeline

---

## 1. Overview

The Mastery Engine is the subsystem that answers three questions for every student × concept pair:

1. **How well does this student know this concept right now?** → `effective_mastery(c)`
2. **When will they forget it?** → `recall_probability(c)`
3. **What should they learn next?** → `learning_frontier`

It lives inside the **Learner Context** (core domain), orchestrated by the `StudentActor` (Proto.Actor virtual actor, event-sourced on PostgreSQL/Marten), with graph structure served from **Neo4j AuraDB** via an in-memory cache.

---

## 2. Data Model

### 2.1 Per-Concept Mastery State (on StudentActor)

Every concept the student has interacted with gets this state, stored as part of the event-sourced `StudentActor` snapshot:

```csharp
public sealed record ConceptMasteryState
{
    // === Core mastery signals ===
    public float MasteryProbability { get; init; }    // 0.0–1.0, from BKT (Phase 1) / MIRT (Phase 2)
    public float HalfLifeHours { get; init; }         // HLR: memory half-life for spaced repetition
    public DateTimeOffset LastInteraction { get; init; }
    public DateTimeOffset FirstEncounter { get; init; }

    // === Performance counters ===
    public int AttemptCount { get; init; }
    public int CorrectCount { get; init; }
    public int CurrentStreak { get; init; }           // consecutive correct (resets on incorrect)

    // === Qualitative signals ===
    public int BloomLevel { get; init; }              // 0–6, highest demonstrated Bloom's level
    public float SelfConfidence { get; init; }        // 0.0–1.0, student self-assessment
    public ErrorType[] RecentErrors { get; init; }    // last 10 error classifications
    public MasteryQuality QualityQuadrant { get; init; } // Fast+Correct, Slow+Correct, Fast+Wrong, Slow+Wrong

    // === Spaced repetition (FSRS-compatible) ===
    public float Stability { get; init; }             // FSRS S parameter (days until 90% recall)
    public float Difficulty { get; init; }             // FSRS D parameter (0–10 scale)

    // === Method tracking ===
    public MethodAttempt[] MethodHistory { get; init; } // (methodology, sessions, outcome) per approach tried

    // === Computed (not persisted — derived at read time) ===
    // RecallProbability = 2^(-deltaHours / HalfLifeHours)     // HLR
    //   or:  (1 + deltaDays / (9 * Stability))^(-1)           // FSRS
    // EffectiveMastery = min(MasteryProbability, RecallProbability) * PrerequisiteSupport
}

public enum MasteryQuality { Mastered, Effortful, Careless, Struggling }

public enum ErrorType { Procedural, Conceptual, Careless, Systematic, Transfer }

public sealed record MethodAttempt(string MethodologyId, int SessionCount, string Outcome);
```

### 2.2 Mastery Thresholds

| State | Range | Visualization | Action |
|-------|-------|---------------|--------|
| Not Started | `mastery < 0.10` | Gray node | In learning frontier if prereqs met |
| Introduced | `0.10 ≤ mastery < 0.40` | Light blue | Continue current methodology |
| Developing | `0.40 ≤ mastery < 0.70` | Yellow | Increase difficulty, check Bloom's level |
| Proficient | `0.70 ≤ mastery < 0.90` | Light green | Reduce scaffolding, attempt higher Bloom's |
| Mastered | `mastery ≥ 0.90` | Green | Move to spaced review schedule |
| Decaying | `mastered but recall < 0.70` | Orange pulse | Schedule review via Outreach |
| Blocked | `PSI < 0.60` | Red outline | Fix prerequisites first |

### 2.3 Neo4j Concept Graph Schema

```cypher
// Concept nodes (shared, immutable at runtime)
(:Concept {
  id: String,
  name: String,
  name_he: String,
  subject: String,          // "math", "physics", "chemistry"
  topic_cluster: String,    // "calculus", "mechanics", "organic"
  depth_level: Int,         // 1=foundational, 2=intermediate, 3=advanced
  intrinsic_load: Float,    // cognitive load rating (calibrated from aggregate data)
  bagrut_weight: Float,     // exam frequency weight (0–1)
  bloom_max: Int            // highest Bloom's level assessable for this concept
})

// Edges
(:Concept)-[:PREREQUISITE {strength: Float}]->(:Concept)
(:Concept)-[:RELATED_TO {type: "analogy"|"contrast"|"builds_on"}]->(:Concept)
(:Concept)-[:IN_CLUSTER]->(:TopicCluster {id, name, subject})

// Items (assessment questions)
(:Item {
  id: String,
  bloom_level: Int,         // 1–6
  difficulty_elo: Float,    // Elo-calibrated difficulty
  discrimination: Float     // 2PL IRT discrimination (optional, Phase 2)
})-[:ASSESSES]->(:Concept)

// Misconceptions
(:Misconception {
  id: String,
  description: String,
  subject: String
})-[:BLOCKS]->(:Concept)

// MCM mapping (methodology selection)
(:ErrorType)-[:MCM_MAPS {confidence: Float}]->(:Methodology)
```

---

## 3. Computation Pipeline

### 3.1 Phase 1 (Launch): BKT + HLR

On every `ConceptAttempted` event, the `StudentActor` runs:

```
1. BKT UPDATE
   if correct:
     P(L) = (1-P(S)) * P(L) / [(1-P(S))*P(L) + P(G)*(1-P(L))]
   else:
     P(L) = P(S) * P(L) / [P(S)*P(L) + (1-P(G))*(1-P(L))]
   P(L_next) = P(L|obs) + (1 - P(L|obs)) * P(T)

2. HLR UPDATE
   h_new = 2^(θ^T · x)
   where x = [attempt_count, correct_count, concept_difficulty,
              prerequisite_depth, bloom_level, days_since_first]

3. RECALL PROBABILITY (continuous, time-based)
   p_recall = 2^(-Δt / h)

4. EFFECTIVE MASTERY
   prereq_support = min(effective_mastery(p) for p in prerequisites)
                    // from in-memory graph cache
   effective_mastery = min(P(L), p_recall) * prereq_support

5. EMIT EVENTS
   if effective_mastery crossed 0.90 upward → emit ConceptMastered
   if p_recall dropped below 0.70          → emit MasteryDecayed
   if error_pattern shows 3+ same type     → emit StagnationDetected
```

### 3.2 Phase 2 (Scale): MIRT Integration

When interaction data volume justifies (target: 10K+ students):

```
1. OFFLINE: Train MIRT model (20–50 dimensions per subject)
   - Q-matrix derived from Neo4j: MATCH (i:Item)-[:ASSESSES]->(c:Concept)
   - Estimation: Python mirt/mirtjml on Analytics read replica
   - Output: θ vector per student per subject

2. PUBLISH θ via NATS JetStream (learner.mirt.updated)

3. StudentActor RECEIVES θ update
   - Uses θ as Bayesian prior for BKT: P(L_0) = sigmoid(θ_k) for cluster k
   - Confidence interval from MIRT Fisher information
   - ConceptMastered fires only if CI lower bound > 0.90

4. COMPOSITE SCORING replaces min(BKT, HLR):
   composite = 0.35·mirt_θ + 0.25·p_recall + 0.15·accuracy_10
             + 0.10·bloom/6 + 0.05·latency_score
             + 0.05·error_absence + 0.05·calibration
   effective = composite × prereq_support
```

### 3.3 Phase 3 (Optimization): Data-Driven Weights

Train a meta-model (XGBoost/LightGBM) on Phase 2 data to optimize composite weights. A/B test composite vs. simple BKT, measured against Bagrut exam correlation.

---

## 4. Decay & Review Scheduling

### 4.1 Decay Detection (In-Actor Timer)

The `StudentActor` uses Proto.Actor's `ReceiveTimeout` to periodically evaluate decay:

```csharp
// Every 6 hours (configurable), check all mastered concepts
foreach (var (conceptId, state) in _masteryOverlay)
{
    var delta = DateTimeOffset.UtcNow - state.LastInteraction;
    var recall = Math.Pow(2, -delta.TotalHours / state.HalfLifeHours);

    if (recall < 0.70 && state.MasteryProbability >= 0.70)
    {
        Emit(new MasteryDecayed(conceptId, recall));
        // → NATS → Outreach Context → "Review quadratic equations!"
    }
}
```

### 4.2 Review Priority Formula

```
review_priority(c) = (0.85 - p_recall(c)) × (1 + log₂(descendant_count(c)))
```

Foundational concepts with many dependents are prioritized even for small decay.

### 4.3 Decay Propagation Through Prerequisites

**Phase 1:** Weighted prerequisite penalty (simple, graduated):

```
effective_mastery(c) = measured_mastery(c) × ∏(max(mastery(p)/0.85, 1.0))
                       for p in prerequisites(c)
```

**Phase 2+:** Bayesian network propagation — when a prerequisite's mastery changes, belief propagation updates all descendant distributions.

---

## 5. Item Selection Algorithm

The `LearningSessionActor` (Pedagogy Context) selects the next item using:

```
1. COMPUTE learning frontier
   frontier = {c : PSI(c) ≥ 0.8 AND mastery(c) < 0.90}

2. RANK frontier concepts by:
   - Information gain: prefer concepts with widest confidence intervals
   - Review urgency: review_priority(c) for decaying concepts
   - Prerequisite readiness: PSI(c)
   - Interleaving: different concept from last item with probability 0.5

3. SELECT item from chosen concept:
   - Target P(correct) ≈ 0.85 (the "85% rule" for desirable difficulty)
   - Use Elo-predicted correctness: E = 1 / (1 + 10^((D_item - θ_student) / 400))
   - Pick item where |E - 0.85| is minimized

4. CALIBRATE scaffolding level:
   mastery < 0.20 AND PSI < 0.80 → "full scaffolding" (worked example)
   mastery < 0.40 AND PSI ≥ 0.80 → "partial scaffolding" (faded example)
   mastery < 0.70                 → "hints on request"
   mastery ≥ 0.70                 → "no scaffolding" (independent practice)
```

---

## 6. Neo4j Graph Queries

### 6.1 Prerequisite Support

```cypher
MATCH (c:Concept {id: $conceptId})<-[:PREREQUISITE]-(p:Concept)
WITH c, collect(p.id) AS prereqIds
UNWIND prereqIds AS pid
// mastery values come from the actor's in-memory overlay, not Neo4j
RETURN c.id, prereqIds
```

The actual `min(mastery(p))` computation happens in-actor using the cached graph + overlay.

### 6.2 Learning Frontier

```cypher
MATCH (c:Concept)
WHERE c.subject = $subject
OPTIONAL MATCH (c)<-[:PREREQUISITE]-(p:Concept)
WITH c, collect(p.id) AS prereqIds
RETURN c.id, c.name, c.topic_cluster, c.intrinsic_load, prereqIds
```

Loaded once at actor startup. Frontier computation runs in-memory:

```csharp
var frontier = _graphCache.Concepts
    .Where(c => PrerequisiteSatisfaction(c) >= 0.8)
    .Where(c => GetMastery(c.Id).EffectiveMastery < 0.90)
    .OrderByDescending(c => InformationGain(c))
    .ToList();
```

### 6.3 Cluster Mastery

```cypher
MATCH (c:Concept)-[:IN_CLUSTER]->(t:TopicCluster {id: $clusterId})
RETURN t.id, t.name, collect(c.id) AS conceptIds, count(c) AS conceptCount
```

Topic mastery = weighted average of effective_mastery for all concepts in cluster, weighted by `bagrut_weight`.

### 6.4 Decay-Risk Concepts

```cypher
MATCH (c:Concept)<-[:PREREQUISITE*1..3]-(dependent:Concept)
WITH c, count(dependent) AS dependentCount
WHERE dependentCount > 3
RETURN c.id, c.name, dependentCount
ORDER BY dependentCount DESC
```

High-dependent-count concepts are flagged for aggressive review scheduling.

---

## 7. Visualization Specification

### 7.1 Knowledge Graph View (Student-Facing)

| Property | Mapping |
|----------|---------|
| Node color | See mastery thresholds table (gray → blue → yellow → green → orange) |
| Node size | `base + importance_score × scale` where importance = PageRank or bagrut_weight |
| Node glow | Pulse animation on mastery threshold crossing |
| Edge style | Solid (prereq mastered), dashed (developing), dotted (not started) |
| Edge color | Gradient from source mastery color to target mastery color |
| Edge width | Proportional to prerequisite strength |

### 7.2 Student Dashboard Widgets

1. **Progress Ring**: `% of [topic] concepts mastered` — per topic cluster
2. **Heat Map**: Time since last review per concept (green=recent, red=stale)
3. **Frontier Display**: "N new concepts ready to learn" — count of learning frontier
4. **Decay Alert**: "N concepts need review this week" — p_recall < 0.85
5. **Streak Counter**: "N-day streak, M concepts strengthened"
6. **Leitner Box View**: Map HLR half-life to 5 visual "boxes" for student comprehension

### 7.3 Teacher Dashboard Widgets

1. **Class Heat Map**: Which concepts are weakest across the class
2. **Distribution Histogram**: Mastery level distribution per concept
3. **Stagnation Alerts**: Students who have plateaued (3+ consecutive stagnation signals)
4. **Prerequisite Gaps**: Common foundational gaps in the class (concepts where >30% of students are below proficient)

---

## 8. Event Flow Summary

```
Student answers a question
    │
    ▼
Mobile/Web app sends AttemptConcept command via SignalR
    │
    ▼
StudentActor (Proto.Actor virtual actor)
    ├── Validates: is this concept in the student's frontier?
    ├── Updates BKT state (P(L) update)
    ├── Updates HLR half-life
    ├── Updates performance counters (accuracy, streak, error type)
    ├── Computes effective_mastery with prerequisite propagation
    ├── Persists ConceptAttempted event → PostgreSQL/Marten
    └── Publishes to NATS JetStream
         │
         ├── → Pedagogy Context (LearningSessionActor)
         │    └── Selects next item, checks stagnation, adjusts scaffolding
         │
         ├── → Engagement Context
         │    └── Awards XP, checks badge conditions, updates streak
         │
         ├── → Analytics Context
         │    └── Projects to teacher/parent dashboards
         │
         └── → Outreach Context (if MasteryDecayed or StagnationDetected)
              └── Schedules WhatsApp/push review reminder

StudentActor ReceiveTimeout (every 6 hours)
    ├── Scans all mastered concepts for recall decay
    ├── Emits MasteryDecayed for any p_recall < 0.70
    └── Publishes review priority list to NATS
```

---

## 9. Onboarding Diagnostic Pipeline

### 9.1 Knowledge Space Theory (KST) Assessment

During onboarding (Step 2 of signup flow), a 10–15 question adaptive diagnostic:

```
1. Initialize: uniform prior over all feasible knowledge states
2. For each question:
   a. Select the most informative concept to test
      (concept that maximally splits the posterior over knowledge states)
   b. Present one item from that concept
   c. Update posterior: P(K|response) ∝ P(response|K) × P(K)
      - Correct → increase weight on states containing this concept
      - Incorrect → increase weight on states NOT containing this concept
      - Skip → treat as weak signal of gap (0.7× incorrect weight)
3. After 10–15 questions:
   a. MAP estimate of knowledge state
   b. Populate initial mastery overlay on StudentActor
   c. Reveal knowledge graph with green (mastered), gray (gap) nodes
```

### 9.2 Initial State Population

```csharp
foreach (var conceptId in diagnosticResult.MasteredConcepts)
{
    _masteryOverlay[conceptId] = new ConceptMasteryState
    {
        MasteryProbability = 0.85f,  // confident but not certain
        HalfLifeHours = 168f,        // 1 week default half-life
        BloomLevel = 3,              // assumed Apply level from diagnostic
        AttemptCount = 1,
        CorrectCount = 1,
    };
}
// Gaps remain at default (mastery = 0.0)
```

---

## 10. Stagnation Detection & Methodology Switching

### 10.1 Stagnation Composite Score

The `StagnationDetectorActor` (child of StudentActor) computes a sliding-window composite:

| Signal | Weight | Threshold | Window |
|--------|--------|-----------|--------|
| Accuracy plateau | 0.30 | <5% improvement over 10 attempts | Per concept cluster |
| Response time drift | 0.20 | >20% increase vs. student baseline | Last 3 sessions |
| Session abandonment | 0.20 | >30% shorter than average session | Last 3 sessions |
| Error type repetition | 0.20 | Same error 3+ times | Cross-session |
| Annotation sentiment | 0.10 | Frustration/confusion detected | Last 5 annotations |

`StagnationDetected` fires when composite > 0.7 for 3 consecutive sessions.

### 10.2 Methodology Switch Algorithm

```
1. Classify dominant error type from last 3 sessions
   Precedence: conceptual > procedural > motivational

2. Query MCM graph: MCM_LOOKUP(error_type, concept_category)
   → Returns [(methodology, confidence)] sorted by confidence

3. Filter out methods in method_attempt_history for this cluster
   (methods tried in last 3 stagnation cycles)

4. Select first remaining candidate with confidence > 0.5
   If none: use first remaining regardless

5. If ALL methods exhausted (8 × 3 = 24+ sessions of stagnation):
   → Flag as "mentor-resistant"
   → Suggest skip to related concept or human tutoring
   → Log escalation for MCM improvement

6. Apply 3-session cooldown before re-evaluating
```

---

## 11. Technology Summary

| Component | Technology | Role in Mastery Engine |
|-----------|-----------|----------------------|
| StudentActor | Proto.Actor (.NET 9) virtual actor | Owns all mastery state, runs BKT/HLR, emits events |
| LearningSessionActor | Proto.Actor (.NET 9) classic actor | Item selection, scaffolding, stagnation detection |
| Event Store | PostgreSQL + Marten 7.x | Persists ConceptAttempted, ConceptMastered, MasteryDecayed, etc. |
| Graph Store | Neo4j AuraDB | Concept graph, prerequisite structure, MCM mappings |
| Graph Cache | In-memory (.NET) | Hot-path graph lookups, loaded at actor startup |
| MIRT Trainer | Python (mirt/mirtjml) | Offline batch estimation on Analytics read replica |
| HLR Trainer | Python (scikit-learn) | Offline logistic regression for half-life weights |
| Event Bus | NATS JetStream | Cross-context event distribution |
| Client Push | SignalR WebSocket | Real-time mastery updates to knowledge graph UI |
| Outreach | WhatsApp/Telegram/FCM | Decay review reminders, stagnation intervention |

---

## 12. Migration Path

| Phase | Trigger | Changes |
|-------|---------|---------|
| **Phase 1: BKT+HLR** | Launch | `min(BKT, HLR_recall) × prereq_support`. Qualitative signals drive methodology switching only, not mastery score. |
| **Phase 2: MIRT+Composite** | 10K students | 7-signal weighted composite. MIRT θ replaces BKT as primary. Confidence intervals on all estimates. |
| **Phase 3: Data-Driven** | 50K students | Meta-model learns optimal weights. A/B test against Phase 2. FSRS evaluated as HLR replacement. |
