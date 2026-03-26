# ACT-003: LearningSessionActor (Classic, Session-Scoped)

**Priority:** P0 — hot path for all student interactions
**Blocked by:** ACT-001 (cluster), ACT-002 (StudentActor parent), DATA-002 (event types)
**Estimated effort:** 4 days
**Contract:** `contracts/actors/learning_session_actor.cs`, `contracts/backend/actor-contracts.cs` (lines 362-482)

---

## Context
The LearningSessionActor is a classic (non-virtual) child actor spawned by the StudentActor on `StartSession` and destroyed on `EndSession` or timeout. It owns the critical hot path: inline BKT updates (~100ns, zero allocations), fatigue scoring with a 5-signal sliding window, and KST-based item selection. All state is transient -- the parent StudentActor persists domain events to Marten. This actor must be fast, correct, and memory-efficient because every question attempt flows through it.

## Subtasks

### ACT-003.1: Actor Scaffold + Lifecycle (Started / Stopping / Stopped)
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- main actor class
- `src/Cena.Actors/Sessions/LearningSessionState.cs` -- transient state record
- `src/Cena.Actors/Sessions/BktParameters.cs` -- 4-parameter BKT model
- `src/Cena.Actors/Sessions/FatigueDataPoint.cs` -- fatigue window data point
- `src/Cena.Actors/Sessions/ConceptQueueItem.cs` -- item selection queue entry

**Acceptance:**
- [ ] `LearningSessionActor : IActor` with constructor accepting `sessionId`, `studentId`, `Methodology`, `StudentState` (read-only ref), and `ILogger<LearningSessionActor>` (contract lines 182-205)
- [ ] `ReceiveAsync` dispatches: `Started`, `Stopping`, `Stopped`, `PresentNextQuestion`, `EvaluateAnswer`, `RequestHint`, `SkipQuestion`, `SessionTimeoutTick`, `GetSessionSummary` (contract lines 210-228)
- [ ] `OnStarted`: initializes `_timeoutCts`, schedules timeout via `Task.Delay(45 min)` -> `SessionTimeoutTick` (contract lines 234-253)
- [ ] `OnStarted`: initializes BKT cache from `studentState.MasteryMap` (contract lines 200-205)
- [ ] `OnStarted`: calls `RefreshConceptQueue()` to build initial queue
- [ ] `OnStopping`: cancels and disposes `_timeoutCts`, logs session summary metrics (contract lines 255-267)
- [ ] `LearningSessionState` has all fields from contract lines 36-91: `SessionId`, `StudentId`, `ActiveMethodology`, `StartedAt`, `CurrentQuestionId`, `CurrentConceptId`, `CurrentDifficulty`, `CurrentHintLevel`, `QuestionPresentedAt`, `QuestionsAttempted`, `QuestionsCorrect`, `QuestionsSkipped`, `TotalHintsUsed`, `QuestionIndex`, `ResponseTimesMs`, `FatigueScore`, `FatigueWindow`, `BktCache`, `ConceptQueue`
- [ ] Constants: `MaxSessionDurationMinutes = 45`, `FatigueTerminationThreshold = 0.8`, `ConceptQueueRefreshInterval = 5` (contract lines 88-90)

