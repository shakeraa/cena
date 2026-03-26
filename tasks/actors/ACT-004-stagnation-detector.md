# ACT-004: StagnationDetectorActor (Classic, Timer-Based Sliding Window)

**Priority:** P1 — drives methodology switches, key to adaptive learning loop
**Blocked by:** ACT-001 (cluster), ACT-002 (StudentActor parent), ACT-003 (session provides signals)
**Estimated effort:** 3 days
**Contract:** `contracts/actors/stagnation_detector_actor.cs`, `contracts/backend/actor-contracts.cs` (lines 484-568), `contracts/backend/domain-services.cs` (lines 469-609)

---

## Context
The StagnationDetectorActor is a classic child of the StudentActor that lives across sessions. It maintains a sliding window of the last 3 sessions per concept cluster and computes a 5-signal composite stagnation score. When the score exceeds 0.7 for 3 consecutive sessions, it fires `StagnationDetected` to the parent, triggering a methodology switch. It also enforces a 3-session cooldown after each switch to give the new methodology time to take effect. The actor uses a per-student adaptive threshold (not a fixed 5%) to prevent false positives on slow learners.

## Subtasks

### ACT-004.1: Actor Scaffold + State Management
**Files to create/modify:**
- `src/Cena.Actors/Stagnation/StagnationDetectorActor.cs` -- main actor
- `src/Cena.Actors/Stagnation/ConceptStagnationState.cs` -- per-concept state
- `src/Cena.Actors/Stagnation/StagnationConfig.cs` -- configuration with signal weights
- `src/Cena.Actors/Stagnation/SessionSignalSnapshot.cs` -- per-session snapshot

**Acceptance:**
- [ ] `StagnationDetectorActor : IActor` with `ILogger<StagnationDetectorActor>` constructor (contract line 157-159)
- [ ] `ReceiveAsync` dispatches: `Started`, `Stopping`, `UpdateSignals`, `CheckStagnation`, `ResetAfterSwitch` (contract lines 162-173)
- [ ] Internal `Dictionary<string, ConceptStagnationState>` keyed by concept ID (contract line 137)
- [ ] `ConceptStagnationState` has all fields from contract lines 37-75: `ConceptClusterId`, `SessionWindow` (max 3), `AccuracyTrail` (max 20), `ResponseTimeTrail` (max 20), `SessionDurationTrail` (max 5), `RecentErrorTypes` (max 10), `LatestAnnotationSentiment`, `CompositeScoreHistory` (max 3), `ConsecutiveStagnantSessions`, `CooldownSessionsRemaining`, `CooldownMethodology`
- [ ] `SessionSignalSnapshot` record: `SessionId`, `AttemptsInSession`, `CorrectInSession`, `AvgResponseTimeMs`, `SessionDurationMinutes`, `DominantErrorType`, `AnnotationSentiment`, `Timestamp` (contract lines 80-88)
- [ ] `StagnationConfig` with weights summing to 1.0 (contract lines 545-587): `WeightAccuracy=0.30`, `WeightResponseTime=0.20`, `WeightAbandonment=0.20`, `WeightErrorRepetition=0.20`, `WeightAnnotationSentiment=0.10`
- [ ] `StagnationConfig.AreWeightsValid()` checks sum within 0.001 tolerance (contract lines 581-586)
- [ ] `StagnationConfig.Default` static property (contract line 576)
- [ ] Per-student adaptive threshold `_studentAvgImprovementRate` initialized to 0.05 (contract line 145)
- [ ] Telemetry: `ActivitySource("Cena.Actors.StagnationDetectorActor")`, `Meter`, `CompositeScoreHistogram`, `StagnationDetectedCounter` (contract lines 148-155)
- [ ] `GetOrCreateConceptState()` lazy-initializes state per concept (contract lines 513-521)

