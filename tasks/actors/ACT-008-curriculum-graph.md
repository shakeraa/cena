# ACT-008: In-Memory Graph Actor — Microsecond Lookups, Hot-Reload

**Priority:** P1 — blocks exercise selection hot path
**Blocked by:** ACT-001 (Cluster Bootstrap), CNT-001 (Math Graph)
**Estimated effort:** 2 days
**Contract:** `contracts/backend/domain-services.cs` (IPrerequisiteEnforcementService), `contracts/data/neo4j-schema.cypher`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The curriculum graph is loaded from Neo4j into an in-memory actor for microsecond-latency prerequisite lookups and exercise selection. The actor subscribes to `cena.curriculum.events.GraphPublished` for hot-reload without disrupting active sessions.

## Subtasks

### ACT-008.1: In-Memory Graph Data Structure

**Files to create/modify:**
- `src/Cena.Actors/CurriculumGraph/CurriculumGraphActor.cs`
- `src/Cena.Actors/CurriculumGraph/InMemoryGraph.cs`

**Acceptance:**
- [ ] Graph loaded from Neo4j on actor activation
- [ ] Adjacency list representation for O(1) neighbor lookup
- [ ] Prerequisite chain query: BFS with max depth 15, < 100 microseconds
- [ ] Frontier computation (unlocked concepts for a mastery map): < 1ms for 2000 nodes
- [ ] Memory: < 50MB for 2000 concepts with metadata
- [ ] Thread-safe reads via immutable snapshot (copy-on-write for hot-reload)

**Test:**
```csharp
[Fact]
public void PrerequisiteLookup_SubMillisecond()
{
    var graph = LoadTestGraph(2000);
    var sw = Stopwatch.StartNew();
    var prereqs = graph.GetPrerequisiteChain("advanced-calculus");
    sw.Stop();
    Assert.True(sw.Elapsed.TotalMilliseconds < 1.0);
    Assert.NotEmpty(prereqs);
}
```

---

### ACT-008.2: Hot-Reload via NATS Event

**Files to create/modify:**
- `src/Cena.Actors/CurriculumGraph/CurriculumGraphActor.cs` (HandleGraphPublished)

**Acceptance:**
- [ ] Subscribes to `cena.curriculum.events.GraphPublished`
- [ ] On event: reload graph from Neo4j, swap immutable snapshot
- [ ] Active queries against old snapshot complete before swap
- [ ] Reload completes within 30 seconds for 2000 concepts
- [ ] Metrics: `cena.graph.reload_duration_ms`, `cena.graph.concept_count`

**Test:**
```csharp
[Fact]
public async Task HotReload_SwapsGraphWithoutDisruption()
{
    var actor = await ActivateGraphActor();
    var oldCount = await actor.Ask<int>(new GetConceptCount());

    await PublishGraphUpdate(addedConcepts: 10);
    await Task.Delay(TimeSpan.FromSeconds(5));

    var newCount = await actor.Ask<int>(new GetConceptCount());
    Assert.Equal(oldCount + 10, newCount);
}
```

---

### ACT-008.3: Prerequisite Enforcement Service

**Files to create/modify:**
- `src/Cena.Domain/Services/PrerequisiteEnforcementService.cs`

**Acceptance:**
- [ ] `CheckPrerequisites(conceptId, masteryMap)`: returns unlocked/blocked status with gap details
- [ ] Uses dual threshold: progression at 0.85, prerequisite gate at 0.95
- [ ] `GetUnlockedFrontier(masteryMap)`: returns concepts ready to learn
- [ ] `GetBlockedConcepts(masteryMap)`: returns concepts with unmet prerequisites

**Test:**
```csharp
[Fact]
public async Task CheckPrerequisites_BlocksWhenBelowGateThreshold()
{
    var masteryMap = new Dictionary<string, double> { ["math-addition"] = 0.90 }; // Below 0.95 gate
    var result = await _service.CheckPrerequisites("math-multiplication", masteryMap);
    Assert.False(result.IsUnlocked);
    Assert.Contains("math-addition", result.MissingPrerequisites);
}
```

---

## Rollback Criteria
- Fall back to direct Neo4j queries (higher latency but correct)

## Definition of Done
- [ ] Graph loaded, prerequisite lookups < 1ms
- [ ] Hot-reload verified
- [ ] Dual threshold enforcement tested
- [ ] PR reviewed by architect
