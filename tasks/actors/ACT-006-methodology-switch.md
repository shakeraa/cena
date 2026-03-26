# ACT-006: MethodologySwitchService (Domain Service, DI-Injected)

**Priority:** P1 — completes the adaptive learning loop
**Blocked by:** ACT-004 (StagnationDetector triggers switch), DATA-005 (Neo4j MCM graph), DATA-004 (Redis cache)
**Estimated effort:** 3 days
**Contract:** `contracts/actors/methodology_switch_service.cs`, `contracts/backend/actor-contracts.cs` (lines 647-713), `contracts/backend/domain-services.cs` (lines 281-381)

---

## Context
The MethodologySwitchService is a stateless domain service (NOT an actor) called by the StudentActor when stagnation is detected or a student manually requests a methodology switch. It implements the 5-step MCM (Methodology-Concept-Mapping) algorithm: classify the dominant error type, query the Neo4j MCM graph (via Redis cache), filter out previously attempted methods, select the best candidate, and fall back to error-type defaults if no MCM data exists. It also enforces cycling prevention (tracking last 3 stagnation cycles per concept) and escalation when all 8 methodologies are exhausted for a concept cluster (flagging as "mentor-resistant").

## Subtasks

### ACT-006.1: Service Scaffold + DI Registration + Types
**Files to create/modify:**
- `src/Cena.Actors/Services/MethodologySwitchService.cs` -- main service
- `src/Cena.Actors/Services/McmCandidate.cs` -- MCM graph candidate record
- `src/Cena.Actors/Services/StagnationCycleRecord.cs` -- cycling prevention record
- `src/Cena.Actors/DependencyInjection/ServiceRegistration.cs` -- DI registration

**Acceptance:**
- [ ] `MethodologySwitchService : IMethodologySwitchService` with `IDriver` (Neo4j), `IDistributedCache` (Redis), `ILogger<MethodologySwitchService>` constructor (contract lines 110-197)
- [ ] Registered as scoped/transient in DI (stateless, no shared mutable state)
- [ ] `McmCandidate` record: `Methodology`, `Confidence`, `EvidenceCount`, `AvgMasteryImprovement` (contract lines 38-49)
- [ ] `StagnationCycleRecord` record: `FromMethodology`, `ToMethodology`, `StagnationScore`, `SwitchedAt` (contract lines 55-59)
- [ ] Constants: `CacheTtl = 1 hour`, `MinConfidenceThreshold = 0.5`, `MaxStagnationCycles = 3`, `CooldownSessions = 3` (contract lines 118-122)
- [ ] `AllMethodologies` array: all 8 values from `Methodology` enum (contract lines 141-151) -- must include `DrillAndPractice` (9th entry, used in `ErrorTypeDefaults` for Procedural at contract line 170)
- [ ] `ErrorTypeDefaults` dictionary mapping each `ErrorType` to prioritized methodology list (contract lines 158-187):
  - `Conceptual` -> Feynman, Analogy, Socratic, BloomsProgression
  - `Procedural` -> WorkedExample, DrillAndPractice, BloomsProgression, RetrievalPractice
  - `Motivational` -> ProjectBased, Socratic, Analogy, RetrievalPractice
  - `None` -> SpacedRepetition, RetrievalPractice, Feynman
- [ ] Telemetry: `SwitchDecisionCounter`, `EscalationCounter`, `FallbackCounter`, `DecisionLatency` histogram (contract lines 124-135)

