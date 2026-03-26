# ACT-015: Domain Service Implementations (Gap Fill)

**Priority:** P1 — required for StudentActor command handling completeness
**Blocked by:** ACT-002 (StudentActor), DATA-001 (Marten), DATA-006 (Neo4j)
**Estimated effort:** 4 days
**Contract:** `contracts/backend/domain-services.cs`

---

## Context
The domain services are stateless, DI-injected into actors. Several service methods are referenced in contracts but not covered by existing actor tasks. This task fills the gap: BKT parameter hot-reload, HLR full review scheduling, cognitive load cooldown/difficulty adjustment, and the entire prerequisite enforcement service. Each subtask implements the exact interface from `domain-services.cs` with full acceptance criteria and tests.

## Subtasks

### ACT-015.1: IBktService.ReloadParametersAsync (Thread-Safe Hot-Reload)
**Files:**
- `src/Cena.Domain/Services/BktService.cs` — add `ReloadParametersAsync` method
- `src/Cena.Domain/Services/BktParameterCache.cs` — copy-on-write parameter cache

**Acceptance:**
- [ ] `ReloadParametersAsync(CancellationToken)` loads fresh BKT parameters from the offline-trained pyBKT model store (S3 or local file)
- [ ] Thread-safe: uses copy-on-write semantics — build new `ImmutableDictionary<string, BktParameters>`, then swap the reference via `Interlocked.Exchange`
- [ ] Zero downtime: callers using `GetParameters(conceptId)` during reload always get a consistent snapshot (old or new, never partial)
- [ ] If reload fails (I/O error, parse error): log ERROR, keep existing parameters, do NOT throw
- [ ] Logs at INFO level: "BKT parameters reloaded: {count} concepts, took {ms}ms"
- [ ] Metric emitted: `cena.bkt.parameters_reloaded` counter, `cena.bkt.reload_duration_ms` histogram
- [ ] Default parameters returned for concepts missing from the trained model: `BktParameters(conceptId, PLearning: 0.1, PSlip: 0.1, PGuess: 0.25, PForget: 0.02, PInitial: 0.3)`
- [ ] Triggered by NATS event `cena.flywheel.bkt_retrained` (Flywheel 2 completion)

**Test:**
```csharp
[Fact]
public async Task ReloadParametersAsync_SwapsParametersAtomically()
{
    // Arrange
    var store = new InMemoryParameterStore(new Dictionary<string, BktParameters>
    {
        ["algebra-1"] = new("algebra-1", 0.1, 0.1, 0.25, 0.02, 0.3)
    });
    var svc = new BktService(store);

    // Verify initial parameters
    var before = svc.GetParameters("algebra-1");
    Assert.Equal(0.1, before.PLearning);

    // Act: reload with new parameters
    store.UpdateParameters("algebra-1", new("algebra-1", 0.2, 0.1, 0.25, 0.02, 0.3));
    await svc.ReloadParametersAsync();

    // Assert
    var after = svc.GetParameters("algebra-1");
    Assert.Equal(0.2, after.PLearning);
}

[Fact]
public async Task ReloadParametersAsync_ConcurrentReadsAreSafe()
{
    var svc = new BktService(new InMemoryParameterStore(GenerateParameters(1000)));

    // Concurrent reads + reload
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var reloadTask = Task.Run(async () =>
    {
        while (!cts.IsCancellationRequested)
            await svc.ReloadParametersAsync(cts.Token);
    });
    var readTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
    {
        while (!cts.IsCancellationRequested)
        {
            var p = svc.GetParameters("concept-0");
            Assert.NotNull(p);
        }
    }));

    await Task.WhenAll(readTasks.Append(reloadTask));
    // No exceptions = thread-safe
}

[Fact]
public async Task ReloadParametersAsync_OnFailure_KeepsExisting()
{
    var store = new FailingParameterStore();
    var svc = new BktService(store);

    // Initial load works
    await svc.ReloadParametersAsync();
    var before = svc.GetParameters("concept-1");

    // Make store fail
    store.ShouldFail = true;
    await svc.ReloadParametersAsync(); // Should NOT throw

    // Parameters unchanged
    var after = svc.GetParameters("concept-1");
    Assert.Equal(before.PLearning, after.PLearning);
}
```

---

### ACT-015.2: IHalfLifeRegressionService.ComputeReviewSchedule
**Files:**
- `src/Cena.Domain/Services/HalfLifeRegressionService.cs` — add `ComputeReviewSchedule` method

