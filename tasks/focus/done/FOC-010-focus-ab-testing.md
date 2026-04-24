# FOC-010: Focus A/B Testing Framework

**Priority:** P2 — validates all focus engine research hypotheses with real users
**Blocked by:** FOC-003 (microbreaks), FOC-006 (boredom-fatigue)
**Estimated effort:** 3-5 days
**Contract:** Extends analytics infrastructure

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

The focus degradation research identified 8 design changes backed by research. All need empirical validation with Cena's specific population (Israeli 16-18 year olds, Hebrew/Arabic, Bagrut math).

## Subtasks

### FOC-010.1: Focus Experiment Configuration
**Files:**
- `src/Cena.Actors/Services/FocusExperimentConfig.cs` — NEW

**Acceptance:**
- [ ] `FocusExperiment` record: `ExperimentId`, `Name`, `Description`, `ControlBehavior`, `TreatmentBehavior`, `AssignmentPercentage`, `StartDate`, `EndDate`
- [ ] Student assignment: hash-based deterministic assignment (same student always in same arm)
- [ ] Support for multi-arm experiments (A/B/C)
- [ ] 6 predefined experiments ready to configure:

| Experiment | Control | Treatment | Primary Metric |
|-----------|---------|-----------|----------------|
| Microbreaks | Reactive breaks only | Proactive 90s/10min + reactive | Post-break accuracy |
| Boredom-Fatigue | Single Disengaged state | Split Bored/Exhausted with differentiated intervention | Return rate + next-session performance |
| Confusion Patience | Intervene at 3 wrong answers | Wait for confusion resolution (5-question window) | Delayed test score (1 week later) |
| Peak Time Adaptation | Fixed 15-min peak | Personalized chronotype-adjusted peak | False positive focus-degradation rate |
| Solution Diversity | Existing struggle classifier | + solution diversity signal | Productive struggle classification accuracy |
| Sensor-Enhanced | 4-signal model | 8-signal model (with sensors) | Focus state accuracy vs self-report |

### FOC-010.2: Experiment Metrics Collector
**Files:**
- `src/Cena.Actors/Services/FocusExperimentCollector.cs` — NEW

**Acceptance:**
- [ ] Collects per-student per-session: `focusStateAccuracy` (vs self-report), `breakEffectiveness` (post-break accuracy delta), `returnRate` (next-session return), `productiveStrugglePrecision`, `microbreakComplianceRate`
- [ ] Self-report prompt: post-session 1-question "How focused were you?" (1-5 scale, Hebrew/Arabic)
- [ ] All metrics tagged with experiment arm for later analysis
- [ ] Export to CSV/Parquet for offline statistical analysis

## Research References
- All research gaps from Focus Degradation Research doc, Section 5
- Focus Degradation Research doc, Section 7 (A/B test metrics)