**Test:**
```csharp
[Fact]
public void StagnationConfig_DefaultWeights_SumToOne()
{
    var config = StagnationConfig.Default;
    Assert.True(config.AreWeightsValid(),
        "Default weights do not sum to 1.0");
    Assert.Equal(0.7, config.StagnationThreshold);
    Assert.Equal(3, config.ConsecutiveSessionsRequired);
    Assert.Equal(3, config.DefaultCooldownSessions);
}

[Fact]
public void StagnationConfig_InvalidWeights_Detected()
{
    var config = new StagnationConfig
    {
        WeightAccuracy = 0.5,
        WeightResponseTime = 0.5,
        WeightAbandonment = 0.5, // Sum > 1.0
        WeightErrorRepetition = 0.0,
        WeightAnnotationSentiment = 0.0
    };
    Assert.False(config.AreWeightsValid());
}

[Fact]
public async Task StagnationDetector_CreatesStateForNewConcept()
{
    var actor = CreateTestStagnationDetector();

    await SendMessage(actor, new UpdateSignals(
        "stu-1", "concept-1", "session-1",
        true, 5000, ErrorType.None, 10, null, 0.7, 5000));

    Assert.True(actor.ConceptStates.ContainsKey("concept-1"));
    Assert.Single(actor.ConceptStates["concept-1"].AccuracyTrail);
}
```

---

### ACT-004.2: UpdateSignals Handler (Per-Attempt Accumulation)
**Files to create/modify:**
- `src/Cena.Actors/Stagnation/StagnationDetectorActor.cs` -- `HandleUpdateSignals()`

**Acceptance:**
- [ ] Accumulates into `AccuracyTrail`: appends `1.0` (correct) or `0.0` (incorrect), FIFO cap at 20 (contract lines 205-206)
- [ ] Accumulates into `ResponseTimeTrail`: appends `cmd.ResponseTimeMs`, FIFO cap at 20 (contract lines 209-210)
- [ ] Accumulates into `RecentErrorTypes`: appends `cmd.ClassifiedErrorType.ToString()` if not `None`, FIFO cap at 10 (contract lines 213-217)
- [ ] Updates `LatestAnnotationSentiment` if `cmd.AnnotationSentiment.HasValue` (contract lines 220-223)
- [ ] Responds with `ActorResult(true)` (contract line 225)
- [ ] Does NOT trigger stagnation check -- that happens at session boundaries (contract line 196)

**Test:**
```csharp
[Fact]
public async Task UpdateSignals_AccumulatesAccuracyTrail()
{
    var actor = CreateTestStagnationDetector();

    for (int i = 0; i < 25; i++)
    {
        await SendMessage(actor, new UpdateSignals(
            "stu-1", "c-1", "s-1",
            i % 2 == 0, 3000 + i * 100, ErrorType.None, 10, null, 0.5, 3000));
    }

    var state = actor.ConceptStates["c-1"];
    Assert.Equal(20, state.AccuracyTrail.Count); // Capped at 20
    Assert.Equal(20, state.ResponseTimeTrail.Count);
}

[Fact]
public async Task UpdateSignals_TracksErrorTypes()
{
    var actor = CreateTestStagnationDetector();

    await SendMessage(actor, new UpdateSignals(
        "stu-1", "c-1", "s-1", false, 5000,
        ErrorType.Conceptual, 10, null, 0.5, 5000));
    await SendMessage(actor, new UpdateSignals(
        "stu-1", "c-1", "s-1", false, 5000,
        ErrorType.None, 10, null, 0.5, 5000)); // None = not added

    Assert.Single(actor.ConceptStates["c-1"].RecentErrorTypes);
    Assert.Equal("Conceptual", actor.ConceptStates["c-1"].RecentErrorTypes[0]);
}

[Fact]
public async Task UpdateSignals_UpdatesSentiment()
{
    var actor = CreateTestStagnationDetector();

    await SendMessage(actor, new UpdateSignals(
        "stu-1", "c-1", "s-1", true, 3000, ErrorType.None, 10,
        annotationSentiment: 0.3, 0.5, 3000));

    Assert.Equal(0.3, actor.ConceptStates["c-1"].LatestAnnotationSentiment);
}

[Fact]
public async Task UpdateSignals_DoesNotTriggerCheckStagnation()
{
    var actor = CreateTestStagnationDetector();

    // Send many failing signals
    for (int i = 0; i < 30; i++)
    {
        await SendMessage(actor, new UpdateSignals(
            "stu-1", "c-1", "s-1", false, 10000,
            ErrorType.Conceptual, 10, 0.1, 0.5, 3000));
    }

    // No StagnationDetected sent to parent
    var parentMessages = GetParentMessages();
    Assert.DoesNotContain(parentMessages, m => m is StagnationDetected);
}
```

