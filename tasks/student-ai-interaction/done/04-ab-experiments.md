# Task 04: A/B Experiment Configs for Tier 1-2 Validation

**Effort**: 1-2 days | **Track**: D | **Depends on**: Tasks 01b, 02, 03 | **Blocks**: 07

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor. The platform has a fully built A/B testing framework:

- **`FocusExperimentService`** (216 lines, `src/actors/Cena.Actors/Services/FocusExperimentConfig.cs`): Hash-based deterministic assignment. `GetAssignment(studentId, experimentId) → ExperimentArm`. Multi-arm support with percentage allocation.
- **`FocusExperimentCollector`** (96 lines, `src/actors/Cena.Actors/Services/FocusExperimentCollector.cs`): Records `ExperimentSessionMetrics` per student per session. In-memory with 100K cap. Supports export.
- **6 predefined experiments** already exist: microbreaks, boredom-fatigue split, confusion patience, peak time, solution diversity, sensor-enhanced.
- **`ExperimentArm` enum**: `Control`, `Treatment`, `TreatmentB` (3-arm support exists).

After Tasks 00-03: real LLM calls, L1 static explanations, L2 cached explanations, L3 personalized explanations, hint generation with BKT credit adjustment, and confusion-state gating all work.

**Research basis**: Ma et al. (2014) expect +0.42-0.57 SD for ITS instruction. VanLehn (2011) expects +0.45 SD for step-level interactivity. Need rigorous measurement.

---

## Objective

Add 3 new experiment configs to the existing `FocusExperimentService.DefaultExperiments` array and extend `ExperimentSessionMetrics` to capture explanation/hint metrics.

---

## Files to Read First (MANDATORY)

| File | Path | Lines | Key Structure |
|------|------|-------|---------------|
| FocusExperimentConfig | `src/actors/Cena.Actors/Services/FocusExperimentConfig.cs` | 216 | `FocusExperimentService`, `DefaultExperiments` array (6 experiments), `FocusExperiment` record, `ExperimentArmConfig`, `ExperimentArm` enum |
| FocusExperimentCollector | `src/actors/Cena.Actors/Services/FocusExperimentCollector.cs` | 96 | `ExperimentSessionMetrics` record — add new fields here |
| FocusExperimentTests | `src/actors/Cena.Actors.Tests/Services/FocusExperimentTests.cs` | ? | Existing test patterns |

---

## Implementation

### 1. Add 3 New Experiments to `DefaultExperiments`

Append to the existing array in `FocusExperimentConfig.cs`:

```csharp
// ── STUDENT AI INTERACTION EXPERIMENTS ──

new FocusExperiment(
    ExperimentId: "ai-explanation-tiers",
    Name: "Explanation Tier Comparison",
    Description: "No explanation vs L1 static vs L2 error-typed vs L3 personalized",
    PrimaryMetric: "mastery_velocity_concepts_per_week",
    Arms: new[]
    {
        new ExperimentArmConfig(ExperimentArm.Control, 25, "No explanation (current baseline)"),
        new ExperimentArmConfig(ExperimentArm.Treatment, 25, "L1 static explanation only"),
        new ExperimentArmConfig(ExperimentArm.TreatmentB, 25, "L2 error-typed cached"),
        // 4th arm needs enum extension — see below
    },
    StartDate: DateTimeOffset.MinValue,
    EndDate: DateTimeOffset.MaxValue
),

new FocusExperiment(
    ExperimentId: "ai-hint-bkt-credit",
    Name: "Hint BKT Credit Curve",
    Description: "Conservative hint credit (1.0/0.7/0.4/0.1) vs generous (1.0/0.8/0.5/0.2)",
    PrimaryMetric: "time_to_mastery_sessions",
    Arms: new[]
    {
        new ExperimentArmConfig(ExperimentArm.Control, 50, "Conservative: 1.0/0.7/0.4/0.1"),
        new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Generous: 1.0/0.8/0.5/0.2")
    },
    StartDate: DateTimeOffset.MinValue,
    EndDate: DateTimeOffset.MaxValue
),

new FocusExperiment(
    ExperimentId: "ai-confusion-gating",
    Name: "Confusion-Gated Hint Delivery",
    Description: "Always deliver hints on request vs respect confusion patience window",
    PrimaryMetric: "delayed_test_score_1_week",
    Arms: new[]
    {
        new ExperimentArmConfig(ExperimentArm.Control, 50, "Always deliver hints immediately"),
        new ExperimentArmConfig(ExperimentArm.Treatment, 50, "Respect confusion patience window")
    },
    StartDate: DateTimeOffset.MinValue,
    EndDate: DateTimeOffset.MaxValue
)
```

### 2. Extend `ExperimentArm` Enum

The explanation-tiers experiment needs 4 arms. Add:
```csharp
public enum ExperimentArm
{
    Control,
    Treatment,
    TreatmentB,
    TreatmentC    // <── NEW: for 4-arm experiments
}
```

### 3. Extend `ExperimentSessionMetrics`

Add explanation/hint metrics to `ExperimentSessionMetrics` in `FocusExperimentCollector.cs`:

```csharp
// ── AI Interaction metrics (NEW) ──
int? ExplanationsServed,            // How many explanations this session
int? ExplanationCacheHits,          // L2 cache hits (no LLM cost)
int? ExplanationCacheMisses,        // L2 misses (LLM generated)
int? L3PersonalizedCount,           // L3 personalized explanations
int? HintsRequested,                // Total hint requests
int? HintsSuppressedByConfusion,    // Hints blocked by ConfusionResolving
double? MasteryGainDelta,           // ΔP(L) for primary concepts this session
double? TimeToMasteryEstimate,      // Estimated sessions to mastery
int? TokensConsumed                 // LLM tokens used this session
```

### 4. Wire into LearningSessionActor

At session end, `LearningSessionActor` should record `ExperimentSessionMetrics` for all active AI experiments. Check `_experimentService.IsActive("ai-explanation-tiers")` and record metrics.

---

## What NOT to Do

- Do NOT build a custom A/B framework — use the existing `FocusExperimentService` pattern exactly
- Do NOT activate experiments in production without explicit approval — leave dates as MinValue/MaxValue
- Do NOT modify the hash-based assignment algorithm — it's deterministic and correct
- Do NOT add UI for experiment management — admin uses the existing pattern

---

## Verification Checklist

- [ ] 3 new experiments registered in `DefaultExperiments` (total: 9)
- [ ] `ExperimentArm.TreatmentC` added to enum
- [ ] Hash-based assignment deterministic: same student → same arm across sessions
- [ ] `ExperimentSessionMetrics` extended with AI interaction fields
- [ ] Existing 6 experiments still function correctly (no regression)
- [ ] Existing tests still pass
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