**Test:**
```csharp
[Fact]
public async Task SessionActor_InitializesStateOnStarted()
{
    var studentState = CreateTestStudentState(concepts: 10);
    var actor = CreateTestSessionActor("session-1", "student-1",
        Methodology.Socratic, studentState);

    await ActivateActor(actor);

    var summary = await RequestSessionSummary(actor);
    Assert.Equal(0, summary.QuestionsAttempted);
    Assert.Equal(0.0, summary.FatigueScore);
}

[Fact]
public async Task SessionActor_InitializesBktCacheFromMasteryMap()
{
    var studentState = CreateTestStudentState(concepts: 5);
    studentState.MasteryMap["algebra-1"] = 0.72;

    var actor = CreateTestSessionActor("s-2", "stu-2",
        Methodology.Socratic, studentState);
    await ActivateActor(actor);

    // BKT cache should have the student's prior mastery
    Assert.Equal(0.72, actor.State.BktCache["algebra-1"].PKnown);
}

[Fact]
public async Task SessionActor_TimeoutSendsEndSessionToParent()
{
    // Use short timeout for testing
    var actor = CreateTestSessionActor("s-3", "stu-3",
        Methodology.Socratic, CreateTestStudentState(),
        timeoutMinutes: 1); // Override for test

    await ActivateActor(actor);
    await Task.Delay(TimeSpan.FromSeconds(2)); // Test uses 1-second timeout

    var parentMessages = GetParentMessages();
    Assert.Contains(parentMessages, m => m is EndSession es
        && es.Reason == SessionEndReason.Timeout);
}
```

**Edge cases:**
- `studentState.MasteryMap` is empty (new student) -> BKT cache starts empty, `RefreshConceptQueue()` returns no items
- `StudentState` reference is null -> constructor throws `ArgumentNullException` (contract line 190)
- Timeout CTS disposed while `Task.Delay` in flight -> no crash, `IsCanceled` check prevents send (contract line 246)

---

### ACT-003.2: Inline BKT Update (Hot Path, Zero Allocation)
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- `BktUpdate()` and `GetOrCreateBktParams()` methods

**Acceptance:**
- [ ] `BktUpdate(BktParameters bkt, bool isCorrect)` implements standard Corbett & Anderson (1994) 4-parameter model (contract lines 492-527)
- [ ] Correct answer posterior: `P(L_n|correct) = P(L_n)*(1-P(S)) / [P(L_n)*(1-P(S)) + (1-P(L_n))*P(G)]` (contract lines 480-481)
- [ ] Incorrect answer posterior: `P(L_n|incorrect) = P(L_n)*P(S) / [P(L_n)*P(S) + (1-P(L_n))*(1-P(G))]` (contract lines 482-483)
- [ ] Learning transition: `P(L_{n+1}) = P(L_n|obs) + (1-P(L_n|obs)) * P(T)` (contract line 520)
- [ ] Clamped to `[0.01, 0.99]` to avoid degenerate states (contract line 523)
- [ ] Zero heap allocations in `BktUpdate()` -- static method, value-type arithmetic only
- [ ] `GetOrCreateBktParams()` initializes from `_studentState.MasteryMap` with defaults: `PLearn=0.1`, `PGuess=0.25`, `PSlip=0.1` (contract lines 532-549)
- [ ] Performance: < 1 microsecond per call (contract line 487: "~100ns per call")
- [ ] `BktUpdateLatency` histogram recorded in microseconds (contract line 173)