---

### ACT-004.3: CheckStagnation -- 5-Signal Composite Score
**Files to create/modify:**
- `src/Cena.Actors/Stagnation/StagnationDetectorActor.cs` -- `HandleCheckStagnation()`, `ComputeSignals()`, and all 5 signal computation methods

**Acceptance:**
- [ ] **Signal 1 -- Accuracy Plateau**: `sigmoid(10 * (adaptive_threshold - improvement_rate))` where `improvement_rate = (recent - baseline) / baseline` (contract lines 367-388)
- [ ] Adaptive threshold: `max(0.02, _studentAvgImprovementRate * 0.5)` -- NOT fixed 0.05 (contract lines 382-384)
- [ ] Returns 0.0 if `AccuracyTrail.Count < 4` (contract line 369)
- [ ] **Signal 2 -- Response Time Drift**: `(recent_median - baseline_median) / baseline_median`, clamped [0,1] (contract lines 397-409)
- [ ] Returns 0.0 if `ResponseTimeTrail.Count < 4` (contract line 399)
- [ ] **Signal 3 -- Session Abandonment**: `1 - (recent_median_duration / baseline_median_duration)`, clamped [0,1] (contract lines 418-429)
- [ ] Returns 0.0 if `SessionDurationTrail.Count < 2` (contract line 420)
- [ ] **Signal 4 -- Error Type Repetition**: `most_common_count / 5`, clamped [0,1] (contract lines 441-452)
- [ ] Returns 0.0 if `RecentErrorTypes` empty (contract line 443)
- [ ] **Signal 5 -- Annotation Sentiment**: `1.0 - sentiment_score` (inverted) (contract lines 463-468)
- [ ] Returns 0.0 if sentiment is null (contract line 465)
- [ ] Composite score: weighted sum of 5 signals, clamped [0,1] (contract lines 269-276)
- [ ] Score history: FIFO cap at 3 entries (contract line 282)
- [ ] Consecutive count incremented when `composite > StagnationThreshold` (0.7), reset to 0 otherwise (contract lines 288-295)
- [ ] `StagnationDetected` sent to parent when `ConsecutiveStagnantSessions >= ConsecutiveSessionsRequired` (3) (contract lines 307-321)
- [ ] Response includes `StagnationCheckResult` with score, signals, consecutive count, and recommended action (contract lines 325-334)