**Acceptance:**
- [ ] Signature: `IReadOnlyList<ReviewScheduleItem> ComputeReviewSchedule(IReadOnlyDictionary<string, double> halfLifeMap, IReadOnlyDictionary<string, DateTimeOffset> lastReviewMap, int maxItems = 10)`
- [ ] For each concept in `halfLifeMap`:
  - Compute `hoursSinceLastReview = (UtcNow - lastReviewMap[conceptId]).TotalHours`
  - Compute `predictedRecall = 2^(-hoursSinceLastReview / halfLifeHours)` via `PredictRecall()`
  - Compute `dueAt` = time when recall drops below 0.85 threshold via `ComputeTimeToThreshold()`
  - Assign priority: "urgent" if recall < 0.5, "standard" if 0.5-0.85, "low" if > 0.85
- [ ] Return list sorted by urgency (lowest `predictedRecall` first), capped at `maxItems`
- [ ] Concepts missing from `lastReviewMap` treated as never-reviewed: `predictedRecall = 0.0`, `dueAt = UtcNow`, priority = "urgent"
- [ ] If `halfLifeMap` is empty, return empty list (no exception)
- [ ] ConceptName resolved from `ICurriculumGraphService` lookup (injected dependency)

**Test:**
```csharp
[Fact]
public void ComputeReviewSchedule_SortsByUrgency()
{
    var svc = new HalfLifeRegressionService(MockGraphService());
    var now = DateTimeOffset.UtcNow;

    var halfLifeMap = new Dictionary<string, double>
    {
        ["concept-a"] = 24.0,  // 24h half-life
        ["concept-b"] = 48.0,  // 48h half-life
        ["concept-c"] = 6.0,   // 6h half-life (decays fast)
    };
    var lastReviewMap = new Dictionary<string, DateTimeOffset>
    {
        ["concept-a"] = now.AddHours(-12), // 50% recall
        ["concept-b"] = now.AddHours(-6),  // ~91% recall
        ["concept-c"] = now.AddHours(-12), // ~25% recall (urgent!)
    };

    var schedule = svc.ComputeReviewSchedule(halfLifeMap, lastReviewMap);

    Assert.Equal("concept-c", schedule[0].ConceptId); // Most urgent
    Assert.Equal("urgent", schedule[0].Priority);
    Assert.Equal("concept-a", schedule[1].ConceptId);
    Assert.Equal("standard", schedule[1].Priority);
    Assert.Equal("concept-b", schedule[2].ConceptId);
    Assert.Equal("low", schedule[2].Priority);
}

[Fact]
public void ComputeReviewSchedule_MissingLastReview_TreatedAsUrgent()
{
    var svc = new HalfLifeRegressionService(MockGraphService());

    var halfLifeMap = new Dictionary<string, double> { ["concept-x"] = 24.0 };
    var lastReviewMap = new Dictionary<string, DateTimeOffset>(); // Empty!

    var schedule = svc.ComputeReviewSchedule(halfLifeMap, lastReviewMap);

    Assert.Single(schedule);
    Assert.Equal("urgent", schedule[0].Priority);
    Assert.Equal(0.0, schedule[0].PredictedRecall);
}

[Fact]
public void ComputeReviewSchedule_RespectsMaxItems()
{
    var svc = new HalfLifeRegressionService(MockGraphService());
    var now = DateTimeOffset.UtcNow;

    var halfLifeMap = Enumerable.Range(0, 50)
        .ToDictionary(i => $"concept-{i}", _ => 24.0);
    var lastReviewMap = Enumerable.Range(0, 50)
        .ToDictionary(i => $"concept-{i}", i => now.AddHours(-i));

    var schedule = svc.ComputeReviewSchedule(halfLifeMap, lastReviewMap, maxItems: 5);

    Assert.Equal(5, schedule.Count);
}

[Fact]
public void ComputeReviewSchedule_EmptyInput_ReturnsEmpty()
{
    var svc = new HalfLifeRegressionService(MockGraphService());
    var schedule = svc.ComputeReviewSchedule(
        new Dictionary<string, double>(),
        new Dictionary<string, DateTimeOffset>());

    Assert.Empty(schedule);
}
```

---

### ACT-015.3: ICognitiveLoadService.ComputeCooldownMinutes
**Files:**
- `src/Cena.Domain/Services/CognitiveLoadService.cs` — add `ComputeCooldownMinutes` method