**Test:**
```csharp
[Fact]
public void BktUpdate_CorrectAnswer_IncreasesKnowledge()
{
    var bkt = new BktParameters { PKnown = 0.5, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1 };
    double prior = bkt.PKnown;

    double posterior = LearningSessionActor.BktUpdate(bkt, isCorrect: true);

    Assert.True(posterior > prior);
    Assert.Equal(posterior, bkt.PKnown); // Mutates in place
}

[Fact]
public void BktUpdate_IncorrectAnswer_DecreasesKnowledge()
{
    var bkt = new BktParameters { PKnown = 0.7, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1 };
    double prior = bkt.PKnown;

    double posterior = LearningSessionActor.BktUpdate(bkt, isCorrect: false);

    Assert.True(posterior < prior);
}

[Fact]
public void BktUpdate_ClampsToBounds()
{
    var bktHigh = new BktParameters { PKnown = 0.999, PLearn = 0.5, PGuess = 0.01, PSlip = 0.01 };
    double high = LearningSessionActor.BktUpdate(bktHigh, isCorrect: true);
    Assert.True(high <= 0.99);

    var bktLow = new BktParameters { PKnown = 0.001, PLearn = 0.001, PGuess = 0.99, PSlip = 0.99 };
    double low = LearningSessionActor.BktUpdate(bktLow, isCorrect: false);
    Assert.True(low >= 0.01);
}

[Fact]
public void BktUpdate_DenominatorZero_ReturnsPrior()
{
    // Edge case: P(L_n) = 0 and P(G) = 0 -> denominator = 0
    var bkt = new BktParameters { PKnown = 0.0, PLearn = 0.0, PGuess = 0.0, PSlip = 0.0 };
    double result = LearningSessionActor.BktUpdate(bkt, isCorrect: true);
    // Should not crash; returns clamped value
    Assert.True(result >= 0.01 && result <= 0.99);
}

[Fact]
public void BktUpdate_Performance_Under1Microsecond()
{
    var bkt = new BktParameters { PKnown = 0.5, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1 };
    var sw = Stopwatch.StartNew();

    for (int i = 0; i < 100_000; i++)
    {
        bkt.PKnown = 0.5; // Reset
        LearningSessionActor.BktUpdate(bkt, i % 2 == 0);
    }

    sw.Stop();
    double avgNs = sw.Elapsed.TotalNanoseconds / 100_000;
    Assert.True(avgNs < 1000, $"BKT update avg {avgNs:F0}ns exceeds 1us budget");
}
```

**Edge cases:**
- All BKT params set to 0 -> denominator guard returns prior, then clamped
- P(known) already at 0.99 and correct answer -> stays at 0.99 (clamp)
- Concept not in BKT cache and not in student mastery map -> defaults to `PriorKnown = 0.3` (contract line 537)

---

### ACT-003.3: Fatigue Score Computation (5-Signal Weighted Composite)
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- `ComputeFatigueScore()` method
- `src/Cena.Actors/Sessions/FatigueDataPoint.cs` -- sliding window data type

**Acceptance:**
- [ ] `FatigueDataPoint` record with fields: `ResponseTimeMs`, `IsCorrect`, `HintsUsed`, `WasSkipped`, `BackspaceCount`, `AnswerChangeCount`, `Timestamp` (contract lines 96-103)
- [ ] Sliding window: last 5 questions (contract line 571)
- [ ] Returns 0.0 if fewer than 2 data points in window (contract line 576)
- [ ] Signal 1 -- Response time drift: `(windowMedianRt - overallMedianRt) / overallMedianRt`, clamped [0,1] (contract lines 578-583)
- [ ] Signal 2 -- Accuracy decline: `overallAccuracy - windowAccuracy`, clamped [0,1] (contract lines 586-591)
- [ ] Signal 3 -- Hint dependency: `avgHints / 3.0`, clamped [0,1] (contract lines 593-595)
- [ ] Signal 4 -- Skip rate: `windowSkips / windowCount` (contract lines 597-598)
- [ ] Signal 5 -- Behavioral uncertainty: `(avgBackspace + avgChanges) / 20.0`, clamped [0,1] (contract lines 600-603)
- [ ] Weights: RT=0.25, Accuracy=0.25, Hints=0.20, Skip=0.15, Uncertainty=0.15 (contract lines 606-610)
- [ ] Session duration amplifier: after 20 minutes, fatigue multiplied by `1 + ((minutes-20)/25)*0.3`, max 1.3x (contract lines 618-624)
- [ ] Final score clamped to [0.0, 1.0] (contract line 626)
- [ ] `FatigueScoreHistogram` metric recorded per computation (contract line 428)

