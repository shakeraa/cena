# MST-003: Half-Life Regression Decay Engine

**Priority:** P0 — drives spaced repetition scheduling
**Blocked by:** MST-001 (ConceptMasteryState)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 3.1 step 2, section 4
**Research ref:** `docs/mastery-measurement-research.md` section 1.3.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

HLR (Settles & Meeder 2016, Duolingo) predicts when a student will forget a concept. The half-life `h` for each concept is a learned function of the student's practice history. At launch, θ weights are hand-tuned; at scale, they're trained offline (MST-016). The recall formula `p(t) = 2^(-Δt/h)` runs continuously — not just on interaction but also on the decay timer (MST-007).

## Subtasks

### MST-003.1: HLR Feature Vector

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/HlrFeatures.cs`
- `src/Cena.Domain/Learner/Mastery/HlrWeights.cs`
- `src/Cena.Domain/Learner/Mastery/IHlrWeightProvider.cs`

**Acceptance:**
- [ ] `HlrFeatures` readonly struct (stack-allocated, zero GC pressure):
  - `AttemptCount` (int)
  - `CorrectCount` (int)
  - `ConceptDifficulty` (float, from Neo4j node property)
  - `PrerequisiteDepth` (int, graph distance to root)
  - `BloomLevel` (int, 0-6)
  - `DaysSinceFirstEncounter` (float)
- [ ] `HlrFeatures.ToVector()` returns `ReadOnlySpan<float>` (6 elements) — no allocation
- [ ] `HlrWeights` readonly struct: 6 float weights + bias, loaded from config
- [ ] `IHlrWeightProvider` interface: `HlrWeights GetWeights(string? conceptCategory = null)`
- [ ] Default weights (hand-tuned): `[0.3, 0.5, -0.2, -0.1, 0.1, 0.05]`, bias=`3.0` (h ≈ 8 days for a fresh concept)

### MST-003.2: HLR Computation

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/HlrCalculator.cs`

**Acceptance:**
- [ ] `HlrCalculator.ComputeHalfLife(HlrFeatures features, HlrWeights weights) → float`
  - Formula: `h = 2^(θ·x + bias)` where θ·x is dot product of weights and feature vector
  - Returns hours (not days)
  - Clamped to [1.0, 8760.0] (1 hour minimum, 1 year maximum)
- [ ] `HlrCalculator.ComputeRecall(float halfLifeHours, TimeSpan elapsed) → float`
  - Formula: `2^(-elapsed.TotalHours / halfLifeHours)`
  - Guard: if halfLifeHours ≤ 0, return 0
  - Guard: if elapsed ≤ TimeSpan.Zero, return 1.0 (just practiced)
- [ ] `HlrCalculator.ScheduleNextReview(float halfLifeHours, float threshold = 0.85f) → TimeSpan`
  - Formula: `TimeSpan.FromHours(-halfLifeHours * Math.Log2(threshold))`
  - With threshold=0.85: review when recall drops to 85%
- [ ] All methods are `static`, zero allocation

### MST-003.3: Integration with ConceptMasteryState

**Acceptance:**
- [ ] `HlrCalculator.UpdateState(ConceptMasteryState state, HlrFeatures features, HlrWeights weights) → ConceptMasteryState`
- [ ] Returns new state with updated `HalfLifeHours` via `WithHalfLifeUpdate()`
- [ ] Called after every `ConceptAttempted` event (half-life adjusts with each practice)

**Test:**
```csharp
[Fact]
public void ComputeRecall_AtHalfLife_Returns50Percent()
{
    var h = 168f; // 1 week
    var elapsed = TimeSpan.FromHours(168);

    var recall = HlrCalculator.ComputeRecall(h, elapsed);

    Assert.InRange(recall, 0.49f, 0.51f);
}

[Fact]
public void ComputeRecall_JustPracticed_Returns100Percent()
{
    var recall = HlrCalculator.ComputeRecall(168f, TimeSpan.Zero);
    Assert.Equal(1.0f, recall);
}

[Fact]
public void ComputeRecall_VeryOld_ReturnsNearZero()
{
    var recall = HlrCalculator.ComputeRecall(24f, TimeSpan.FromDays(365));
    Assert.True(recall < 0.001f);
}

[Fact]
public void ComputeHalfLife_MorePractice_LongerHalfLife()
{
    var weights = HlrWeights.Default;

    var fresh = HlrCalculator.ComputeHalfLife(
        new HlrFeatures(AttemptCount: 1, CorrectCount: 1, ConceptDifficulty: 0.5f,
            PrerequisiteDepth: 2, BloomLevel: 2, DaysSinceFirstEncounter: 1), weights);

    var practiced = HlrCalculator.ComputeHalfLife(
        new HlrFeatures(AttemptCount: 20, CorrectCount: 18, ConceptDifficulty: 0.5f,
            PrerequisiteDepth: 2, BloomLevel: 4, DaysSinceFirstEncounter: 30), weights);

    Assert.True(practiced > fresh,
        $"Practiced h={practiced}h should > fresh h={fresh}h");
}

[Fact]
public void ScheduleNextReview_DefaultThreshold_CorrectTiming()
{
    var h = 168f; // 1 week
    var nextReview = HlrCalculator.ScheduleNextReview(h, threshold: 0.85f);

    // 0.85 = 2^(-t/168) → t = -168 * log2(0.85) ≈ 168 * 0.2345 ≈ 39.4 hours
    Assert.InRange(nextReview.TotalHours, 38, 41);
}

[Fact]
public void ComputeHalfLife_Clamped_MinimumOneHour()
{
    // Pathological features that would produce tiny half-life
    var weights = new HlrWeights(new float[] { -10, -10, -10, -10, -10, -10 }, Bias: -20);
    var features = new HlrFeatures(0, 0, 1.0f, 10, 0, 0);

    var h = HlrCalculator.ComputeHalfLife(features, weights);
    Assert.True(h >= 1.0f, "Half-life must be at least 1 hour");
}
```
