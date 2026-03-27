# FOC-005: Confusion vs Frustration Discriminator

**Priority:** P1 — prevents premature intervention during productive confusion
**Blocked by:** FOC-001 (focus pipeline), ACT-004 (stagnation detector)
**Estimated effort:** 3-5 days
**Contract:** Extends `FocusDegradationService.ClassifyStruggle()` (lines 275-331)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

D'Mello & Graesser (2012, 2014): Confusion caused by cognitive disequilibrium is BENEFICIAL for deep learning — if resolved. Frustration from persistent failure is HARMFUL. The existing `ClassifyStruggle()` distinguishes productive struggle from frustration, but doesn't model confusion as a separate (positive) state.

Key insight: Confusion → IF resolved → deep learning (DO NOT interrupt). Frustration → IF persistent → learned helplessness (INTERVENE).

LAK 2024: Confusion and frustration are non-linear dynamical systems — small timing differences in intervention can produce very different outcomes.

## Subtasks

### FOC-005.1: Confusion Detection Signal
**Files:**
- `src/Cena.Actors/Services/ConfusionDetector.cs` — NEW

**Acceptance:**
- [ ] `IConfusionDetector.Detect(ConfusionInput) → ConfusionState`
- [ ] Confusion signals:
  - Wrong answer on previously-mastered concept (unexpected error)
  - Longer RT followed by correct answer (thinking through confusion)
  - Changed answer mid-submission (reconsidered)
  - Requested hint then cancelled it (tried to solve on their own)
- [ ] `ConfusionState`: `NotConfused`, `Confused`, `ConfusionResolving`, `ConfusionStuck`
- [ ] `ConfusionResolving`: accuracy recovering after confusion period (let it ride)
- [ ] `ConfusionStuck`: confusion persists >N questions without resolution → scaffold

### FOC-005.2: Confusion Resolution Tracker
**Files:**
- `src/Cena.Actors/Services/ConfusionResolutionTracker.cs` — NEW

**Acceptance:**
- [ ] Tracks confusion → resolution sequences per concept
- [ ] `ResolutionWindow`: after confusion detected, monitor next 3-5 questions
- [ ] If student gets the concept right within window → `Resolved` (log as positive learning event)
- [ ] If student fails within window → `Unresolved` (trigger scaffold/hint)
- [ ] `ConfusionResolutionRate` computed over last 10 confusion events (predicts student's ability to self-resolve)
- [ ] High resolution rate (>0.7) → extend patience window. Low rate (<0.3) → intervene sooner.

### FOC-005.3: Integration with Struggle Classifier
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `ClassifyStruggle()`

**Acceptance:**
- [ ] New `StruggleType.ProductiveConfusion` added to enum
- [ ] Existing `ProductiveStruggle` split: if confusion signals present AND accuracy recovering → `ProductiveConfusion`
- [ ] `ProductiveConfusion` recommendation: "Student is confused but resolving. Provide NO hint. Wait for resolution window to expire."
- [ ] `ConfusionStuck` maps to existing `UnproductiveFrustration` but with different recommendation: "Provide scaffolding hint, not methodology switch. Confusion needs a nudge, not a restart."
- [ ] `StruggleInput` gains optional `ConfusionState` field

**Test:**
```csharp
[Fact]
public void ProductiveConfusion_DetectedWhenConfusedButRecovering()
{
    var input = new StruggleInput(
        AccuracySlope: 0.02, // slightly improving
        SameErrorTypeCount: 1, // varied errors
        ResponseTimeMean: 4500, // longer than usual (thinking)
        ResponseTimeStdDev: 800, // relatively stable
        AnnotationSentiment: 0.5,
        ConfusionState: ConfusionState.ConfusionResolving
    );
    var result = service.ClassifyStruggle(input);
    Assert.Equal(StruggleType.ProductiveConfusion, result.Type);
    Assert.Contains("NO hint", result.Recommendation);
}
```

## Research References
- D'Mello & Graesser (2012): affect dynamics during complex learning
- D'Mello & Graesser (2014): confusion can be beneficial for learning
- LAK 2024: confusion/frustration as non-linear dynamical systems
