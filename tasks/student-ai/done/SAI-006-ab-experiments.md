# SAI-006: A/B Experiments for Tier 1-2 Validation

**Priority:** P2 — measures impact of SAI-001 through SAI-005
**Blocked by:** SAI-002 (hints), SAI-004 (explanations)
**Estimated effort:** 1-2 days
**Stack:** .NET 9, FocusExperimentConfig (existing framework)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The A/B testing framework is fully operational: `FocusExperimentConfig` with 6 predefined experiments, `FocusExperimentCollector` with per-session metrics collection, hash-based deterministic arm assignment, CSV/Parquet export. This task adds 3 new experiments to measure the sigma improvement of the Student AI Interaction features.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Services/FocusExperimentConfig.cs` | Experiment definitions — add new experiments here |
| `src/actors/Cena.Actors/Services/FocusExperimentCollector.cs` | Metrics collection — add new metric types |

## Subtasks

### SAI-006.1: Define Explanation Experiment

Add experiment: `adaptive_explanations`
- **Control**: L1 static explanation only (existing behavior post SAI-001)
- **Treatment**: L2+L3 personalized explanations (full SAI-003/004 pipeline)
- **Primary metric**: Next-concept mastery gain (BKT delta on related concept 1 week later)
- **Secondary metrics**: Session duration, return rate, self-reported understanding (1-5)
- **Assignment**: 50/50, hash-based on studentId

### SAI-006.2: Define Hint Progression Experiment

Add experiment: `hint_bkt_weighting`
- **Control**: Hints with no BKT credit adjustment (hints delivered, BKT unchanged)
- **Treatment**: Hints with credit curve (1.0/0.7/0.4/0.1 from SAI-002)
- **Primary metric**: Mastery retention at 1 week (recall probability via HLR)
- **Secondary**: Hint usage rate, time-to-mastery, hint dependency (hints/question trend)

### SAI-006.3: Define Confusion Gating Experiment

Extends existing "Confusion Patience" experiment:
- **Control**: Current ConfusionPatience experiment behavior
- **Treatment**: Full DeliveryGate integration (SAI-005) — confusion-aware + boredom-aware gating
- **Primary metric**: Delayed test score (1 week)
- **Secondary**: Confusion resolution rate, self-report focus

### SAI-006.4: Metric Collection Points

Add collection points in `LearningSessionActor` and `StudentActor`:
- `ExplanationSource` per answer (L1/L2/L3/none) — for explanation experiment
- `HintCreditAdjustment` per attempt (actual P_T multiplier used) — for hint experiment
- `DeliveryGateAction` per hint/explanation (deliver/defer/suppress) — for gating experiment

**Acceptance:**
- [ ] 3 new experiments registered in `FocusExperimentConfig`
- [ ] Hash-based assignment deterministic (same student always in same arm)
- [ ] Metrics collected per-session via `FocusExperimentCollector`
- [ ] No performance impact when experiments are disabled
- [ ] Export includes new metric types in CSV/Parquet
