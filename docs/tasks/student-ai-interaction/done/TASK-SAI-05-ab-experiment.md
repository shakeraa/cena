# TASK-SAI-05: A/B Experiment Wiring for Tier 1-2 Validation

**Priority**: MEDIUM — validates that the interventions actually improve learning
**Effort**: 1-2 days
**Depends on**: TASK-SAI-02 (hints), TASK-SAI-03 (L2 explanations)
**Track**: D (after Tracks B + C)

---

## Context

A full A/B framework already exists and is operational:

- `FocusExperimentConfig` with 6 predefined experiments (microbreaks, boredom-fatigue split, confusion patience, peak time adaptation, solution diversity, sensor-enhanced)
- `FocusExperimentCollector` captures per-student per-session metrics
- Hash-based deterministic arm assignment (student always in same arm)
- Export to CSV/Parquet for offline analysis

This task adds **two new experiments** to the existing framework to validate Tier 1 (explanations) and Tier 2 (hints).

---

## New Experiments

### Experiment: `explanation_quality`

| Arm | Behavior | Metric |
|-----|----------|--------|
| `control` | L1 static explanation only (or no explanation if none exists) | Baseline |
| `l2_cached` | L2 misconception-specific explanation | Test L2 value |
| `l3_personalized` | Full L3 personalized explanation | Test L3 marginal value over L2 |

### Experiment: `hint_progression`

| Arm | Behavior | Metric |
|-----|----------|--------|
| `control` | No hints (current behavior) | Baseline |
| `hints_no_bkt_adjust` | 3-level hints without BKT credit reduction | Isolate hint value |
| `hints_with_bkt_adjust` | 3-level hints with 1.0/0.7/0.4/0.1 credit curve | Full implementation |

### Metrics to Capture (Per Student Per Session)

- `mastery_velocity` — questions to reach mastery 0.85 on a concept
- `retention_7d` — mastery on same concept 7 days later
- `hint_usage_rate` — % of questions where hints were requested
- `explanation_view_time_ms` — how long student spent reading explanation
- `session_duration_minutes`
- `questions_per_session`
- `return_rate_next_day` — did student come back?

---

## Implementation

### Register Experiments

**Modify**: wherever `FocusExperimentConfig` experiments are defined.

Add:
```csharp
new ExperimentDefinition("explanation_quality", ["control", "l2_cached", "l3_personalized"]),
new ExperimentDefinition("hint_progression", ["control", "hints_no_bkt_adjust", "hints_with_bkt_adjust"])
```

### Wire Into ExplanationOrchestrator

**Modify**: `src/actors/Cena.Actors/Services/ExplanationOrchestrator.cs`

```csharp
var arm = _experimentCollector.GetArm(studentId, "explanation_quality");
return arm switch
{
    "control" => question.Explanation ?? fallback,
    "l2_cached" => await ResolveL2(req, ct) ?? question.Explanation ?? fallback,
    "l3_personalized" => await ResolveL3(req, ct),
    _ => await ResolveL3(req, ct)  // default to full
};
```

### Wire Into HintAdjustedBktService

**Modify**: `src/actors/Cena.Actors/Services/HintAdjustedBktService.cs`

```csharp
var arm = _experimentCollector.GetArm(studentId, "hint_progression");
return arm switch
{
    "control" => _bktService.Update(input),  // no hints, no adjustment
    "hints_no_bkt_adjust" => _bktService.Update(input),  // hints available but no credit reduction
    "hints_with_bkt_adjust" => UpdateWithHints(input, hintCountUsed),  // full implementation
    _ => UpdateWithHints(input, hintCountUsed)
};
```

### Wire Hint Availability Into LearningSessionActor

In the "control" arm of `hint_progression`, suppress hint delivery entirely (return `Delivered: false`).

### Emit Metrics

Use existing `FocusExperimentCollector` to log per-session:

```csharp
_experimentCollector.Record(studentId, sessionId, new Dictionary<string, double>
{
    ["mastery_velocity"] = questionsToMastery,
    ["hint_usage_rate"] = hintsUsed / questionsAttempted,
    ["explanation_view_time_ms"] = avgExplanationViewTime,
    ["session_duration_minutes"] = sessionMinutes,
    ["questions_per_session"] = questionsAttempted
});
```

---

## Coding Standards

- Experiment arm assignment must be **deterministic** (hash-based, already implemented). Same student always sees same arm.
- Never change a student's arm mid-experiment. The hash is computed once at session start.
- Metrics must be emitted AFTER session completes, not during (avoid partial data).
- This task adds no new infrastructure — it wires existing experiments + existing services.
- Write a unit test that verifies each arm produces the expected behavior (mock the experiment collector).

---

## Acceptance Criteria

1. Two new experiments registered: `explanation_quality` (3 arms) and `hint_progression` (3 arms)
2. Explanation resolution respects experiment arm (control gets L1 only, l2_cached gets L2, l3 gets full)
3. Hint progression respects experiment arm (control gets no hints, no_bkt_adjust gets hints without credit reduction)
4. Deterministic arm assignment (same student, same arm, always)
5. Per-session metrics captured and exportable via existing CSV/Parquet export
6. Minimum run: 2 weeks, 100 students per arm before drawing conclusions