**Test:**
```csharp
[Fact]
public void AccuracyPlateau_FlatAccuracy_HighScore()
{
    var state = CreateConceptState();
    // Baseline: 50% accuracy, Recent: 50% accuracy (no improvement)
    state.AccuracyTrail = Enumerable.Repeat(0.0, 10)
        .Concat(Enumerable.Repeat(1.0, 10).Select((_, i) => i < 5 ? 1.0 : 0.0))
        .ToList();
    // Make first half ~50%, second half ~50%
    state.AccuracyTrail = new List<double> {
        1, 0, 1, 0, 1, 0, 1, 0, 1, 0, // baseline ~0.5
        1, 0, 1, 0, 1, 0, 1, 0, 1, 0  // recent ~0.5
    };

    double signal = ComputeAccuracyPlateau(state);
    Assert.True(signal > 0.5, $"Flat accuracy should give high plateau signal, got {signal:F3}");
}

[Fact]
public void AccuracyPlateau_Improving_LowScore()
{
    var state = CreateConceptState();
    state.AccuracyTrail = new List<double> {
        0, 0, 0, 0, 0, 0, 0, 0, 1, 0, // baseline ~0.1
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1  // recent ~1.0
    };

    double signal = ComputeAccuracyPlateau(state);
    Assert.True(signal < 0.3, $"Improving accuracy should give low signal, got {signal:F3}");
}

[Fact]
public void ResponseTimeDrift_GettingSlower_HighScore()
{
    var state = CreateConceptState();
    state.ResponseTimeTrail = new List<int> {
        3000, 3000, 3000, 3000, 3000,  // baseline
        3000, 3000, 3000, 3000, 3000,
        6000, 6000, 6000, 6000, 6000,  // recent: 2x slower
        6000, 6000, 6000, 6000, 6000
    };

    double signal = ComputeResponseTimeDrift(state);
    Assert.True(signal > 0.7, $"2x slower should give high drift signal, got {signal:F3}");
}

[Fact]
public void ErrorRepetition_SameErrorRepeated_HighScore()
{
    var state = CreateConceptState();
    state.RecentErrorTypes = new List<string> {
        "Conceptual", "Conceptual", "Conceptual", "Conceptual", "Conceptual",
        "Procedural", "Conceptual"
    };

    double signal = ComputeErrorRepetition(state);
    // Conceptual appears 6 times, signal = min(1, 6/5) = 1.0
    Assert.Equal(1.0, signal);
}

[Fact]
public void AnnotationSentiment_Frustrated_HighScore()
{
    var state = CreateConceptState();
    state.LatestAnnotationSentiment = 0.1; // Very negative

    double signal = ComputeAnnotationSentiment(state);
    Assert.Equal(0.9, signal, 2); // 1.0 - 0.1 = 0.9
}

[Fact]
public async Task CheckStagnation_3ConsecutiveSessions_TriggersDetection()
{
    var actor = CreateTestStagnationDetector();
    PopulateStagnatingData(actor, "c-1"); // All signals high

    // 3 consecutive session checks above threshold
    for (int i = 0; i < 3; i++)
    {
        var result = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
            new CheckStagnation("stu-1", "c-1"));
        Assert.True(result.Data!.CompositeScore > 0.7);
    }

    // Parent received StagnationDetected
    var parentMessages = GetParentMessages();
    Assert.Contains(parentMessages, m => m is StagnationDetected sd
        && sd.ConceptId == "c-1"
        && sd.ConsecutiveStagnantSessions == 3);
}

[Fact]
public async Task CheckStagnation_ScoreBelowThreshold_ResetsConsecutive()
{
    var actor = CreateTestStagnationDetector();

    // 2 stagnating sessions
    PopulateStagnatingData(actor, "c-1");
    await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "c-1"));
    await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "c-1"));

    // Now add improving data
    PopulateImprovingData(actor, "c-1");
    var result = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "c-1"));

    Assert.Equal(0, result.Data!.ConsecutiveStagnantSessions);
    Assert.False(result.Data.IsStagnating);
}

[Fact]
public void AccuracyPlateau_TooFewDataPoints_ReturnsZero()
{
    var state = CreateConceptState();
    state.AccuracyTrail = new List<double> { 1, 0, 1 }; // Only 3

    double signal = ComputeAccuracyPlateau(state);
    Assert.Equal(0.0, signal);
}
```

**Edge cases:**
- `baselineAccuracy` is 0 -> `improvementRate` returns 0.0 (avoid division by zero, contract line 376)
- `baselineMedian` RT is 0 -> returns 0.0 (contract line 405)
- `SessionDurationTrail` has only 1 entry -> `SessionAbandonment` returns 0.0 (contract line 420)
- No error types recorded -> `ErrorRepetition` returns 0.0 (contract line 443)
- Composite score exactly 0.7 -> NOT stagnating (threshold is `>`, not `>=`, contract line 286)