**Acceptance:**
- [ ] Signature: `int ComputeCooldownMinutes(double fatigueScoreAtEnd, int sessionDurationMinutes, int questionsAttempted)`
- [ ] Base cooldown: `fatigueScoreAtEnd * 60` minutes (high fatigue = long cooldown)
- [ ] Duration multiplier: sessions > 30 min add 50% to cooldown, > 60 min add 100%
- [ ] Intensity multiplier: > 20 questions add 25% to cooldown
- [ ] Floor: minimum 5 minutes cooldown regardless of fatigue
- [ ] Cap: maximum 120 minutes (2 hours) — longer cooldowns lose the student
- [ ] If `fatigueScoreAtEnd <= 0.2`: return 5 (minimal cooldown for low fatigue)
- [ ] Result is rounded to nearest integer

**Test:**
```csharp
[Theory]
[InlineData(0.8, 45, 25, 90)]   // High fatigue, long session, many questions
[InlineData(0.1, 15, 5, 5)]     // Low fatigue → floor of 5
[InlineData(0.5, 20, 10, 30)]   // Medium fatigue, normal session
[InlineData(1.0, 90, 40, 120)]  // Max fatigue → capped at 120
[InlineData(0.0, 10, 3, 5)]     // Zero fatigue → floor of 5
public void ComputeCooldownMinutes_CalculatesCorrectly(
    double fatigue, int duration, int questions, int expectedApprox)
{
    var svc = new CognitiveLoadService();
    var result = svc.ComputeCooldownMinutes(fatigue, duration, questions);

    Assert.InRange(result, 5, 120);
    Assert.InRange(result, expectedApprox - 15, expectedApprox + 15);
}

[Fact]
public void ComputeCooldownMinutes_NeverBelowFloor()
{
    var svc = new CognitiveLoadService();
    var result = svc.ComputeCooldownMinutes(0.01, 1, 1);
    Assert.True(result >= 5);
}

[Fact]
public void ComputeCooldownMinutes_NeverAboveCap()
{
    var svc = new CognitiveLoadService();
    var result = svc.ComputeCooldownMinutes(1.0, 120, 100);
    Assert.True(result <= 120);
}
```

---

### ACT-015.4: ICognitiveLoadService.RecommendDifficultyAdjustment
**Files:**
- `src/Cena.Domain/Services/CognitiveLoadService.cs` — add `RecommendDifficultyAdjustment` method

**Acceptance:**
- [ ] Signature: `DifficultyAdjustment RecommendDifficultyAdjustment(FatigueAssessment currentFatigue)`
- [ ] If `FatigueScore >= 0.8`: reduce by 2 levels, reason "High cognitive load — significantly reducing difficulty"
- [ ] If `FatigueScore >= 0.6`: reduce by 1 level, reason "Moderate fatigue — reducing difficulty"
- [ ] If `FatigueScore <= 0.2` AND `PrimaryDriver != "error_rate"`: increase by 1 level, reason "Student performing well — increasing challenge"
- [ ] Otherwise: maintain (delta 0), reason "Appropriate difficulty level"
- [ ] If `SuggestEndSession` is true: always reduce by 2 regardless of score (session should end, make it easy meanwhile)
- [ ] `Recommendation` field: "reduce" if `LevelDelta < 0`, "increase" if `> 0`, "maintain" if `== 0`

**Test:**
```csharp
[Fact]
public void RecommendDifficultyAdjustment_HighFatigue_Reduces()
{
    var svc = new CognitiveLoadService();
    var assessment = new FatigueAssessment(
        FatigueScore: 0.85, PrimaryDriver: "duration",
        SuggestBreak: true, SuggestEndSession: false,
        EstimatedProductiveMinutesRemaining: 5);

    var adj = svc.RecommendDifficultyAdjustment(assessment);

    Assert.Equal("reduce", adj.Recommendation);
    Assert.Equal(-2, adj.LevelDelta);
}

[Fact]
public void RecommendDifficultyAdjustment_LowFatigue_Increases()
{
    var svc = new CognitiveLoadService();
    var assessment = new FatigueAssessment(
        FatigueScore: 0.15, PrimaryDriver: "duration",
        SuggestBreak: false, SuggestEndSession: false,
        EstimatedProductiveMinutesRemaining: 30);

    var adj = svc.RecommendDifficultyAdjustment(assessment);

    Assert.Equal("increase", adj.Recommendation);
    Assert.Equal(1, adj.LevelDelta);
}

[Fact]
public void RecommendDifficultyAdjustment_LowFatigueButHighErrors_Maintains()
{
    var svc = new CognitiveLoadService();
    var assessment = new FatigueAssessment(
        FatigueScore: 0.15, PrimaryDriver: "error_rate",
        SuggestBreak: false, SuggestEndSession: false,
        EstimatedProductiveMinutesRemaining: 30);

    var adj = svc.RecommendDifficultyAdjustment(assessment);

    Assert.Equal("maintain", adj.Recommendation);
    Assert.Equal(0, adj.LevelDelta);
}

[Fact]
public void RecommendDifficultyAdjustment_SuggestEndSession_AlwaysReduces()
{
    var svc = new CognitiveLoadService();
    var assessment = new FatigueAssessment(
        FatigueScore: 0.4, PrimaryDriver: "duration",
        SuggestBreak: false, SuggestEndSession: true,
        EstimatedProductiveMinutesRemaining: 0);

    var adj = svc.RecommendDifficultyAdjustment(assessment);

    Assert.Equal("reduce", adj.Recommendation);
    Assert.Equal(-2, adj.LevelDelta);
}
```