**Test:**
```csharp
[Fact]
public void AllMethodologies_Contains8Entries()
{
    Assert.Equal(8, MethodologySwitchService.AllMethodologies.Length);
    Assert.Contains(Methodology.Socratic, MethodologySwitchService.AllMethodologies);
    Assert.Contains(Methodology.RetrievalPractice, MethodologySwitchService.AllMethodologies);
}

[Fact]
public void ErrorTypeDefaults_AllErrorTypesHaveFallbacks()
{
    Assert.True(MethodologySwitchService.ErrorTypeDefaults.ContainsKey(ErrorType.Conceptual));
    Assert.True(MethodologySwitchService.ErrorTypeDefaults.ContainsKey(ErrorType.Procedural));
    Assert.True(MethodologySwitchService.ErrorTypeDefaults.ContainsKey(ErrorType.Motivational));
    Assert.True(MethodologySwitchService.ErrorTypeDefaults.ContainsKey(ErrorType.None));
}

[Fact]
public void ErrorTypeDefaults_ConceptualStartsWithFeynman()
{
    var defaults = MethodologySwitchService.ErrorTypeDefaults[ErrorType.Conceptual];
    Assert.Equal(Methodology.Feynman, defaults[0]);
}

[Fact]
public void ErrorTypeDefaults_ProceduralStartsWithWorkedExample()
{
    var defaults = MethodologySwitchService.ErrorTypeDefaults[ErrorType.Procedural];
    Assert.Equal(Methodology.WorkedExample, defaults[0]);
}

[Fact]
public void ErrorTypeDefaults_MotivationalStartsWithProjectBased()
{
    var defaults = MethodologySwitchService.ErrorTypeDefaults[ErrorType.Motivational];
    Assert.Equal(Methodology.ProjectBased, defaults[0]);
}
```

---

### ACT-006.2: 5-Step Decision Algorithm (Main Path)
**Files to create/modify:**
- `src/Cena.Actors/Services/MethodologySwitchService.cs` -- `DecideSwitch()` method

**Acceptance:**
- [ ] **Step 1 -- Classify**: uses `request.DominantErrorType` directly (classification happens upstream) (contract lines 220-226)
- [ ] **Step 2 -- MCM lookup**: calls `QueryMcmGraph(errorType, conceptCategory)` (contract lines 232-237)
- [ ] **Step 3 -- Filter**: excludes all methods in `request.MethodAttemptHistory` AND `request.CurrentMethodology`, ordered by confidence desc (contract lines 243-255)
- [ ] **Step 4 -- Select**: first candidate with `Confidence >= 0.5`; if none, best available regardless of confidence (contract lines 264-280)
- [ ] **Step 5 -- Fallback**: if no MCM candidates remain, applies `ApplyFallback(errorType, attemptedMethods)` (contract lines 286-295)
- [ ] Returns `DecideSwitchResponse` with `ShouldSwitch=true`, `RecommendedMethodology`, `Confidence`, `DecisionTrace` (contract lines 346-352)
- [ ] `DecisionTrace`: concatenated step-by-step reasoning log for observability (contract lines 216, 226, 237, etc.)
- [ ] `DecisionLatency` histogram recorded in milliseconds (contract lines 337, 316)

