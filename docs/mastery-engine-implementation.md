# Mastery Engine — Implementation Summary

> **Status:** Implemented (15/18 tasks)
> **Last updated:** 2026-03-27
> **Namespace:** `Cena.Actors.Mastery`, `Cena.Actors.Simulation`, `Cena.Actors.Api`
> **Tests:** 507 passing (158 mastery-specific)
> **Research basis:** `docs/mastery-measurement-research.md`, `docs/mastery-engine-architecture.md`

---

## 1. What Was Built

The Mastery Engine is the computational core of the Learner Context. It tracks what each student knows, when they'll forget it, and what they should learn next. All domain logic is pure computation (zero I/O on the hot path), wired additively into the existing `StudentActor` event-sourced aggregate.

---

## 2. Completed Tasks

| Task | Name | Files | Effort |
|------|------|-------|--------|
| MST-001 | ConceptMasteryState value object | 3 | S |
| MST-002 | BKT engine (zero-alloc hot path) | 2 | M |
| MST-003 | HLR decay engine (Settles & Meeder 2016) | 3 | M |
| MST-004 | Prerequisite support calculator | 2 | M |
| MST-005 | Effective mastery compositor + pipeline | 2 | S |
| MST-006 | StudentActor mastery handler (additive wiring) | 4 | L |
| MST-007 | Decay timer config + scanner | 2 | M |
| MST-008 | Review priority calculator | 1 | M |
| MST-009 | Learning frontier + PSI | 2 | M |
| MST-010 | Elo item selector + interleaving | 3 | L |
| MST-011 | Scaffolding level determiner | 2 | S |
| MST-012 | Quality matrix classifier | 2 | S |
| MST-013 | KST onboarding diagnostic | 4 | L |
| MST-014 | Initial state populator | 1 | S |
| MST-017 | Mastery REST API (adapted from GraphQL) | 4 | M |

### Remaining (Phase 2 — need production data)

| Task | Name | Reason |
|------|------|--------|
| MST-015 | BKT parameter trainer (Python) | Needs production attempt data to calibrate per-KC parameters |
| MST-016 | HLR weight trainer (Python) | Needs production data for regression weight optimization |
| MST-018 | MIRT estimator (Python) | Needs 10K+ users for multidimensional IRT |

---

## 3. Architecture

### 3.1 Data Flow (Per Attempt)

```
AttemptConcept command
       │
       ▼
┌─── StudentActor.HandleAttemptConcept ───┐
│                                          │
│  1. IBktService.Update()  ◄── existing   │
│  2. Stage ConceptAttempted_V1 event      │
│  3. FlushEvents() → Marten              │
│  4. _state.Apply(event)                  │
│     └── ApplyMasteryOverlay()            │
│         ├── WithAttempt() (counters)     │
│         ├── WithBktUpdate() (mastery)    │
│         ├── WithRecentError() (errors)   │
│         ├── Classify quality quadrant    │
│         └── Update response baseline     │
│  5. EnrichMasteryAfterAttempt()  ◄── new │
│     ├── Compute HLR half-life            │
│     ├── Update HlrTimers                 │
│     └── Detect stagnation                │
└──────────────────────────────────────────┘
```

### 3.2 Key Design Decision: Additive, Not Replacement

The existing `IBktService` flow is **untouched**. The new mastery pipeline runs alongside it:

- `MasteryMap` (existing): `Dictionary<string, double>` — simple P(known) per concept
- `MasteryOverlay` (new): `Dictionary<string, ConceptMasteryState>` — rich per-concept state with BKT probability, HLR half-life, Bloom's level, error history, quality quadrant, FSRS fields, method tracking

Both are maintained in parallel. The overlay is populated during event replay via `ApplyMasteryOverlay()` for deterministic reconstruction.

### 3.3 Module Map