---

### ACT-004.4: Cooldown After Methodology Switch
**Files to create/modify:**
- `src/Cena.Actors/Stagnation/StagnationDetectorActor.cs` -- `HandleResetAfterSwitch()`

**Acceptance:**
- [ ] Resets `ConsecutiveStagnantSessions` to 0 (contract line 490)
- [ ] Clears `CompositeScoreHistory` (contract line 491)
- [ ] Sets `CooldownSessionsRemaining = cmd.CooldownSessions` (default 3) (contract line 494)
- [ ] Records `CooldownMethodology = cmd.NewMethodology` for tracking (contract line 495)
- [ ] Clears `RecentErrorTypes` -- fresh start for new methodology (contract line 498)
- [ ] During cooldown: `HandleCheckStagnation` decrements cooldown counter and skips all signal computation, returns score 0.0 with cooldown status message (contract lines 248-263)
- [ ] Responds with `ActorResult(true)` (contract line 505)

**Test:**
```csharp
[Fact]
public async Task ResetAfterSwitch_ClearsStagnationState()
{
    var actor = CreateTestStagnationDetector();
    PopulateStagnatingData(actor, "c-1");

    // Build up consecutive sessions
    await SendMessage(actor, new CheckStagnation("stu-1", "c-1"));
    await SendMessage(actor, new CheckStagnation("stu-1", "c-1"));

    // Reset
    await SendMessage(actor, new ResetAfterSwitch(
        "stu-1", "c-1", Methodology.Feynman, CooldownSessions: 3));

    var state = actor.ConceptStates["c-1"];
    Assert.Equal(0, state.ConsecutiveStagnantSessions);
    Assert.Empty(state.CompositeScoreHistory);
    Assert.Empty(state.RecentErrorTypes);
    Assert.Equal(3, state.CooldownSessionsRemaining);
    Assert.Equal(Methodology.Feynman, state.CooldownMethodology);
}

[Fact]
public async Task Cooldown_SkipsStagnationCheckForNSessions()
{
    var actor = CreateTestStagnationDetector();
    PopulateStagnatingData(actor, "c-1");

    await SendMessage(actor, new ResetAfterSwitch(
        "stu-1", "c-1", Methodology.WorkedExample, CooldownSessions: 3));

    // 3 checks during cooldown -- all skipped
    for (int i = 0; i < 3; i++)
    {
        var result = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
            new CheckStagnation("stu-1", "c-1"));
        Assert.False(result.Data!.IsStagnating);
        Assert.Equal(0.0, result.Data.CompositeScore);
    }

    Assert.Equal(0, actor.ConceptStates["c-1"].CooldownSessionsRemaining);

    // 4th check: cooldown expired, real check happens
    PopulateStagnatingData(actor, "c-1");
    var realResult = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "c-1"));
    Assert.True(realResult.Data!.CompositeScore > 0);
}

[Fact]
public async Task ResetAfterSwitch_CustomCooldownSessions()
{
    var actor = CreateTestStagnationDetector();

    await SendMessage(actor, new ResetAfterSwitch(
        "stu-1", "c-1", Methodology.Analogy, CooldownSessions: 5));

    Assert.Equal(5, actor.ConceptStates["c-1"].CooldownSessionsRemaining);
}

[Fact]
public async Task Cooldown_DoesNotAffectOtherConcepts()
{
    var actor = CreateTestStagnationDetector();
    PopulateStagnatingData(actor, "c-1");
    PopulateStagnatingData(actor, "c-2");

    // Reset only c-1
    await SendMessage(actor, new ResetAfterSwitch(
        "stu-1", "c-1", Methodology.Feynman, CooldownSessions: 3));

    // c-2 still detects stagnation
    var c2Result = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "c-2"));
    Assert.True(c2Result.Data!.CompositeScore > 0);
}
```