**Test:**
```csharp
[Fact]
public void FatigueScore_ReturnsZero_WhenFewerThan2DataPoints()
{
    var state = CreateSessionState();
    state.FatigueWindow.Add(CreateFatiguePoint(correct: true, rtMs: 5000));

    double score = ComputeFatigueScore(state);
    Assert.Equal(0.0, score);
}

[Fact]
public void FatigueScore_HighForSlowInaccurateSession()
{
    var state = CreateSessionState(overallMedianRt: 3000, overallAccuracy: 0.8);
    // Window: slow, incorrect, lots of hints and skips
    for (int i = 0; i < 5; i++)
    {
        state.FatigueWindow.Add(CreateFatiguePoint(
            correct: false, rtMs: 9000, hints: 3, skipped: true,
            backspace: 15, answerChanges: 5));
    }

    double score = ComputeFatigueScore(state);
    Assert.True(score > 0.7, $"Expected high fatigue, got {score:F3}");
}

[Fact]
public void FatigueScore_LowForFastAccurateSession()
{
    var state = CreateSessionState(overallMedianRt: 5000, overallAccuracy: 0.7);
    for (int i = 0; i < 5; i++)
    {
        state.FatigueWindow.Add(CreateFatiguePoint(
            correct: true, rtMs: 4000, hints: 0, skipped: false));
    }

    double score = ComputeFatigueScore(state);
    Assert.True(score < 0.3, $"Expected low fatigue, got {score:F3}");
}

[Fact]
public void FatigueScore_DurationAmplifier_IncreasesAfter20Minutes()
{
    var state = CreateSessionState(overallMedianRt: 5000, overallAccuracy: 0.6,
        startedMinutesAgo: 40); // 40 minutes in

    for (int i = 0; i < 5; i++)
        state.FatigueWindow.Add(CreateFatiguePoint(correct: false, rtMs: 8000));

    double scoreAt40 = ComputeFatigueScore(state);

    // Same signals but at 10 minutes (no amplifier)
    state = CreateSessionState(overallMedianRt: 5000, overallAccuracy: 0.6,
        startedMinutesAgo: 10);
    for (int i = 0; i < 5; i++)
        state.FatigueWindow.Add(CreateFatiguePoint(correct: false, rtMs: 8000));

    double scoreAt10 = ComputeFatigueScore(state);

    Assert.True(scoreAt40 > scoreAt10,
        $"40min score ({scoreAt40:F3}) should exceed 10min score ({scoreAt10:F3})");
}

[Fact]
public void FatigueScore_WindowLimitedToLast5()
{
    var state = CreateSessionState(overallMedianRt: 5000, overallAccuracy: 0.5);
    // Add 20 good points, then 5 bad points
    for (int i = 0; i < 20; i++)
        state.FatigueWindow.Add(CreateFatiguePoint(correct: true, rtMs: 4000));
    for (int i = 0; i < 5; i++)
        state.FatigueWindow.Add(CreateFatiguePoint(correct: false, rtMs: 12000,
            hints: 3, skipped: true));

    double score = ComputeFatigueScore(state);
    // Only last 5 (bad) points are in the window
    Assert.True(score > 0.5, $"Window should only see bad points, got {score:F3}");
}
```

**Edge cases:**
- `overallMedianRt` is 0 -> `rtDrift` returns 0.0 (no division by zero)
- `QuestionsAttempted` is 0 -> `overallAccuracy` defaults to 0.5 (contract line 589)
- All 5 window points are skips -> `skipRate = 1.0`, `accuracy = 0.0`

---

### ACT-003.4: Item Selection (KST + BKT Priority Queue)
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- `RefreshConceptQueue()` and `HandlePresentNextQuestion()`

**Acceptance:**
- [ ] `RefreshConceptQueue()` clears and rebuilds `ConceptQueue` (contract line 775)
- [ ] Priority = Zone of Proximal Development score: `1 - 4*(pKnown - 0.5)^2` (peak at P=0.5) (contract line 791)
- [ ] Review boost: if HLR timer exists and recall < 0.85, add `0.3 * (1 - recall)` (contract lines 796-805)
- [ ] Novelty boost: +0.1 for concepts not yet in BKT cache (contract line 808)
- [ ] Sorted descending by priority, top 10 enqueued (contract lines 823-824)
- [ ] Difficulty mapped from P(known): `<0.4->Recall`, `0.4-0.6->Comprehension`, `0.6-0.8->Application`, `>=0.8->Analysis` (contract lines 813-819)
- [ ] Queue refreshed every 5 questions or when empty (contract lines 324-328, constant at line 90)
- [ ] `HandlePresentNextQuestion`: checks fatigue threshold first (contract lines 293-308)
- [ ] `HandlePresentNextQuestion`: checks session duration against 45-min max (contract lines 311-321)
- [ ] `HandlePresentNextQuestion`: dequeues next item, updates current question state (contract lines 339-361)
- [ ] Empty queue returns `NO_QUESTIONS` error (contract lines 331-336)

