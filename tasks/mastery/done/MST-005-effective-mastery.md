# MST-005: Effective Mastery Compositor

**Priority:** P0 — single number that drives all downstream decisions
**Blocked by:** MST-002 (BKT engine), MST-003 (HLR decay engine), MST-004 (prerequisite calculator)
**Estimated effort:** 1-2 days (S)
**Contract:** `docs/mastery-engine-architecture.md` section 3.1 step 4
**Research ref:** `docs/mastery-measurement-research.md` section 4.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Effective mastery is the single composite signal that answers "how well does this student know this concept right now?" It combines three signals: BKT probability P(L), HLR recall probability p_recall, and prerequisite support. The Phase 1 formula is `min(P(L), p_recall) * prereq_support`. This value drives learning frontier computation, item selection, scaffolding levels, decay alerts, and all visualization coloring. It must be cheap to compute (called on every UI render and every attempt).

## Subtasks

### MST-005.1: Effective Mastery Calculator

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/EffectiveMasteryCalculator.cs`

**Acceptance:**
- [ ] `EffectiveMasteryCalculator.Compute(ConceptMasteryState state, float prereqSupport, DateTimeOffset now) → float`
- [ ] Formula: `min(state.MasteryProbability, state.RecallProbability(now)) * prereqSupport`
- [ ] Guard: if `prereqSupport <= 0`, returns `0.0`
- [ ] Guard: if state has never been interacted with (`LastInteraction == default`), returns `0.0`
- [ ] Output clamped to [0.0, 1.0]
- [ ] Method is `static`, zero allocation

### MST-005.2: Threshold Crossing Detection

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/EffectiveMasteryCalculator.cs` (add `DetectThresholdCrossing` method)

**Acceptance:**
- [ ] `EffectiveMasteryCalculator.DetectThresholdCrossing(float previousEffective, float newEffective) → MasteryThresholdEvent?`
- [ ] Returns `ConceptMastered` trigger when crossing 0.90 upward
- [ ] Returns `MasteryDecayed` trigger when crossing 0.70 downward
- [ ] Returns `null` when no threshold is crossed
- [ ] Enum: `MasteryThresholdEvent { ConceptMastered, MasteryDecayed, PrerequisiteBlocked }`
- [ ] Returns `PrerequisiteBlocked` when crossing 0.60 downward (PSI gate)

### MST-005.3: Full Pipeline Integration

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/MasteryPipeline.cs`

**Acceptance:**
- [ ] `MasteryPipeline.ProcessAttempt(ConceptMasteryState currentState, bool isCorrect, BktParameters bktParams, HlrFeatures hlrFeatures, HlrWeights hlrWeights, float prereqSupport, DateTimeOffset now) → MasteryUpdateResult`
- [ ] `MasteryUpdateResult` record: `(ConceptMasteryState NewState, float EffectiveMastery, MasteryThresholdEvent? ThresholdEvent)`
- [ ] Executes steps in order: BKT update → HLR update → effective mastery → threshold detection
- [ ] Returns the fully updated state with all signals composed
- [ ] Does NOT emit events (that is the actor's job) — pure computation only

**Test:**
```csharp
[Fact]
public void EffectiveMastery_CombinesBktAndRecallWithPrereqs()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.85f,
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-168) // at half-life
    };
    var prereqSupport = 0.90f;
    var now = DateTimeOffset.UtcNow;

    var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport, now);

    // recall ≈ 0.50 (at half-life), mastery = 0.85
    // min(0.85, 0.50) = 0.50, × 0.90 prereq = 0.45
    Assert.InRange(effective, 0.44f, 0.46f);
}

[Fact]
public void EffectiveMastery_RecentPractice_UsesFullMastery()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.85f,
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.UtcNow.AddMinutes(-5) // just practiced
    };
    var prereqSupport = 1.0f;
    var now = DateTimeOffset.UtcNow;

    var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport, now);

    // recall ≈ 1.0 (just practiced), min(0.85, ~1.0) = 0.85
    Assert.InRange(effective, 0.84f, 0.86f);
}

[Fact]
public void EffectiveMastery_ZeroPrereqSupport_ReturnsZero()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.95f,
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.UtcNow
    };

    var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport: 0.0f, DateTimeOffset.UtcNow);

    Assert.Equal(0.0f, effective);
}

[Fact]
public void ThresholdCrossing_UpwardPast90_ReturnsMastered()
{
    var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
        previousEffective: 0.88f, newEffective: 0.92f);

    Assert.Equal(MasteryThresholdEvent.ConceptMastered, evt);
}

[Fact]
public void ThresholdCrossing_DownwardBelow70_ReturnsDecayed()
{
    var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
        previousEffective: 0.75f, newEffective: 0.68f);

    Assert.Equal(MasteryThresholdEvent.MasteryDecayed, evt);
}

[Fact]
public void ThresholdCrossing_NoChange_ReturnsNull()
{
    var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
        previousEffective: 0.80f, newEffective: 0.82f);

    Assert.Null(evt);
}

[Fact]
public void FullPipeline_CorrectAnswer_IncreasesEffectiveMastery()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.50f,
        HalfLifeHours = 72f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-12),
        AttemptCount = 5,
        CorrectCount = 3,
        CurrentStreak = 1,
        BloomLevel = 2
    };
    var bktParams = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);
    var hlrFeatures = new HlrFeatures(6, 4, 0.5f, 2, 2, 7f);
    var hlrWeights = HlrWeights.Default;

    var result = MasteryPipeline.ProcessAttempt(
        state, isCorrect: true, bktParams, hlrFeatures, hlrWeights,
        prereqSupport: 1.0f, DateTimeOffset.UtcNow);

    Assert.True(result.EffectiveMastery > 0.50f,
        $"Effective mastery should increase after correct answer, got {result.EffectiveMastery}");
    Assert.True(result.NewState.MasteryProbability > state.MasteryProbability);
}
```
