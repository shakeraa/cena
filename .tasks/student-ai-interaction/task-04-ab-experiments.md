# Task 04: A/B Experiment Configuration for Tier 1-2 Validation

**Track**: D
**Effort**: 1-2 days
**Depends on**: Tasks 01b, 02, 03
**Blocks**: Nothing (enables measurement, not feature work)

---

## System Context

Cena is an event-sourced .NET educational platform with a full A/B testing framework already operational. `FocusDegradationService.cs` (615 lines) contains `FocusExperimentConfig` with 6 predefined experiments: microbreaks, boredom-fatigue split, confusion patience, peak time adaptation, solution diversity, sensor-enhanced. `FocusExperimentCollector` captures per-student per-session metrics with hash-based deterministic arm assignment and CSV/Parquet export.

This task adds 3 new experiments to measure the learning impact of the hint system (Task 01b), L2 explanation cache (Task 02), and L3 personalized explanations (Task 03). No new framework code — just new experiment configurations following the established pattern.

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | Full (615 lines) | `FocusExperimentConfig` class — understand the structure: name, arms, assignment logic, metric collection |
| Find `FocusExperimentCollector` | Full | Per-student per-session metric capture — understand how metrics are recorded and exported |
| Existing 6 experiment definitions | In FocusDegradationService | Study the pattern: how arms are defined, how hash-based assignment works, how metrics feed analysis |

---

## Implementation Requirements

### 1. Experiment: `explanation_tiers`

**Purpose**: Measure learning impact of L1 vs L2 vs L3 explanations.

| Arm | Description | What Student Gets |
|-----|-------------|-------------------|
| control | No AI explanation | Generic "Incorrect. Try again." (current behavior) |
| l1_static | L1 only | Static AI-generated explanation from question aggregate |
| l2_cached | L1 + L2 | Error-type-specific cached explanation |
| l3_personalized | L1 + L2 + L3 | Full personalized explanation with student context |

**Metrics to capture per student per session**:
- `mastery_gain` — delta P_L at session end vs start (per concept)
- `time_to_mastery` — questions to reach P_L >= 0.85 threshold
- `explanation_view_rate` — fraction of explanations the student reads (needs frontend event)
- `retry_success_rate` — after seeing explanation, does next attempt on same concept succeed?
- `session_length_questions` — total questions before voluntary end
- `engagement_score` — from `FocusDegradationService` composite score

**Assignment**: hash-based on `studentId` — deterministic, same student always gets same arm.

### 2. Experiment: `hint_bkt_credit`

**Purpose**: Validate the BKT credit curve for hint usage.

| Arm | Credit Curve (0/1/2/3 hints) | Rationale |
|-----|------------------------------|-----------|
| aggressive | 1.0 / 0.7 / 0.4 / 0.1 | Strong penalty — hints should not count as learning |
| moderate | 1.0 / 0.8 / 0.5 / 0.2 | Moderate penalty — some learning still occurs with hints |
| lenient | 1.0 / 0.9 / 0.7 / 0.4 | Light penalty — hints are instructional, not just crutches |

**Metrics**:
- `mastery_accuracy` — does P_L predict actual performance on unseen questions?
- `hint_request_rate` — are students gaming the lenient curve by over-requesting hints?
- `independent_success_rate` — on questions where hints were NOT used, per-arm comparison
- `time_to_mastery` — per concept

### 3. Experiment: `confusion_gating`

**Purpose**: Validate whether respecting the confusion patience window improves learning.

| Arm | Behavior |
|-----|----------|
| patience | Respect ConfusionDetector patience window — no automatic hints during ConfusionResolving |
| immediate | Always deliver hints immediately when confusion detected — ignore patience window |
| student_choice | Show "Need a hint?" prompt during ConfusionResolving — let student decide |

**Metrics**:
- `confusion_resolution_rate` — what percentage of confused episodes resolve without intervention?
- `confusion_to_mastery_time` — from confusion onset to concept mastery
- `frustration_abandonment_rate` — sessions ended with reason 'tired' during confusion
- `mastery_depth` — Bloom level achieved post-confusion

### 4. Follow the Existing Pattern

- Define configs using the same structure as the 6 existing experiments in `FocusDegradationService`
- Use `FocusExperimentCollector` for metric capture — do NOT build a parallel collection system
- Hash-based deterministic arm assignment — same student, same arm, every session
- Export format: CSV/Parquet compatible with existing pipeline

### 5. Safety Guardrails

- Experiments must be **opt-in activation** — define the configs but do NOT activate in production without explicit approval
- The `control` arm in `explanation_tiers` degrades the student experience — limit to 10% of students
- If any arm shows statistically significant negative impact on `mastery_gain` after 1000 students, auto-disable

---

## What NOT to Do

- Do NOT build a custom A/B framework — use existing `FocusExperimentConfig` pattern
- Do NOT activate experiments in production without approval
- Do NOT create new metric storage — use `FocusExperimentCollector`
- Do NOT change the experiment infrastructure — only add new configs

---

## Verification Checklist

- [ ] 3 new experiment configs registered in the system
- [ ] Hash-based arm assignment is deterministic (same student → same arm across sessions)
- [ ] `explanation_tiers` has 4 arms with correct behavior per arm
- [ ] `hint_bkt_credit` applies different credit curves per arm
- [ ] `confusion_gating` respects/ignores patience window per arm
- [ ] Metrics captured per-student per-session for all 3 experiments
- [ ] CSV/Parquet export includes new experiment data
- [ ] Experiments are NOT activated by default
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
