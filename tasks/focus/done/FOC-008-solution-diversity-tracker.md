# FOC-008: Solution Diversity Tracker

**Priority:** P1 — strongest predictor of productive failure success (meta-analysis N=12,000)
**Blocked by:** ACT-003 (SessionActor), MST-001 (ConceptMasteryState)
**Estimated effort:** 2 days
**Contract:** Extends `StruggleInput` and `ClassifyStruggle()`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Sinha & Kapur (2021, N=12,000+): The NUMBER OF DIFFERENT SOLUTION APPROACHES a student generates during productive failure is a stronger predictor of learning than prior math achievement. Students who tried 3+ different approaches outperformed those who repeated the same approach.

npj Science of Learning (2023): "Inventive production" (solution diversity) had stronger association with PF learning than pre-existing math achievement differences.

Cena's current `ClassifyStruggle()` uses error variety (same error type count) as a proxy but doesn't track distinct solution APPROACHES.

## Subtasks

### FOC-008.1: Approach Classification
**Files:**
- `src/Cena.Actors/Services/SolutionDiversityTracker.cs` — NEW

**Acceptance:**
- [ ] `ISolutionDiversityTracker.TrackAttempt(ConceptId, ApproachSignature) → DiversityState`
- [ ] `ApproachSignature` captures how the student tried to solve the problem: `DirectComputation`, `WorkedBackward`, `TriedExample`, `UsedFormula`, `DrewDiagram`, `GavePartialAnswer`, `Guessed`
- [ ] For structured questions: approach inferred from answer pattern (e.g., procedural steps used)
- [ ] For open-ended: approach inferred from time profile + hint usage + annotation content
- [ ] `DiversityState`: `uniqueApproachCount`, `mostRecentApproach`, `isExploring` (>= 2 different approaches)

### FOC-008.2: Integration with Struggle Classifier
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `ClassifyStruggle()`

**Acceptance:**
- [ ] `StruggleInput` gains `SolutionDiversityCount` (int, default 1)
- [ ] If `SolutionDiversityCount >= 3` AND `AccuracySlope > -0.05` → classify as `ProductiveStruggle` even if accuracy is flat (exploration compensates for plateau)
- [ ] Boost productive struggle confidence by `0.05 * diversityCount` (more approaches = more confident it's productive)
- [ ] If `SolutionDiversityCount == 1` AND `SameErrorTypeCount >= 3` → stronger signal for `UnproductiveFrustration`

**Test:**
```csharp
[Fact]
public void HighDiversity_ClassifiesAsProductive_EvenWithFlatAccuracy()
{
    var input = new StruggleInput(
        AccuracySlope: 0.0, // flat — normally ambiguous
        SameErrorTypeCount: 1,
        ResponseTimeMean: 3000,
        ResponseTimeStdDev: 500,
        AnnotationSentiment: 0.5,
        SolutionDiversityCount: 4 // tried 4 different approaches!
    );
    var result = service.ClassifyStruggle(input);
    Assert.Equal(StruggleType.ProductiveStruggle, result.Type);
}
```

## Research References
- Sinha & Kapur (2021): meta-analysis, 166 comparisons, d = 0.36-0.58
- npj Science of Learning (2023): inventive production > prior math achievement
- Kapur (2008): productive failure in mathematical problem solving