```
Cena.Actors/
├── Mastery/                     # Domain computation (pure, no I/O)
│   ├── ConceptMasteryState.cs   # MST-001: Immutable record, 14 fields, 6 With-methods
│   ├── MasteryEnums.cs          # MasteryQuality, ErrorType, MasteryLevel, thresholds
│   ├── BktParameters.cs         # MST-002: P_L0, P_T, P_S, P_G with validation
│   ├── BktTracer.cs             # MST-002: Zero-alloc BKT update rule
│   ├── HlrFeatures.cs           # MST-003: Stack-allocated feature vector
│   ├── HlrWeights.cs            # MST-003: Weight vector with dot product
│   ├── HlrCalculator.cs         # MST-003: h = 2^(θ·x + bias), recall, scheduling
│   ├── IConceptGraphCache.cs    # MST-004: Graph interface + prerequisite edge
│   ├── PrerequisiteCalculator.cs # MST-004: min(mastery) + weighted penalty
│   ├── EffectiveMasteryCalculator.cs # MST-005: min(P(L), recall) × prereq_support
│   ├── MasteryPipeline.cs       # MST-005: Full BKT → HLR → effective → threshold
│   ├── MasteryDecayScanner.cs   # MST-007: Scan overlay for decaying concepts
│   ├── DecayTimerConfig.cs      # MST-007: Configurable scan parameters
│   ├── ReviewPriorityCalculator.cs # MST-008: (0.85 - recall) × (1 + log2(descendants))
│   ├── PrerequisiteSatisfactionIndex.cs # MST-009: avg(mastery(prereqs))
│   ├── LearningFrontierCalculator.cs # MST-009: PSI ≥ 0.8 ∧ mastery < 0.9
│   ├── EloScoring.cs            # MST-010: Expected correctness + dual update
│   ├── ItemSelector.cs          # MST-010: 85% rule + interleaving
│   ├── ScaffoldingService.cs    # MST-011: Mastery → Full/Partial/Hints/None
│   ├── MasteryQualityClassifier.cs # MST-012: (fast/slow) × (correct/incorrect)
│   ├── ResponseTimeBaseline.cs  # MST-012: Circular buffer median
│   ├── MasteryStagnationDetector.cs # MST-006: 3+ repeated error detection
│   ├── IBktParameterProvider.cs # MST-006: DI interface for per-KC params
│   ├── IHlrWeightProvider.cs    # MST-006: DI interface for HLR weights
│   ├── KnowledgeState.cs        # MST-013: Immutable concept set
│   ├── KnowledgeStateSpace.cs   # MST-013: Feasible state enumeration/sampling
│   ├── DiagnosticEngine.cs      # MST-013: KST adaptive question selection
│   ├── DiagnosticResult.cs      # MST-013: MAP estimate result
│   └── InitialStatePopulator.cs # MST-014: Diagnostic → mastery state conversion
├── Api/                         # REST API layer
│   ├── MasteryApiDtos.cs        # MST-017: Response types
│   ├── MasteryApiService.cs     # MST-017: Pure transformation service
│   └── MasteryEndpoints.cs      # MST-017: Minimal API endpoints
├── Simulation/                  # Test data generation
│   ├── CurriculumSeedData.cs    # 41 concepts, 7 clusters, Bagrut structure
│   ├── StudentArchetypes.cs     # 6 archetypes with statistical profiles
│   └── MasterySimulator.cs      # Full pipeline simulation engine
└── Students/
    ├── StudentActor.Mastery.cs  # MST-006: Enrichment + decay scan partial
    ├── StudentState.cs          # MST-006: MasteryOverlay + ResponseBaseline
    └── StudentMessages.cs       # MST-017: GetMasteryOverlayQuery
```

---

## 4. REST API Endpoints