**Test:**
```csharp
[Fact]
public void ConceptQueue_PrioritizesZoneOfProximalDevelopment()
{
    var studentState = CreateTestStudentState();
    studentState.MasteryMap["c1"] = 0.5;  // Peak ZPD
    studentState.MasteryMap["c2"] = 0.1;  // Too easy
    studentState.MasteryMap["c3"] = 0.95; // Nearly mastered

    var actor = CreateTestSessionActor(studentState: studentState);
    var queue = actor.State.ConceptQueue.ToList();

    Assert.Equal("c1", queue[0].ConceptId); // P=0.5 has highest ZPD
}

[Fact]
public void ConceptQueue_AppliesReviewBoost()
{
    var studentState = CreateTestStudentState();
    studentState.MasteryMap["c1"] = 0.5;
    studentState.MasteryMap["c2"] = 0.5; // Same ZPD
    studentState.HlrTimers["c2"] = new HlrState
    {
        HalfLifeHours = 24,
        LastReviewAt = DateTimeOffset.UtcNow.AddDays(-3) // Recall decayed
    };

    var actor = CreateTestSessionActor(studentState: studentState);
    var queue = actor.State.ConceptQueue.ToList();

    // c2 should be first due to review boost
    Assert.Equal("c2", queue[0].ConceptId);
    Assert.True(queue[0].IsReview);
}

[Fact]
public void ConceptQueue_MapsDifficultyFromMastery()
{
    var studentState = CreateTestStudentState();
    studentState.MasteryMap["low"] = 0.2;
    studentState.MasteryMap["mid"] = 0.5;
    studentState.MasteryMap["high"] = 0.7;
    studentState.MasteryMap["expert"] = 0.85;

    var actor = CreateTestSessionActor(studentState: studentState);
    var items = actor.State.ConceptQueue.ToDictionary(i => i.ConceptId);

    Assert.Equal(DifficultyLevel.Recall, items["low"].Difficulty);
    Assert.Equal(DifficultyLevel.Comprehension, items["mid"].Difficulty);
    Assert.Equal(DifficultyLevel.Application, items["high"].Difficulty);
    Assert.Equal(DifficultyLevel.Analysis, items["expert"].Difficulty);
}

[Fact]
public async Task PresentNextQuestion_ReturnsFatigueError_WhenThresholdExceeded()
{
    var actor = CreateTestSessionActor();
    actor.State.FatigueScore = 0.85; // Above 0.8 threshold

    var result = await SendMessage<ActorResult<PresentExercise>>(
        actor, new PresentNextQuestion());

    Assert.False(result.Success);
    Assert.Equal("FATIGUE_THRESHOLD", result.ErrorCode);
}

[Fact]
public async Task PresentNextQuestion_ReturnsTimeoutError_After45Minutes()
{
    var actor = CreateTestSessionActor();
    actor.State.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-46);

    var result = await SendMessage<ActorResult<PresentExercise>>(
        actor, new PresentNextQuestion());

    Assert.False(result.Success);
    Assert.Equal("SESSION_TIMEOUT", result.ErrorCode);
}

[Fact]
public async Task PresentNextQuestion_RefreshesQueueEvery5Questions()
{
    var actor = CreateTestSessionActor(studentState: CreateTestStudentState(concepts: 20));
    actor.State.QuestionIndex = 4; // Next call is question 5

    await SendMessage<ActorResult<PresentExercise>>(actor, new PresentNextQuestion());

    // Queue was refreshed (QuestionIndex % 5 == 0 after increment)
    Assert.True(actor.State.ConceptQueue.Count > 0);
}
```