**Test:**
```csharp
[Fact]
public async Task DecideSwitch_SelectsHighConfidenceMcmCandidate()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Feynman, 0.85, 120, 0.15),
        new McmCandidate(Methodology.Analogy, 0.60, 80, 0.10),
    });

    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string>(), // No prior attempts
        StagnationScore: 0.75, ConsecutiveStagnantSessions: 3));

    Assert.True(result.ShouldSwitch);
    Assert.Equal(Methodology.Feynman, result.RecommendedMethodology);
    Assert.Equal(0.85, result.Confidence);
    Assert.False(result.AllMethodologiesExhausted);
    Assert.Contains("Step1", result.DecisionTrace);
    Assert.Contains("Step4", result.DecisionTrace);
}

[Fact]
public async Task DecideSwitch_FiltersOutAttemptedMethods()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Feynman, 0.85, 120, 0.15),
        new McmCandidate(Methodology.Analogy, 0.70, 80, 0.10),
        new McmCandidate(Methodology.BloomsProgression, 0.55, 50, 0.08),
    });

    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string> { "Feynman", "Analogy" }, // Already tried
        0.75, 3));

    Assert.True(result.ShouldSwitch);
    Assert.Equal(Methodology.BloomsProgression, result.RecommendedMethodology);
}

[Fact]
public async Task DecideSwitch_ExcludesCurrentMethodology()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Socratic, 0.90, 200, 0.20), // Current!
        new McmCandidate(Methodology.Feynman, 0.70, 100, 0.12),
    });

    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic, // Current
        new List<string>(), 0.75, 3));

    // Should NOT select Socratic even though it's highest confidence
    Assert.Equal(Methodology.Feynman, result.RecommendedMethodology);
}

[Fact]
public async Task DecideSwitch_SelectsBestAvailable_WhenNoneAboveThreshold()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Procedural", "calculus", new[]
    {
        new McmCandidate(Methodology.WorkedExample, 0.35, 20, 0.05),
        new McmCandidate(Methodology.BloomsProgression, 0.30, 15, 0.04),
    });

    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "calc-1", "calculus",
        ErrorType.Procedural, Methodology.Socratic,
        new List<string>(), 0.75, 3));

    // Both below 0.5 threshold, but best available selected
    Assert.True(result.ShouldSwitch);
    Assert.Equal(Methodology.WorkedExample, result.RecommendedMethodology);
    Assert.Equal(0.35, result.Confidence);
    Assert.Contains("best available", result.DecisionTrace);
}
```

---

### ACT-006.3: Fallback Logic + Escalation (All Methods Exhausted)
**Files to create/modify:**
- `src/Cena.Actors/Services/MethodologySwitchService.cs` -- `ApplyFallback()`, `DetermineEscalationAction()`

**Acceptance:**
- [ ] `ApplyFallback`: iterates `ErrorTypeDefaults[errorType]` first, then `AllMethodologies`, skipping already-attempted (contract lines 472-496)
- [ ] Fallback confidence: 0.4 for error-type defaults, 0.2 for any-methodology fallback (contract lines 482, 492)
- [ ] Returns `null` when ALL methodologies exhausted (contract line 495)
- [ ] Escalation: when `ApplyFallback` returns null, returns `DecideSwitchResponse` with `ShouldSwitch=false`, `AllMethodologiesExhausted=true`, `EscalationAction` (contract lines 302-325)
- [ ] `DetermineEscalationAction` logic (contract lines 511-523):
  - `StagnationScore > 0.9` -> `"connect_tutor"` (human intervention)
  - `ConsecutiveStagnantSessions >= 5` -> `"suggest_skip"`
  - Default -> `"try_adjacent"`
- [ ] Error path: if any exception in `DecideSwitch`, catches, logs ERROR, applies emergency fallback with empty attempted set (contract lines 354-374)
- [ ] `EscalationCounter` incremented when all methods exhausted (contract lines 304-305)
- [ ] `FallbackCounter` incremented when error-type defaults used (contract lines 291-292)