Base path: `/api/v1/mastery`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/{studentId}?subject=` | Full mastery overlay, recall + effective at request time |
| GET | `/{studentId}/topics/{clusterId}` | Aggregated topic progress |
| GET | `/{studentId}/frontier?maxResults=` | Learning frontier (next concepts to learn) |
| GET | `/{studentId}/decay-alerts` | Concepts needing review, priority-sorted |
| GET | `/{studentId}/review-schedule?maxItems=` | Spaced repetition schedule |

---

## 5. Simulation Data

### 5.1 Curriculum Graph

41 concepts across 7 topic clusters, modeled on the Israeli Bagrut 5-unit math syllabus:

| Cluster | Concepts | Depth Range |
|---------|----------|-------------|
| Algebra | 8 | 1–3 |
| Functions | 7 | 1–3 |
| Geometry | 7 | 1–3 |
| Trigonometry | 4 | 2–3 |
| Calculus | 6 | 2–3 |
| Probability | 6 | 1–3 |
| Vectors | 4 | 2–3 |

40+ prerequisite edges with realistic pedagogical ordering.

### 5.2 Student Archetypes

| Archetype | Accuracy | RT (ms) | Error Dominant | Retention | Study Rate |
|-----------|----------|---------|----------------|-----------|------------|
| HighAchiever | μ=0.90, σ=0.05 | μ=8K | Careless | 1.5× | 85% |
| SteadyLearner | μ=0.70, σ=0.10 | μ=15K | Balanced | 1.0× | 60% |
| Struggling | μ=0.40, σ=0.12 | μ=25K | Conceptual | 0.6× | 30% |
| FastCareless | μ=0.65, σ=0.15 | μ=6K | Careless | 1.1× | 50% |
| SlowThorough | μ=0.80, σ=0.08 | μ=30K | Transfer | 1.3× | 70% |
| Inconsistent | μ=0.60, σ=0.20 | μ=14K | Mixed | 0.7× | 25% |

### 5.3 Statistical Distributions

- **Accuracy:** Beta distribution, concept-difficulty modulated (harder → lower P(correct))
- **Response time:** Log-normal distribution (realistic right-skew: most moderate, some very slow)
- **Error types:** Weighted categorical per archetype
- **Study attendance:** Bernoulli per day, archetype-specific rate, Shabbat excluded

### 5.4 Usage

```csharp
// Generate a 30-student cohort with 60 days of history
var cohort = MasterySimulator.GenerateCohort(
    studentsPerArchetype: 5,
    simulationDays: 60,
    seed: 42);  // deterministic for reproducibility

// Each student has:
// - MasteryOverlay: per-concept rich state
// - AttemptHistory: timestamped attempts with mastery progression
// - ResponseBaseline: personal median response time
// - EloTheta: calibrated ability rating
// - TotalSessions, StudyStreakDays
```

---

## 6. Key Formulas

| Formula | Source | Used In |
|---------|--------|---------|
| `P(L\|correct) = (1-P_S)·P_L / [(1-P_S)·P_L + P_G·(1-P_L)]` | BKT (Corbett & Anderson 1994) | MST-002 |
| `h = 2^(θ·x + bias)` | HLR (Settles & Meeder 2016) | MST-003 |
| `p(t) = 2^(-Δt/h)` | Exponential forgetting | MST-003 |
| `effective = min(P(L), recall) × prereq_support` | Composite signal | MST-005 |
| `priority = (0.85 - recall) × (1 + log2(descendants))` | Review urgency | MST-008 |
| `PSI = avg(mastery(p) for p in prerequisites)` | Prerequisite readiness | MST-009 |
| `P(correct) = 1 / (1 + 10^((d-θ)/400))` | Elo expected score | MST-010 |
| `entropy_split → max information concept` | KST adaptive testing | MST-013 |

---

## 7. Performance Characteristics

- **BKT update:** Zero heap allocation, < 1μs per call
- **HLR half-life:** Stack-allocated features, < 1μs
- **Effective mastery:** Pure arithmetic, < 1μs
- **Decay scan (200 concepts):** < 1ms total
- **Learning frontier (200 concepts):** < 5ms
- **Event replay (1000 events):** < 100ms for mastery overlay reconstruction
- **All simulation tests (30 students × 60 days):** < 200ms
