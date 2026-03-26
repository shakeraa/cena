# ACT-006: 5-Step MCM Algorithm, Cycling Prevention, Escalation

**Priority:** P1 — blocks adaptive methodology
**Blocked by:** ACT-001 (Cluster Bootstrap), DATA-003 (Neo4j)
**Estimated effort:** 2 days
**Contract:** `contracts/actors/methodology_switch_service.cs`, `contracts/backend/domain-services.cs` (IMethodologySwitchService)

---

## Context

When stagnation is detected, the MethodologySwitchService executes a 5-step algorithm: classify error type, MCM graph lookup, filter exhausted methods, select best candidate, fallback. Cycling prevention tracks last 3 stagnation cycles. Escalation when all 8 methodologies exhausted flags "mentor-resistant."

## Subtasks

### ACT-006.1: MCM Graph Query (Neo4j + Redis Cache)

**Files to create/modify:**
- `src/Cena.Domain/Services/MethodologySwitchService.cs`

**Acceptance:**
- [ ] Cypher query: `MATCH (et:ErrorType {name: $errorType})-[r:RECOMMENDS]->(m:Methodology) WHERE r.for_category = $category`
- [ ] Redis cache with 1-hour TTL, key: `mcm:{errorType}:{conceptCategory}`
- [ ] Cache miss -> Neo4j query -> cache result
- [ ] Neo4j failure -> empty candidates, fallback logic handles

**Test:**
```csharp
[Fact]
public async Task McmLookup_ReturnsCandidates()
{
    var candidates = await _service.QueryMcmGraph(ErrorType.Procedural, "cat-arithmetic");
    Assert.NotEmpty(candidates);
    Assert.True(candidates[0].Confidence > 0);
}
```

---

### ACT-006.2: 5-Step Decision Algorithm

**Files to create/modify:**
- `src/Cena.Domain/Services/MethodologySwitchService.cs` (DecideSwitch)

**Acceptance:**
- [ ] Step 1: Error type classification (precedence: conceptual > procedural > motivational)
- [ ] Step 2: MCM lookup returns candidates sorted by confidence
- [ ] Step 3: Filter out methods in `MethodAttemptHistory`
- [ ] Step 4: Select first with confidence > 0.5; else best available
- [ ] Step 5: Fallback defaults: Conceptual->Feynman, Procedural->WorkedExample, Motivational->ProjectBased
- [ ] Full decision trace logged for observability

**Test:**
```csharp
[Fact]
public async Task DecideSwitch_FiltersExhaustedMethods()
{
    var request = new DecideSwitchRequest(
        StudentId: "stu-1", ConceptId: "math-1", ConceptCategory: "cat-arithmetic",
        DominantErrorType: ErrorType.Procedural, CurrentMethodology: Methodology.Socratic,
        MethodAttemptHistory: new[] { "Socratic", "WorkedExample" },
        StagnationScore: 0.8, ConsecutiveStagnantSessions: 3);

    var decision = await _service.DecideSwitch(request);
    Assert.True(decision.ShouldSwitch);
    Assert.NotEqual(Methodology.Socratic, decision.RecommendedMethodology);
    Assert.NotEqual(Methodology.WorkedExample, decision.RecommendedMethodology);
}
```

---

### ACT-006.3: Cycling Prevention + Escalation

**Files to create/modify:**
- `src/Cena.Domain/Services/MethodologySwitchService.cs` (DetermineEscalationAction)

**Acceptance:**
- [ ] Track last 3 stagnation cycles per concept: `{ from, to, stagnationScore, switchedAt }`
- [ ] Same methodology pair appearing in recent cycles -> excluded from candidates
- [ ] All 8 methodologies exhausted -> `AllMethodologiesExhausted = true`
- [ ] Escalation actions: `connect_tutor` (score > 0.9), `suggest_skip` (>= 5 stagnant sessions), `try_adjacent` (default)
- [ ] 3-session cooldown after each switch enforced

**Test:**
```csharp
[Fact]
public async Task Escalation_WhenAllMethodsExhausted()
{
    var request = new DecideSwitchRequest(
        MethodAttemptHistory: AllMethodologies.Select(m => m.ToString()).ToList(),
        StagnationScore: 0.95, ConsecutiveStagnantSessions: 6,
        /* ... other fields ... */);

    var decision = await _service.DecideSwitch(request);
    Assert.False(decision.ShouldSwitch);
    Assert.True(decision.AllMethodologiesExhausted);
    Assert.Equal("connect_tutor", decision.EscalationAction);
}
```

---

### ACT-006.4: Integration with StudentActor

**Files to create/modify:**
- `src/Cena.Actors/StudentActor.cs` (HandleStagnationDetected)

**Acceptance:**
- [ ] StudentActor calls `IMethodologySwitchService.DecideSwitch()` when stagnation detected
- [ ] Switch decision applied: update `MethodologyMap`, record in `MethodAttemptHistory`
- [ ] `MethodologySwitched_V1` event persisted to Marten and published to NATS
- [ ] Actor state updated atomically

**Test:**
```csharp
[Fact]
public async Task StudentActor_SwitchesMethodologyOnStagnation()
{
    var actor = await ActivateStudentActor("stu-1");
    await actor.Tell(new StagnationDetected("stu-1", "math-1", stagnationScore: 0.8));

    var state = await actor.GetState();
    Assert.NotEqual(Methodology.Socratic, state.MethodologyMap["math-1"]);
}
```

---

## Rollback Criteria
- Disable methodology switching; students stay on default Socratic methodology

## Definition of Done
- [ ] All 4 subtasks pass their tests
- [ ] 5-step algorithm produces correct decisions for all error type combinations
- [ ] Cycling prevention tested with multi-cycle scenarios
- [ ] Escalation triggers at correct thresholds
- [ ] PR reviewed by architect