**Edge cases:**
- Student has no concepts in mastery map -> queue is empty, returns `NO_QUESTIONS`
- All concepts mastered at 0.99 -> still enqueued (for review), difficulty = Analysis
- HLR timer `LastReviewAt` far in the future (clock skew) -> recall > 1.0, no review boost

---

### ACT-003.5: EvaluateAnswer + Hint + Skip Handlers
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- `HandleEvaluateAnswer()`, `HandleRequestHint()`, `HandleSkipQuestion()`

**Acceptance:**
- [ ] `HandleEvaluateAnswer`: validates `cmd.QuestionId` matches `_state.CurrentQuestionId`, returns `QUESTION_MISMATCH` error on mismatch (contract lines 391-397)
- [ ] BKT update runs inline with `Stopwatch` measuring microseconds (contract lines 409-413)
- [ ] Fatigue window updated with new `FatigueDataPoint` after each answer (contract lines 421-428)
- [ ] XP calculation: `isCorrect ? Max(2, 10 - hintLevel*2) : (int)(score * 5)` (contract lines 431-433)
- [ ] `nextAction` determination: fatigue >= 0.72 -> `consider_break`, mastery >= 0.85 -> `advance_concept`, mastery < 0.4 and incorrect -> `provide_scaffolding`, else `continue` (contract lines 436-444)
- [ ] Returns `EvaluateAnswerResponse` with all fields (contract lines 447-455)
- [ ] `HandleRequestHint`: validates question ID, max 3 hints, increments `CurrentHintLevel` and `TotalHintsUsed` (contract lines 637-669)
- [ ] `HandleSkipQuestion`: validates question ID, increments `QuestionsSkipped`, adds skip fatigue point (contract lines 680-705)
- [ ] Skip fatigue point: `IsCorrect=false`, `WasSkipped=true`, `HintsUsed=0` (contract lines 693-695)

**Test:**
```csharp
[Fact]
public async Task EvaluateAnswer_UpdatesBktAndReturnsMastery()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");

    var result = await SendMessage<ActorResult<EvaluateAnswerResponse>>(actor,
        new EvaluateAnswer("s-1", "q-1", "answer", 5000, null, 2, 1));

    Assert.True(result.Success);
    Assert.True(result.Data!.UpdatedMastery > 0);
    Assert.Equal(1, actor.State.QuestionsAttempted);
}

[Fact]
public async Task EvaluateAnswer_QuestionMismatch_ReturnsError()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");

    var result = await SendMessage<ActorResult<EvaluateAnswerResponse>>(actor,
        new EvaluateAnswer("s-1", "wrong-q", "answer", 5000, null, 0, 0));

    Assert.False(result.Success);
    Assert.Equal("QUESTION_MISMATCH", result.ErrorCode);
}

[Fact]
public async Task EvaluateAnswer_XpCalculation_ReducedByHints()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");
    actor.State.CurrentHintLevel = 2;

    var result = await SendMessage<ActorResult<EvaluateAnswerResponse>>(actor,
        new EvaluateAnswer("s-1", "q-1", "correct", 3000, null, 0, 0));

    // XP = max(2, 10 - 2*2) = 6
    Assert.Equal(6, result.Data!.XpEarned);
}

[Fact]
public async Task RequestHint_IncrementsHintLevel()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");

    var result = await SendMessage<ActorResult<HintResponse>>(actor,
        new RequestHint("s-1", "q-1", 1));

    Assert.True(result.Success);
    Assert.Equal(1, actor.State.CurrentHintLevel);
    Assert.Equal(1, actor.State.TotalHintsUsed);
}

[Fact]
public async Task RequestHint_Level4_ReturnsMaxHintsError()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");

    var result = await SendMessage<ActorResult<HintResponse>>(actor,
        new RequestHint("s-1", "q-1", 4));

    Assert.False(result.Success);
    Assert.Equal("MAX_HINTS_REACHED", result.ErrorCode);
}

[Fact]
public async Task SkipQuestion_RecordsFatigueAndIncrementsCounter()
{
    var actor = CreateTestSessionActorWithQuestion("q-1", "concept-1");

    var result = await SendMessage<ActorResult>(actor,
        new SkipQuestion("s-1", "q-1", 8000, "too hard"));

    Assert.True(result.Success);
    Assert.Equal(1, actor.State.QuestionsSkipped);
    Assert.True(actor.State.FatigueWindow.Last().WasSkipped);
}
```