---

### ACT-015.5: IPrerequisiteEnforcementService (Full Implementation)
**Files:**
- `src/Cena.Domain/Services/PrerequisiteEnforcementService.cs` — full implementation
- `src/Cena.Domain/Services/IPrerequisiteEnforcementService.cs` — interface (from contract)

**Acceptance:**
- [ ] Constructor injection: `ICurriculumGraphService` (provides prerequisite edges from Neo4j/in-memory graph)
- [ ] Uses dual threshold: `PrerequisiteGateThreshold = 0.95` (NOT `ProgressionThreshold = 0.85`)

**CheckPrerequisites:**
- [ ] Signature: `Task<PrerequisiteCheckResult> CheckPrerequisites(string conceptId, Dictionary<string, double> masteryMap)`
- [ ] Queries the curriculum graph for all prerequisites of `conceptId`
- [ ] For each prerequisite: check if `masteryMap[prereqId] >= 0.95`
- [ ] `IsUnlocked = true` only when ALL prerequisites meet the gate threshold
- [ ] `MissingPrerequisites`: list of prerequisite IDs below 0.95
- [ ] `PrerequisiteMasteryGaps`: for each missing prerequisite, `0.95 - currentMastery`
- [ ] `SuggestedActionHe`: Hebrew string "יש לחזור על {conceptName} כדי לפתוח נושא זה" for the prerequisite with the largest gap
- [ ] If concept has no prerequisites: `IsUnlocked = true`, empty lists

**GetBlockedConcepts:**
- [ ] Signature: `Task<List<BlockedConcept>> GetBlockedConcepts(Dictionary<string, double> masteryMap)`
- [ ] Iterates ALL concepts in the curriculum graph
- [ ] Returns concepts where at least one prerequisite is below 0.95
- [ ] `UnlockProgress`: ratio of satisfied prerequisites to total prerequisites (0.0-1.0)
- [ ] Sorted by `UnlockProgress` descending (closest to unlock first)

**GetUnlockedFrontier:**
- [ ] Signature: `Task<List<string>> GetUnlockedFrontier(Dictionary<string, double> masteryMap)`
- [ ] Returns concepts where ALL prerequisites are at >= 0.95 AND the concept itself is < 0.85 (ProgressionThreshold)
- [ ] This is the optimal next-study set
- [ ] Empty list if student has mastered everything or has no unlocked concepts