**Test:**
```csharp
[Fact]
public async Task Fallback_UsesErrorTypeDefaults_WhenNoMcmData()
{
    var neo4j = new MockNeo4jDriver(); // Empty MCM graph
    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string>(), 0.75, 3));

    Assert.True(result.ShouldSwitch);
    Assert.Equal(Methodology.Feynman, result.RecommendedMethodology);
    Assert.Equal(0.4, result.Confidence); // Fallback confidence
    Assert.Contains("Fallback", result.DecisionTrace);
}

[Fact]
public async Task Fallback_SkipsAlreadyAttempted()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string> { "Feynman", "Analogy" },
        0.75, 3));

    // Feynman and Analogy already tried -> BloomsProgression is next for Conceptual
    Assert.Equal(Methodology.BloomsProgression, result.RecommendedMethodology);
}

[Fact]
public async Task Fallback_TriesAnyMethodology_AfterErrorDefaults()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    // All Conceptual defaults tried
    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string> { "Feynman", "Analogy", "Socratic", "BloomsProgression" },
        0.75, 3));

    // Falls through to any remaining methodology
    Assert.True(result.ShouldSwitch);
    Assert.Equal(0.2, result.Confidence); // Any-methodology confidence
    Assert.NotNull(result.RecommendedMethodology);
}

[Fact]
public async Task AllExhausted_ReturnsEscalation()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    // All 8 methodologies already attempted + current
    var allAttempted = new List<string>
    {
        "Socratic", "SpacedRepetition", "Feynman", "ProjectBased",
        "BloomsProgression", "WorkedExample", "Analogy", "RetrievalPractice",
        "DrillAndPractice"
    };

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        allAttempted, 0.75, 3));

    Assert.False(result.ShouldSwitch);
    Assert.True(result.AllMethodologiesExhausted);
    Assert.Null(result.RecommendedMethodology);
    Assert.NotNull(result.EscalationAction);
    Assert.Contains("ESCALATION", result.DecisionTrace);
}

[Fact]
public async Task Escalation_ConnectTutor_WhenScoreVeryHigh()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    var allAttempted = Enum.GetNames<Methodology>().ToList();

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        allAttempted,
        StagnationScore: 0.95, // Very high
        ConsecutiveStagnantSessions: 3));

    Assert.Equal("connect_tutor", result.EscalationAction);
}

[Fact]
public async Task Escalation_SuggestSkip_WhenManyConsecutiveSessions()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    var allAttempted = Enum.GetNames<Methodology>().ToList();

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        allAttempted,
        StagnationScore: 0.75,
        ConsecutiveStagnantSessions: 5)); // Many sessions

    Assert.Equal("suggest_skip", result.EscalationAction);
}

[Fact]
public async Task Escalation_TryAdjacent_Default()
{
    var neo4j = new MockNeo4jDriver();
    var service = CreateService(neo4j: neo4j);

    var allAttempted = Enum.GetNames<Methodology>().ToList();

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        allAttempted,
        StagnationScore: 0.72,
        ConsecutiveStagnantSessions: 3));

    Assert.Equal("try_adjacent", result.EscalationAction);
}

[Fact]
public async Task ErrorInDecideSwitch_AppliesSafeFallback()
{
    var neo4j = new MockNeo4jDriver { ShouldThrow = true }; // Neo4j failure
    var service = CreateService(neo4j: neo4j);

    var result = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string>(), 0.75, 3));

    // Should return a fallback rather than throwing
    Assert.True(result.ShouldSwitch);
    Assert.NotNull(result.RecommendedMethodology);
    Assert.Contains("ERROR", result.DecisionTrace);
}
```

**Edge cases:**
- `MethodAttemptHistory` contains unrecognized methodology names -> `Enum.TryParse` returns null, excluded from filter (contract lines 244-247)
- `ConceptCategory` not in MCM graph -> `QueryMcmGraph` returns empty list -> falls through to fallback
- All `ErrorTypeDefaults` tried but `AllMethodologies` has extras -> catches `DrillAndPractice` as last resort

---

### ACT-006.4: MCM Graph Query (Neo4j via Redis Cache)
**Files to create/modify:**
- `src/Cena.Actors/Services/MethodologySwitchService.cs` -- `QueryMcmGraph()`, `TryGetFromCache()`, `SetCache()`

**Acceptance:**
- [ ] Cache key format: `mcm:{errorType}:{conceptCategory}` (contract line 401)
- [ ] Cache hit: returns cached `List<McmCandidate>` immediately, sets activity tag `cache.hit=true` (contract lines 402-407)
- [ ] Cache miss: executes Cypher query against Neo4j (contract lines 416-434):
  ```cypher
  MATCH (e:ErrorType {name: $errorType})-[r:REMEDIATED_BY]->(m:Methodology)
  WHERE r.conceptCategory = $category
  RETURN m.name AS methodology, r.confidence AS confidence,
         r.evidenceCount AS evidence, r.avgImprovement AS improvement
  ORDER BY r.confidence DESC
  ```