**Edge cases:**
- `EvaluateAnswer` after session already timed out -> `CurrentQuestionId` is null, returns mismatch
- All 3 hints used, then `EvaluateAnswer` -> XP reduced to `Max(2, 10-6) = 4`
- Skip with 0 `TimeSpentBeforeSkipMs` -> valid, fatigue RT=0 contributes to drift

---

### ACT-003.6: GetSessionSummary + Timeout Handler
**Files to create/modify:**
- `src/Cena.Actors/Sessions/LearningSessionActor.cs` -- `HandleGetSessionSummary()`, `HandleSessionTimeout()`

**Acceptance:**
- [ ] `HandleGetSessionSummary` responds with `SessionSummary(DurationMinutes, QuestionsAttempted, QuestionsCorrect, AvgResponseTimeMs, FatigueScore, LastConceptId)` (contract lines 735-751, internal message at student_actor.cs lines 1550-1556)
- [ ] Duration computed from `DateTimeOffset.UtcNow - _state.StartedAt` (contract line 737)
- [ ] Average response time: `_state.ResponseTimesMs.Average()` or 0 if empty (contract lines 738-739)
- [ ] `HandleSessionTimeout`: logs with session stats, increments `SessionTimeoutCounter`, sends `EndSession` to parent with `SessionEndReason.Timeout` (contract lines 715-728)
- [ ] Timeout counter metric: `cena.session.timeouts_total` (contract line 177)

**Test:**
```csharp
[Fact]
public async Task GetSessionSummary_ReturnsAccurateStats()
{
    var actor = CreateTestSessionActor();
    // Simulate 3 questions: 2 correct, 1 incorrect
    await SimulateQuestionAnswer(actor, correct: true, rtMs: 3000);
    await SimulateQuestionAnswer(actor, correct: true, rtMs: 5000);
    await SimulateQuestionAnswer(actor, correct: false, rtMs: 7000);

    var summary = await RequestSessionSummary(actor);

    Assert.Equal(3, summary.QuestionsAttempted);
    Assert.Equal(2, summary.QuestionsCorrect);
    Assert.Equal(5000, summary.AvgResponseTimeMs); // (3000+5000+7000)/3 = 5000
    Assert.True(summary.FatigueScore >= 0.0);
}

[Fact]
public async Task GetSessionSummary_EmptySession_ReturnsZeros()
{
    var actor = CreateTestSessionActor();

    var summary = await RequestSessionSummary(actor);

    Assert.Equal(0, summary.QuestionsAttempted);
    Assert.Equal(0, summary.QuestionsCorrect);
    Assert.Equal(0, summary.AvgResponseTimeMs);
}

[Fact]
public async Task SessionTimeout_SendsEndSessionToParent()
{
    var actor = CreateTestSessionActor();
    await ActivateActor(actor);

    await SendMessage(actor, new SessionTimeoutTick());

    var parentMessages = GetParentMessages();
    var endSession = parentMessages.OfType<EndSession>().Single();
    Assert.Equal(SessionEndReason.Timeout, endSession.Reason);
    Assert.Equal(actor.State.StudentId, endSession.StudentId);
}
```

---

## Integration Test (full session lifecycle)