**Edge cases:**
- `ResetAfterSwitch` on a concept with no prior state -> creates state, sets cooldown
- Cooldown of 0 sessions -> next check runs normally
- Multiple resets on same concept -> latest cooldown wins

---

## Integration Test (full stagnation lifecycle)

```csharp
[Fact]
public async Task StagnationDetector_FullLifecycle()
{
    var actor = CreateTestStagnationDetector();

    // Phase 1: Accumulate signals over 3 sessions (student struggling)
    for (int session = 0; session < 3; session++)
    {
        // 10 attempts per session: slow, incorrect, same error type
        for (int i = 0; i < 10; i++)
        {
            await SendMessage(actor, new UpdateSignals(
                "stu-1", "algebra-1", $"session-{session}",
                false, 12000 + session * 2000, ErrorType.Conceptual,
                5 - session, // Shortening sessions (abandonment)
                0.2, 0.7, 4000));
        }

        var result = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
            new CheckStagnation("stu-1", "algebra-1"));

        if (session < 2)
        {
            Assert.False(result.Data!.IsStagnating ||
                result.Data.ConsecutiveStagnantSessions < 3);
        }
        else
        {
            // 3rd consecutive session -> triggers
            Assert.True(result.Data!.ConsecutiveStagnantSessions >= 3);
        }
    }

    // Phase 2: Parent received StagnationDetected
    var parentMessages = GetParentMessages();
    var detection = parentMessages.OfType<StagnationDetected>().Last();
    Assert.Equal("algebra-1", detection.ConceptId);
    Assert.True(detection.CompositeScore > 0.7);

    // Phase 3: Methodology switch -> reset with cooldown
    await SendMessage(actor, new ResetAfterSwitch(
        "stu-1", "algebra-1", Methodology.Feynman, CooldownSessions: 3));

    // Phase 4: Cooldown period
    for (int i = 0; i < 3; i++)
    {
        var cooldownResult = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
            new CheckStagnation("stu-1", "algebra-1"));
        Assert.False(cooldownResult.Data!.IsStagnating);
    }

    // Phase 5: Post-cooldown, student improving -> no stagnation
    for (int i = 0; i < 10; i++)
    {
        await SendMessage(actor, new UpdateSignals(
            "stu-1", "algebra-1", "session-improving",
            true, 3000, ErrorType.None, 15, 0.8, 0.7, 4000));
    }

    var finalResult = await SendMessage<ActorResult<StagnationCheckResult>>(actor,
        new CheckStagnation("stu-1", "algebra-1"));
    Assert.False(finalResult.Data!.IsStagnating);
    Assert.Equal(0, finalResult.Data.ConsecutiveStagnantSessions);
}
```

## Performance Benchmarks
- `ComputeSignals()` for one concept: < 50 microseconds
- `HandleUpdateSignals`: < 10 microseconds (append to lists)
- `HandleCheckStagnation` full cycle: < 100 microseconds
- Memory per tracked concept: ~2KB (trails + history)
- 500 concepts tracked per student: ~1MB (within actor budget)

## Rollback Criteria
- If stagnation detects too aggressively (false positives): raise threshold from 0.7 to 0.8, increase consecutive sessions from 3 to 5
- If stagnation never triggers (false negatives): lower threshold to 0.6, reduce consecutive sessions to 2
- If adaptive threshold causes instability: revert to fixed 0.05 improvement rate
- If memory per concept too high: reduce trail sizes from 20 to 10

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=StagnationDetector"` -> 0 failures
- [ ] Each signal individually verified against 2 hand-calculated scenarios
- [ ] Composite score verified against 3 known-stagnating + 3 known-improving test profiles
- [ ] Cooldown enforcement verified end-to-end
- [ ] PR reviewed by architect (you)