- [ ] Results deserialized into `McmCandidate` list with `Enum.TryParse` for methodology name (contract lines 436-447)
- [ ] Results cached with 1-hour TTL (contract lines 449, 118)
- [ ] Neo4j failure: caught, logged as WARNING, returns empty list (fallback handles it) (contract lines 452-459)
- [ ] Cache read failure: caught, logged as DEBUG, treated as cache miss (contract lines 529-542)
- [ ] Cache write failure: caught, logged as DEBUG, non-fatal (contract lines 545-558)

**Test:**
```csharp
[Fact]
public async Task QueryMcm_CacheHit_ReturnsWithoutNeo4j()
{
    var neo4j = new MockNeo4jDriver();
    var cache = new MockDistributedCache();
    var service = CreateService(neo4j: neo4j, cache: cache);

    var cached = new List<McmCandidate>
    {
        new(Methodology.Feynman, 0.80, 100, 0.12)
    };
    cache.Set("mcm:Conceptual:algebra", cached);

    var result = await service.QueryMcmGraph(ErrorType.Conceptual, "algebra");

    Assert.Single(result);
    Assert.Equal(Methodology.Feynman, result[0].Methodology);
    Assert.Equal(0, neo4j.QueryCount); // Neo4j NOT called
}

[Fact]
public async Task QueryMcm_CacheMiss_QueriesNeo4jAndCaches()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Feynman, 0.80, 100, 0.12),
        new McmCandidate(Methodology.Analogy, 0.65, 50, 0.08),
    });
    var cache = new MockDistributedCache();
    var service = CreateService(neo4j: neo4j, cache: cache);

    var result = await service.QueryMcmGraph(ErrorType.Conceptual, "algebra");

    Assert.Equal(2, result.Count);
    Assert.Equal(1, neo4j.QueryCount); // Neo4j called

    // Verify cached
    Assert.True(cache.Contains("mcm:Conceptual:algebra"));
}

[Fact]
public async Task QueryMcm_Neo4jFailure_ReturnsEmptyList()
{
    var neo4j = new MockNeo4jDriver { ShouldThrow = true };
    var cache = new MockDistributedCache();
    var service = CreateService(neo4j: neo4j, cache: cache);

    var result = await service.QueryMcmGraph(ErrorType.Conceptual, "algebra");

    Assert.Empty(result);
    // Verify NOT cached (don't cache empty on error)
    Assert.False(cache.Contains("mcm:Conceptual:algebra"));
}

[Fact]
public async Task QueryMcm_UnrecognizedMethodology_SkippedSilently()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResultsRaw("Conceptual", "algebra", new[]
    {
        ("Feynman", 0.80, 100, 0.12),
        ("UnknownMethod", 0.90, 200, 0.20), // Should be skipped
    });
    var cache = new MockDistributedCache();
    var service = CreateService(neo4j: neo4j, cache: cache);

    var result = await service.QueryMcmGraph(ErrorType.Conceptual, "algebra");

    Assert.Single(result); // Only Feynman, UnknownMethod skipped
}

[Fact]
public async Task QueryMcm_CacheReadFailure_FallsThroughToNeo4j()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Feynman, 0.80, 100, 0.12)
    });
    var cache = new MockDistributedCache { ShouldThrowOnRead = true };
    var service = CreateService(neo4j: neo4j, cache: cache);

    var result = await service.QueryMcmGraph(ErrorType.Conceptual, "algebra");

    Assert.Single(result); // Still works via Neo4j
    Assert.Equal(1, neo4j.QueryCount);
}
```

**Edge cases:**
- Empty `conceptCategory` string -> query returns no results, falls through to fallback
- Redis connection dead -> all cache operations silently fail, every call hits Neo4j
- Neo4j returns 0 results for valid query -> empty list, fallback handles it
- Neo4j returns duplicate methodology entries -> duplicates preserved (filtered later by confidence)

---