**Test:**
```csharp
[Fact]
public async Task CheckPrerequisites_AllMet_ReturnsUnlocked()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["calculus-1"] = new() { "algebra-1", "algebra-2" }
    });
    var svc = new PrerequisiteEnforcementService(graph);

    var mastery = new Dictionary<string, double>
    {
        ["algebra-1"] = 0.96,
        ["algebra-2"] = 0.95
    };

    var result = await svc.CheckPrerequisites("calculus-1", mastery);

    Assert.True(result.IsUnlocked);
    Assert.Empty(result.MissingPrerequisites);
}

[Fact]
public async Task CheckPrerequisites_OneMissing_ReturnsBlocked()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["calculus-1"] = new() { "algebra-1", "algebra-2" }
    });
    var svc = new PrerequisiteEnforcementService(graph);

    var mastery = new Dictionary<string, double>
    {
        ["algebra-1"] = 0.96,
        ["algebra-2"] = 0.80  // Below 0.95 gate!
    };

    var result = await svc.CheckPrerequisites("calculus-1", mastery);

    Assert.False(result.IsUnlocked);
    Assert.Single(result.MissingPrerequisites);
    Assert.Contains("algebra-2", result.MissingPrerequisites);
    Assert.Equal(0.15, result.PrerequisiteMasteryGaps["algebra-2"], precision: 2);
    Assert.NotNull(result.SuggestedActionHe);
}

[Fact]
public async Task CheckPrerequisites_UsesGateThreshold_Not085()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["calculus-1"] = new() { "algebra-1" }
    });
    var svc = new PrerequisiteEnforcementService(graph);

    // 0.90 is above ProgressionThreshold (0.85) but BELOW PrerequisiteGateThreshold (0.95)
    var mastery = new Dictionary<string, double> { ["algebra-1"] = 0.90 };

    var result = await svc.CheckPrerequisites("calculus-1", mastery);

    Assert.False(result.IsUnlocked); // Must be >= 0.95
}

[Fact]
public async Task GetBlockedConcepts_SortedByUnlockProgress()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["calc-1"] = new() { "alg-1", "alg-2" },  // 1/2 met = 0.5
        ["calc-2"] = new() { "alg-1" },            // 0/1 met = 0.0
        ["trig-1"] = new() { "alg-1", "geom-1" },  // 2/2 met but below threshold
    });
    var svc = new PrerequisiteEnforcementService(graph);

    var mastery = new Dictionary<string, double>
    {
        ["alg-1"] = 0.96,
        ["alg-2"] = 0.80,
        ["geom-1"] = 0.50,
    };

    var blocked = await svc.GetBlockedConcepts(mastery);

    Assert.True(blocked.Count >= 2);
    Assert.True(blocked[0].UnlockProgress >= blocked[1].UnlockProgress);
}

[Fact]
public async Task GetUnlockedFrontier_ReturnsReadyToStudyConcepts()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["calc-1"] = new() { "alg-1" },
        ["calc-2"] = new() { "alg-1", "alg-2" },
    });
    var svc = new PrerequisiteEnforcementService(graph);

    var mastery = new Dictionary<string, double>
    {
        ["alg-1"] = 0.96,   // Gate met
        ["alg-2"] = 0.80,   // Gate NOT met
        ["calc-1"] = 0.50,  // In progress (below 0.85)
    };

    var frontier = await svc.GetUnlockedFrontier(mastery);

    Assert.Contains("calc-1", frontier);       // All prereqs met, not yet mastered
    Assert.DoesNotContain("calc-2", frontier);  // alg-2 gate not met
    Assert.DoesNotContain("alg-1", frontier);   // Already mastered
}

[Fact]
public async Task CheckPrerequisites_NoPrereqs_ReturnsUnlocked()
{
    var graph = MockGraph(new Dictionary<string, List<string>>
    {
        ["basics-1"] = new() // No prerequisites
    });
    var svc = new PrerequisiteEnforcementService(graph);

    var result = await svc.CheckPrerequisites("basics-1", new Dictionary<string, double>());

    Assert.True(result.IsUnlocked);
    Assert.Empty(result.MissingPrerequisites);
}
```

**Edge cases:**
- Circular prerequisites in graph: detect and log ERROR, treat as no prerequisites
- Concept not found in graph: return `IsUnlocked = false` with error message
- `masteryMap` missing a prerequisite concept entirely: treat as mastery 0.0

---

## Integration Test (cross-service)

```csharp
[Fact]
public async Task DomainServices_WorkTogether_InAttemptFlow()
{
    // Setup all services
    var bkt = new BktService(paramStore);
    var hlr = new HalfLifeRegressionService(graphService);
    var cog = new CognitiveLoadService();
    var prereq = new PrerequisiteEnforcementService(graphService);

    // 1. Check prerequisites before attempt
    var prereqResult = await prereq.CheckPrerequisites("algebra-2", masteryMap);
    Assert.True(prereqResult.IsUnlocked);

    // 2. BKT update after correct answer
    var bktResult = bkt.Update(new BktUpdateInput("algebra-2", 0.80, true, 0, false));
    Assert.True(bktResult.PosteriorMastery > 0.80);

    // 3. Cognitive load check
    var fatigue = cog.UpdateFatigue(fatigueInput);
    var adj = cog.RecommendDifficultyAdjustment(fatigue);

    // 4. If mastered, compute initial half-life and schedule
    if (bktResult.MasteryThresholdCrossed)
    {
        var schedule = hlr.ComputeReviewSchedule(halfLifeMap, lastReviewMap);
        Assert.NotEmpty(schedule);
    }
}
```

## Rollback Criteria
- If prerequisite enforcement breaks student flow: disable gate checks (return `IsUnlocked = true` always)
- If BKT reload corrupts parameters: revert to hardcoded defaults
- If cognitive load cooldown is too aggressive: reduce multipliers by 50%

## Definition of Done
- [ ] All 5 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=DomainServices"` -- 0 failures
- [ ] All methods allocation-free on hot path (BKT update, fatigue update)
- [ ] Thread-safety verified for BKT reload under concurrent load
- [ ] PR reviewed by architect