```csharp
[Fact]
public async Task LearningSession_FullLifecycle()
{
    // Setup: student with 10 concepts at varying mastery
    var studentState = CreateTestStudentState(concepts: 10);
    studentState.MasteryMap["algebra-1"] = 0.5;
    studentState.MasteryMap["algebra-2"] = 0.3;
    studentState.HlrTimers["algebra-1"] = new HlrState
    {
        HalfLifeHours = 48, LastReviewAt = DateTimeOffset.UtcNow.AddDays(-5)
    };

    var actor = CreateTestSessionActor("session-e2e", "student-e2e",
        Methodology.Socratic, studentState);
    await ActivateActor(actor);

    // 1. Present first question (should be algebra-1 due to ZPD + review boost)
    var q1 = await SendMessage<ActorResult<PresentExercise>>(actor, new PresentNextQuestion());
    Assert.True(q1.Success);
    Assert.Equal("algebra-1", q1.Data!.ConceptId);
    Assert.True(q1.Data.IsReview);

    // 2. Evaluate answer (correct)
    var eval1 = await SendMessage<ActorResult<EvaluateAnswerResponse>>(actor,
        new EvaluateAnswer("session-e2e", q1.Data.QuestionId, "42", 3000, 4, 1, 0));
    Assert.True(eval1.Success);
    Assert.True(eval1.Data!.UpdatedMastery > 0.5); // BKT increased

    // 3. Request hint on next question
    var q2 = await SendMessage<ActorResult<PresentExercise>>(actor, new PresentNextQuestion());
    Assert.True(q2.Success);
    var hint = await SendMessage<ActorResult<HintResponse>>(actor,
        new RequestHint("session-e2e", q2.Data!.QuestionId, 1));
    Assert.True(hint.Success);

    // 4. Skip a question
    var q3 = await SendMessage<ActorResult<PresentExercise>>(actor, new PresentNextQuestion());
    await SendMessage<ActorResult>(actor,
        new SkipQuestion("session-e2e", q3.Data!.QuestionId, 5000, "confused"));

    // 5. Answer 5 more questions to trigger queue refresh
    for (int i = 0; i < 5; i++)
    {
        var q = await SendMessage<ActorResult<PresentExercise>>(actor, new PresentNextQuestion());
        if (!q.Success) break; // May hit fatigue or run out of questions
        await SendMessage<ActorResult<EvaluateAnswerResponse>>(actor,
            new EvaluateAnswer("session-e2e", q.Data!.QuestionId, "ans", 4000, null, 0, 0));
    }

    // 6. Get summary
    var summary = await RequestSessionSummary(actor);
    Assert.True(summary.QuestionsAttempted >= 7);
    Assert.True(summary.QuestionsCorrect >= 1);
    Assert.True(summary.FatigueScore >= 0.0);
}
```

## Performance Benchmarks
- BKT update: < 1 microsecond per call (P99), zero allocations
- `ComputeFatigueScore()`: < 10 microseconds per call
- `RefreshConceptQueue()`: < 100 microseconds for 2000 concepts
- `PresentNextQuestion` end-to-end: < 500 microseconds (no I/O)
- Memory per session actor: < 50KB (BKT cache + fatigue window + queue)

## Rollback Criteria
- If BKT update produces NaN or divergent results: fall back to fixed mastery increment (+0.05 correct, -0.03 incorrect)
- If fatigue scoring causes too many false terminations: disable duration amplifier, raise threshold to 0.9
- If item selection produces poor concept ordering: fall back to round-robin within mastery tier
- If session actor memory exceeds 50KB: reduce concept queue size from 10 to 5, trim fatigue window to 3

## Definition of Done
- [ ] All 6 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=LearningSession"` -> 0 failures
- [ ] BKT benchmark: 100K updates in < 100ms
- [ ] Fatigue score verified against 3 hand-calculated scenarios
- [ ] Memory: 100 concurrent sessions < 5MB total
- [ ] PR reviewed by architect (you)