## Integration Test (full methodology switch lifecycle)

```csharp
[Fact]
public async Task MethodologySwitch_FullLifecycle()
{
    var neo4j = new MockNeo4jDriver();
    neo4j.SetMcmResults("Conceptual", "algebra", new[]
    {
        new McmCandidate(Methodology.Feynman, 0.85, 120, 0.15),
        new McmCandidate(Methodology.Analogy, 0.70, 80, 0.10),
        new McmCandidate(Methodology.BloomsProgression, 0.55, 50, 0.08),
    });
    var cache = new MockDistributedCache();
    var service = CreateService(neo4j: neo4j, cache: cache);

    // Phase 1: First stagnation -> Feynman (highest confidence, not attempted)
    var result1 = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Socratic,
        new List<string>(), 0.75, 3));

    Assert.True(result1.ShouldSwitch);
    Assert.Equal(Methodology.Feynman, result1.RecommendedMethodology);

    // Phase 2: Second stagnation -> Analogy (Feynman now attempted)
    var result2 = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Feynman,
        new List<string> { "Socratic", "Feynman" },
        0.78, 3));

    Assert.True(result2.ShouldSwitch);
    Assert.Equal(Methodology.Analogy, result2.RecommendedMethodology);

    // Phase 3: Cache hit on second query (same errorType + category)
    Assert.Equal(1, neo4j.QueryCount); // Only 1 Neo4j call, rest from cache

    // Phase 4: Third stagnation -> BloomsProgression
    var result3 = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.Analogy,
        new List<string> { "Socratic", "Feynman", "Analogy" },
        0.80, 3));

    Assert.Equal(Methodology.BloomsProgression, result3.RecommendedMethodology);

    // Phase 5: MCM candidates exhausted -> falls back to error-type defaults
    var result4 = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.BloomsProgression,
        new List<string> { "Socratic", "Feynman", "Analogy", "BloomsProgression" },
        0.82, 3));

    Assert.True(result4.ShouldSwitch);
    // Should pick from AllMethodologies not yet tried

    // Phase 6: Eventually exhaust all -> escalation
    var allAttempted = Enum.GetNames<Methodology>().ToList();
    var result5 = await service.DecideSwitch(new DecideSwitchRequest(
        "stu-1", "algebra-1", "algebra",
        ErrorType.Conceptual, Methodology.RetrievalPractice,
        allAttempted, 0.92, 6));

    Assert.False(result5.ShouldSwitch);
    Assert.True(result5.AllMethodologiesExhausted);
    Assert.Equal("connect_tutor", result5.EscalationAction); // Score > 0.9
}
```

## Performance Benchmarks
- `DecideSwitch` with cache hit: < 5ms (no Neo4j)
- `DecideSwitch` with cache miss: < 50ms (Neo4j round-trip)
- `ApplyFallback`: < 10 microseconds (array iteration)
- `DetermineEscalationAction`: < 1 microsecond (conditionals)
- Redis cache read: < 1ms
- Neo4j Cypher query: < 20ms (indexed)

## Rollback Criteria
- If MCM graph returns nonsensical recommendations: disable graph lookup, use `ErrorTypeDefaults` only
- If Redis cache causes stale data issues: reduce TTL from 1 hour to 5 minutes
- If escalation triggers too often: increase `AllMethodologies` count by adding `DrillAndPractice` as a real option
- If decision latency exceeds 100ms: pre-warm cache on actor activation, increase cache TTL

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=MethodologySwitch"` -> 0 failures
- [ ] 5-step algorithm verified end-to-end for 3 error types (Conceptual, Procedural, Motivational)
- [ ] Escalation path verified: all 3 actions (connect_tutor, suggest_skip, try_adjacent)
- [ ] Cache hit/miss verified: Neo4j only called once per unique (errorType, category)
- [ ] Error resilience: Neo4j down -> fallback works; Redis down -> fallback works
- [ ] PR reviewed by architect (you)
