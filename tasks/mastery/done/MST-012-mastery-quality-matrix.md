# MST-012: Mastery Quality Matrix Classifier

**Priority:** P2 — qualitative signal for understanding mastery depth
**Blocked by:** MST-001 (ConceptMasteryState)
**Estimated effort:** 1-2 days (S)
**Contract:** `docs/mastery-engine-architecture.md` section 2.1 (QualityQuadrant field)
**Research ref:** `docs/mastery-measurement-research.md` section 2.3

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The mastery quality matrix classifies each response into a 2x2 matrix: (fast/slow) x (correct/incorrect). This produces four quadrants: Mastered (fast+correct), Effortful (slow+correct), Careless (fast+incorrect), and Struggling (slow+incorrect). The threshold for "fast" vs "slow" is the student's own median response time for similar-difficulty items — not a global constant. This signal is stored on `ConceptMasteryState.QualityQuadrant` and used by the stagnation detector and teacher dashboard.

## Subtasks

### MST-012.1: Response Time Baseline Tracker

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/ResponseTimeBaseline.cs`

**Acceptance:**
- [ ] `ResponseTimeBaseline` record: `MedianResponseTimeMs` (float), `SampleCount` (int), `ResponseTimes` (circular buffer of last 20 response times)
- [ ] `ResponseTimeBaseline.Update(int responseTimeMs) → ResponseTimeBaseline` returns new baseline with updated median
- [ ] Median computed from circular buffer using sorting (20 elements max — trivial cost)
- [ ] Initial baseline: if fewer than 3 samples, use `15_000ms` as default median
- [ ] Immutable — returns new instance on each update

### MST-012.2: Quality Matrix Classifier

**Files to create:**
- `src/Cena.Pedagogy/Domain/Services/MasteryQualityClassifier.cs`

**Acceptance:**
- [ ] `MasteryQualityClassifier.Classify(bool isCorrect, int responseTimeMs, float medianResponseTimeMs) → MasteryQuality`
- [ ] Fast = `responseTimeMs < medianResponseTimeMs`; Slow = `responseTimeMs >= medianResponseTimeMs`
- [ ] Fast + Correct → `MasteryQuality.Mastered`
- [ ] Slow + Correct → `MasteryQuality.Effortful`
- [ ] Fast + Incorrect → `MasteryQuality.Careless`
- [ ] Slow + Incorrect → `MasteryQuality.Struggling`
- [ ] Method is `static`, pure function

### MST-012.3: Integration with Attempt Pipeline

**Files to create/modify:**
- `src/Cena.Pedagogy/Domain/Services/MasteryQualityClassifier.cs` (add `ClassifyAndUpdate`)

**Acceptance:**
- [ ] `MasteryQualityClassifier.ClassifyAndUpdate(ConceptMasteryState state, bool isCorrect, int responseTimeMs, ResponseTimeBaseline baseline) → (MasteryQuality quality, ResponseTimeBaseline updatedBaseline)`
- [ ] Classifies the response using current baseline median
- [ ] Updates the baseline with the new response time
- [ ] Returns both the quality classification and the updated baseline
- [ ] Called by StudentActor mastery handler after BKT/HLR updates

**Test:**
```csharp
[Fact]
public void Classify_FastCorrect_ReturnsMastered()
{
    var quality = MasteryQualityClassifier.Classify(
        isCorrect: true, responseTimeMs: 5_000, medianResponseTimeMs: 12_000f);

    Assert.Equal(MasteryQuality.Mastered, quality);
}

[Fact]
public void Classify_SlowCorrect_ReturnsEffortful()
{
    var quality = MasteryQualityClassifier.Classify(
        isCorrect: true, responseTimeMs: 18_000, medianResponseTimeMs: 12_000f);

    Assert.Equal(MasteryQuality.Effortful, quality);
}

[Fact]
public void Classify_FastIncorrect_ReturnsCareless()
{
    var quality = MasteryQualityClassifier.Classify(
        isCorrect: false, responseTimeMs: 3_000, medianResponseTimeMs: 12_000f);

    Assert.Equal(MasteryQuality.Careless, quality);
}

[Fact]
public void Classify_SlowIncorrect_ReturnsStruggling()
{
    var quality = MasteryQualityClassifier.Classify(
        isCorrect: false, responseTimeMs: 25_000, medianResponseTimeMs: 12_000f);

    Assert.Equal(MasteryQuality.Struggling, quality);
}

[Fact]
public void ResponseTimeBaseline_UpdatesMedian()
{
    var baseline = new ResponseTimeBaseline(
        MedianResponseTimeMs: 15_000f, SampleCount: 0,
        ResponseTimes: Array.Empty<int>());

    baseline = baseline.Update(10_000);
    baseline = baseline.Update(12_000);
    baseline = baseline.Update(14_000);
    baseline = baseline.Update(8_000);
    baseline = baseline.Update(20_000);

    // Sorted: 8000, 10000, 12000, 14000, 20000 → median = 12000
    Assert.Equal(12_000f, baseline.MedianResponseTimeMs);
    Assert.Equal(5, baseline.SampleCount);
}

[Fact]
public void ResponseTimeBaseline_FewSamples_UsesDefault()
{
    var baseline = new ResponseTimeBaseline(
        MedianResponseTimeMs: 15_000f, SampleCount: 0,
        ResponseTimes: Array.Empty<int>());

    baseline = baseline.Update(5_000); // only 1 sample

    // With < 3 samples, should still use default 15000ms
    Assert.Equal(15_000f, baseline.MedianResponseTimeMs);
}
```
