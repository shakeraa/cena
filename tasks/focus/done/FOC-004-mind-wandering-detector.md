# FOC-004: Mind-Wandering Detector (Aware vs Unaware)

**Priority:** P2 — enhances attention model with 30%-of-time phenomenon
**Blocked by:** FOC-001 (enhanced focus pipeline)
**Estimated effort:** 3-5 days
**Contract:** Extends `FocusDegradationService.ComputeFocusState()`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Meta-analysis (Wammes et al., 2022): mind-wandering occurs ~30% of educational time and explains ~7% of variability in learning outcomes. Two types:
- **Aware mind-wandering:** Student knows they drifted (pause → return). Less harmful, may self-correct.
- **Unaware mind-wandering:** Student doesn't realize it (gradual degradation). More harmful, needs intervention.

Current Cena model treats all "drifting" as equivalent. Distinguishing aware from unaware enables better-timed interventions.

## Subtasks

### FOC-004.1: Mind-Wandering Signal Extraction
**Files:**
- `src/Cena.Actors/Services/MindWanderingDetector.cs` — NEW

**Acceptance:**
- [ ] `IMindWanderingDetector.Detect(MindWanderingInput) → MindWanderingState`
- [ ] **Aware pattern:** RT gap (>2x baseline for one question) followed by normal RT = pause-then-return
- [ ] **Unaware pattern:** Gradually increasing RT variance over 5+ questions with NO gap/pause = drift
- [ ] **Focused:** RT variance within baseline, no gaps
- [ ] `MindWanderingState`: `Focused`, `AwareDrift`, `UnawareDrift`, `Ambiguous`
- [ ] Confidence score: how certain we are about the classification

### FOC-004.2: Differentiated Intervention Rules
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — integrate

**Acceptance:**
- [ ] `AwareDrift` → gentle nudge only: "Welcome back! Let's continue." (they already self-corrected)
- [ ] `UnawareDrift` → stronger intervention: change question type, add visual stimulus, or trigger microbreak
- [ ] `AwareDrift` does NOT lower focus score as much as `UnawareDrift` (50% penalty reduction)
- [ ] `MindWanderingState` included in `FocusState` for analytics

**Test:**
```csharp
[Fact]
public void AwareDrift_DetectedFromRtGapThenRecovery()
{
    var input = new MindWanderingInput(
        RecentRtMs: [2000, 2100, 8500, 2200, 1900], // gap at index 2, then normal
        BaselineRtMs: [2000, 2100, 1900, 2050, 2000],
        RecentAccuracies: [1, 1, 0, 1, 1]
    );
    var result = detector.Detect(input);
    Assert.Equal(MindWanderingState.AwareDrift, result.State);
}

[Fact]
public void UnawareDrift_DetectedFromGradualVarianceIncrease()
{
    var input = new MindWanderingInput(
        RecentRtMs: [2000, 2300, 2800, 3500, 4200, 5100], // steadily increasing, no gap
        BaselineRtMs: [2000, 2100, 1900, 2050, 2000, 2100],
        RecentAccuracies: [1, 1, 0, 0, 1, 0]
    );
    var result = detector.Detect(input);
    Assert.Equal(MindWanderingState.UnawareDrift, result.State);
}
```

## Research References
- Wammes et al. (2022): mind-wandering meta-analysis — 30% frequency, 7% variance
- IJAIED (2024): video-based mind-wandering detection generalizability
- APA PsycNet (2024): mind-wandering increases in frequency over time
