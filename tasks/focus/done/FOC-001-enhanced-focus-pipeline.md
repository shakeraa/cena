# FOC-001: Enhanced Focus Signal Pipeline

**Priority:** P1 — enriches the core attention model with sensor + behavioral signals
**Blocked by:** ACT-002 (StudentActor), MOB-003 (WebSocket), FOC-002 (sensor layer), FOC-009 (privacy layer)
**Estimated effort:** 1-2 weeks
**Contract:** `src/actors/Cena.Actors/Services/FocusDegradationService.cs` (lines 77-172)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

The existing `FocusDegradationService.ComputeFocusState()` uses 4 signals:
1. Attention (RT variance) — weight 0.30
2. Engagement (hint/annotation rate) — weight 0.20
3. Accuracy trend (slope) — weight 0.25
4. Vigilance decrement (time decay) — weight 0.25

Research (autoresearch 2026-03-27) identified 4 additional signals available from mobile sensors and behavioral analysis that can significantly improve detection accuracy:
5. **Motion stability** — accelerometer/gyroscope detect fidgeting, phone-down, walking
6. **App focus** — lifecycle events detect backgrounding, multitasking, notification interruptions
7. **Touch pattern consistency** — tap rhythm, swipe velocity, touch area consistency
8. **Environment stability** — ambient light changes, proximity sensor (face-down detection)

The pipeline must be backwards-compatible: if sensor data is unavailable (web client, permissions denied), the existing 4-signal model works unchanged.

## Subtasks

### FOC-001.1: Extended FocusInput Record
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — extend `FocusInput` record

**Acceptance:**
- [ ] `FocusInput` gains optional sensor fields: `MotionStabilityScore?`, `AppFocusScore?`, `TouchPatternScore?`, `EnvironmentScore?`
- [ ] All new fields are nullable (`double?`) — absence means "no sensor data available"
- [ ] Existing callers continue to work unchanged (new fields default to `null`)
- [ ] `SensorDataAvailable` computed property: true if any sensor field is non-null

### FOC-001.2: Adaptive Weighting System
**Files:**
- `src/Cena.Actors/Services/FocusWeightCalculator.cs` — NEW

**Acceptance:**
- [ ] When NO sensor data: weights are original [0.30, 0.20, 0.25, 0.25] for 4 signals
- [ ] When ALL sensor data available: weights redistribute to 8 signals:
  - Attention: 0.20, Engagement: 0.12, Trend: 0.15, Vigilance: 0.15
  - Motion: 0.12, AppFocus: 0.10, TouchPattern: 0.08, Environment: 0.08
- [ ] When PARTIAL sensor data: interpolate weights proportionally (only include available signals)
- [ ] Weights always sum to 1.0 (±0.001 tolerance)
- [ ] `FocusWeightCalculator.ComputeWeights(SensorAvailability) → FocusWeights` pure function

**Test:**
```csharp
[Fact]
public void Weights_NoSensors_MatchOriginal()
{
    var weights = FocusWeightCalculator.ComputeWeights(SensorAvailability.None);
    Assert.Equal(0.30, weights.Attention, precision: 3);
    Assert.Equal(0.20, weights.Engagement, precision: 3);
    Assert.Equal(0.25, weights.Trend, precision: 3);
    Assert.Equal(0.25, weights.Vigilance, precision: 3);
    Assert.InRange(weights.Sum(), 0.999, 1.001);
}

[Fact]
public void Weights_AllSensors_Redistribute()
{
    var weights = FocusWeightCalculator.ComputeWeights(SensorAvailability.All);
    Assert.Equal(8, weights.ActiveSignalCount);
    Assert.InRange(weights.Sum(), 0.999, 1.001);
}
```

### FOC-001.3: Sensor Signal Integration
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `ComputeFocusState()`

**Acceptance:**
- [ ] If `MotionStabilityScore` is provided, blend into composite (stable phone = focused)
- [ ] If `AppFocusScore` is provided, blend into composite (no app switches = focused)
- [ ] If `TouchPatternScore` is provided, blend into composite (consistent touch = focused)
- [ ] If `EnvironmentScore` is provided, blend into composite (stable environment = focused)
- [ ] `FocusState` record gains: `SensorSignalCount` (0-4), `SensorConfidenceBoost` (higher signal count → higher confidence in the focus assessment)
- [ ] Existing telemetry histograms updated to tag with `sensor_count` dimension

### FOC-001.4: Executive Load Factor (Thomson 2022)
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify vigilance decay

**Acceptance:**
- [ ] Add `executiveLoadFactor` to vigilance decay formula based on task complexity
- [ ] Math problem difficulty (from mastery engine) maps to executive load: recall=0.0, application=0.3, analysis=0.6, synthesis=0.8
- [ ] Modified decay: `decayFactor = 1.0 - (0.3 + executiveLoadFactor * 0.15) * ln(1 + (t - peak) / peak)`
- [ ] Higher Bloom levels → steeper vigilance decay (research: executive control decrements co-occur with vigilance decrements)
- [ ] Unit test: analysis-level questions cause faster vigilance drop than recall-level

**Test:**
```csharp
[Theory]
[InlineData(0.0, 20.0, 0.78)] // recall at 20 min → slow decay
[InlineData(0.6, 20.0, 0.72)] // analysis at 20 min → faster decay
[InlineData(0.8, 20.0, 0.69)] // synthesis at 20 min → fastest decay
public void ExecutiveLoad_IncreasesDecayRate(double load, double minutes, double expectedMax)
{
    // Higher executive load should produce lower vigilance score
    var score = ComputeVigilanceWithLoad(minutes, peakMinutes: 15, load);
    Assert.True(score <= expectedMax);
}
```

## Research References
- Thomson et al. (2022) — vigilance + executive control dual decrement
- Esterman et al. (2013) — RT variance as attention proxy
- Focus Degradation Research doc, Sections 2.1, 2.5, 2.12
