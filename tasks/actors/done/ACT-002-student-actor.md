# ACT-002: StudentActor (Virtual, Event-Sourced)

**Priority:** P0 — the core domain actor
**Blocked by:** ACT-001 (cluster), DATA-001 (PostgreSQL), DATA-002 (event types), DATA-003 (snapshot)
**Estimated effort:** 5 days (largest single task in the system)
**Contract:** `contracts/actors/student_actor.cs`, `contracts/data/marten-event-store.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The StudentActor is the event-sourced aggregate root. Every student gets one. It holds mastery state, spawns child actors (session, stagnation, outreach), and persists all domain events to Marten. This is the most critical code path in the system — it must be correct, fast, and resilient.

## Subtasks

### ACT-002.1: Virtual Grain Registration + Activation
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — main actor
- `src/Cena.Actors/Students/IStudentGrain.cs` — grain interface
- `src/Cena.Actors/Students/StudentState.cs` — aggregate state

**Acceptance:**
- [ ] `IStudentGrain` interface with `AttemptConcept`, `StartSession`, `EndSession`, `GetProfile`
- [ ] Virtual actor: `Cluster.GetGrain<IStudentGrain>(studentId)` activates automatically
- [ ] Grain kind registered: `ClusterConfig.WithClusterKind("StudentGrain", props)`
- [ ] Activation logged: "StudentActor {studentId} activated"
- [ ] Passivation after 30 minutes idle via `context.SetReceiveTimeout(TimeSpan.FromMinutes(30))`
- [ ] Passivation logged: "StudentActor {studentId} passivating after {idleMinutes}min idle"

**Test:**
```csharp
[Fact]
public async Task StudentActor_ActivatesOnFirstMessage()
{
    var grain = _cluster.GetGrain<IStudentGrain>("student-1");
    var profile = await grain.GetProfile();
    Assert.NotNull(profile);
    Assert.Equal("student-1", profile.StudentId);
}

[Fact]
public async Task StudentActor_PassivatesAfterIdle()
{
    var grain = _cluster.GetGrain<IStudentGrain>("student-2");
    await grain.GetProfile(); // Activate

    // Wait for passivation (use shorter timeout in test)
    await Task.Delay(TimeSpan.FromSeconds(5)); // Test uses 3s timeout

    // Verify actor was passivated (activation counter incremented on next call)
    var profile = await grain.GetProfile(); // Re-activate
    Assert.Equal(2, _activationCounter.GetCount("student-2"));
}
```

---

### ACT-002.2: State Restoration from Marten (Snapshot + Replay)
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — `RestoreState()` method
- `src/Cena.Actors/Students/StudentState.cs` — all Apply methods

**Acceptance:**
- [ ] On activation: load latest snapshot from Marten
- [ ] Replay events since snapshot (max 100 events if snapshot is current)
- [ ] If no snapshot: replay entire stream (first activation)
- [ ] `_state.EventVersion` set to stream version after replay
- [ ] All Apply methods use `e.Timestamp` (NOT `DateTimeOffset.UtcNow`)
- [ ] `RecalculateBaselines()` called ONCE after replay completes (not per event during replay)
- [ ] Activation latency logged as metric: `cena.actor.activation_ms`

**Test:**
```csharp
[Fact]
public async Task StudentActor_RestoresFromSnapshot()
{
    // Setup: persist 150 events (snapshot at 100)
    await PersistTestEvents("student-3", 150);

    // Activate actor
    var grain = _cluster.GetGrain<IStudentGrain>("student-3");
    var profile = await grain.GetProfile();

    // State reflects all 150 events
    Assert.Equal(150, profile.EventVersion);
    // Verify mastery from events
    Assert.True(profile.ConceptMastery.ContainsKey("concept-1"));
}

[Fact]
public async Task StudentActor_RestoresFromEmptyStream()
{
    var grain = _cluster.GetGrain<IStudentGrain>("new-student");
    var profile = await grain.GetProfile();

    Assert.Equal(0, profile.EventVersion);
    Assert.Empty(profile.ConceptMastery);
}

[Fact]
public async Task StudentActor_ReplayIsDeterministic()
{
    await PersistTestEvents("student-4", 50);

    // Activate twice (passivate between)
    var p1 = await GetProfileViaGrain("student-4");
    await PassivateAndReactivate("student-4");
    var p2 = await GetProfileViaGrain("student-4");

    // State is identical
    Assert.Equal(
        JsonSerializer.Serialize(p1.ConceptMastery),
        JsonSerializer.Serialize(p2.ConceptMastery)
    );
}
```

**Edge cases:**
- Snapshot is corrupt → fall back to full replay, log ERROR
- Stream has 10,000+ events, no snapshot → replay succeeds but slow, log WARNING + trigger snapshot
- PostgreSQL connection fails during restore → actor activation fails, Proto.Actor retries via supervision

---

### ACT-002.3: Atomic Event Persistence (StageEvent + FlushEvents)
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — persistence methods

**Acceptance:**
- [ ] `StageEvent(event)` collects events into `_pendingEvents` list
- [ ] `FlushEvents()` appends ALL staged events in ONE Marten session with expected version
- [ ] Expected version: `_state.EventVersion` passed to `session.Events.Append()`
- [ ] Concurrent writes throw `EventStreamUnexpectedMaxEventIdException`
- [ ] On success: `_state.EventVersion += _pendingEvents.Count`
- [ ] On failure: `_pendingEvents` NOT cleared (retry possible)
- [ ] NATS publish happens AFTER Marten commit (outbox pattern)
- [ ] Snapshot check runs after flush (every 100 events)

**Test:**
```csharp
[Fact]
public async Task FlushEvents_PersistsAtomically()
{
    var actor = CreateTestStudentActor("student-5");

    // Stage 3 events from one command
    actor.StageEvent(new ConceptAttempted_V1(...));
    actor.StageEvent(new XpAwarded_V1(...));
    actor.StageEvent(new ConceptMastered_V1(...));

    await actor.FlushEvents();

    // All 3 in Marten as atomic batch
    var events = await _store.LightweightSession().Events.FetchStreamAsync("student-5");
    Assert.Equal(3, events.Count);
}

[Fact]
public async Task FlushEvents_RejectsOnVersionConflict()
{
    var actor = CreateTestStudentActor("student-6");

    // Simulate concurrent write: advance version in DB
    await AppendExternalEvent("student-6");

    actor.StageEvent(new ConceptAttempted_V1(...));

    // FlushEvents should fail with optimistic concurrency exception
    await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(
        () => actor.FlushEvents());

    // Events NOT cleared — can retry after state refresh
    Assert.Single(actor.PendingEvents);
}

[Fact]
public async Task FlushEvents_PublishesToNatsAfterCommit()
{
    var natsMessages = new List<string>();
    _nats.OnPublish += (subject, _) => natsMessages.Add(subject);

    var actor = CreateTestStudentActor("student-7");
    actor.StageEvent(new ConceptAttempted_V1(...));
    await actor.FlushEvents();

    // NATS received the event
    Assert.Contains(natsMessages, s => s.Contains("conceptattempted"));
}
```

---

### ACT-002.4: AttemptConcept Command Handler
**Files:**
- `src/Cena.Actors/Students/Handlers/AttemptConceptHandler.cs`

**Acceptance:**
- [ ] Validates: conceptId exists in curriculum graph, sessionId is active, student not rate-limited
- [ ] BKT update via `IBktService.Update()` (injected, microsecond scale)
- [ ] Stages `ConceptAttempted_V1` (always)
- [ ] Stages `XpAwarded_V1` if correct (with difficulty multiplier: 1x recall → 4x analysis)
- [ ] Stages `ConceptMastered_V1` if `posteriorMastery >= 0.85` AND `!state.ConceptMastery[conceptId].IsMastered`
- [ ] Flushes all staged events atomically
- [ ] Sends `UpdateSignals` to `StagnationDetectorActor` (fire-and-forget)
- [ ] Responds to caller with `AttemptResult(isCorrect, posteriorMastery, feedback)`

**Test:**
```csharp
[Fact]
public async Task AttemptConcept_CorrectAnswer_AwardsXpAndChecksMastery()
{
    var actor = CreateTestStudentActor("student-8");
    await actor.StartTestSession();

    var result = await actor.Handle(new AttemptConcept(
        StudentId: "student-8",
        ConceptId: "algebra-1",
        QuestionId: "q-1",
        Answer: "correct-answer",
        ...
    ));

    Assert.True(result.IsCorrect);
    Assert.True(result.PosteriorMastery > 0.5);

    // Events persisted
    var events = await GetStreamEvents("student-8");
    Assert.Contains(events, e => e is ConceptAttempted_V1);
    Assert.Contains(events, e => e is XpAwarded_V1);
}

[Fact]
public async Task AttemptConcept_WrongAnswer_NoXp()
{
    var result = await actor.Handle(new AttemptConcept(..., Answer: "wrong"));

    Assert.False(result.IsCorrect);
    var events = await GetStreamEvents("student-8");
    Assert.DoesNotContain(events, e => e is XpAwarded_V1);
}

[Fact]
public async Task AttemptConcept_CrossesMasteryThreshold_EmitsMasteredEvent()
{
    // Set prior mastery to 0.84 (just below threshold)
    SetMastery("student-9", "algebra-1", 0.84);

    // Correct answer pushes above 0.85
    var result = await actor.Handle(new AttemptConcept(..., IsCorrect: true));

    Assert.True(result.PosteriorMastery >= 0.85);
    var events = await GetStreamEvents("student-9");
    Assert.Contains(events, e => e is ConceptMastered_V1);
}

[Fact]
public async Task AttemptConcept_AlreadyMastered_NoSecondMasteredEvent()
{
    SetMastery("student-10", "algebra-1", 0.90, isMastered: true);

    await actor.Handle(new AttemptConcept(..., IsCorrect: true));

    var events = await GetStreamEvents("student-10");
    Assert.DoesNotContain(events, e => e is ConceptMastered_V1);
}

[Fact]
public async Task AttemptConcept_InvalidConceptId_ReturnsError()
{
    var result = await actor.Handle(new AttemptConcept(
        ..., ConceptId: "nonexistent-concept"));

    Assert.False(result.Success);
    Assert.Contains("not found", result.Error);
}
```

**Edge cases:**
- BKT returns P(known) > 1.0 → clamp to 1.0
- BKT returns P(known) < 0.0 → clamp to 0.0
- Concurrent attempts on same concept (race) → expected version handles it
- Student at daily LLM budget limit → evaluation uses cached rubric, not LLM

---

### ACT-002.5: Child Actor Spawning (Session, Stagnation, Outreach)
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — child lifecycle methods

**Acceptance:**
- [ ] `LearningSessionActor` spawned on `StartSession`, stopped on `EndSession`
- [ ] Only ONE active session at a time (reject `StartSession` if session already active)
- [ ] `StagnationDetectorActor` spawned on first activation, lives across sessions
- [ ] `OutreachSchedulerActor` spawned on first activation, manages HLR timers
- [ ] Children supervised: OneForOne, restart on failure, stop after 3 failures in 60s
- [ ] Lazy spawning: stagnation + outreach actors NOT spawned until first session (saves memory for inactive students)

**Test:**
```csharp
[Fact]
public async Task StudentActor_SpawnsSessionOnStart()
{
    var grain = _cluster.GetGrain<IStudentGrain>("student-11");
    await grain.StartSession(new StartSessionCommand("math", "socratic"));

    var profile = await grain.GetProfile();
    Assert.NotNull(profile.ActiveSessionId);
}

[Fact]
public async Task StudentActor_RejectsDuplicateSession()
{
    var grain = _cluster.GetGrain<IStudentGrain>("student-12");
    await grain.StartSession(new StartSessionCommand("math", "socratic"));

    var result = await grain.StartSession(new StartSessionCommand("physics", "drill"));
    Assert.False(result.Success);
    Assert.Contains("already active", result.Error);
}

[Fact]
public async Task StudentActor_ChildSurvivedRestart()
{
    var grain = _cluster.GetGrain<IStudentGrain>("student-13");
    await grain.StartSession(...);

    // Simulate child actor failure
    await CauseSessionActorFailure("student-13");

    // Child restarted (supervision)
    var profile = await grain.GetProfile();
    Assert.NotNull(profile.ActiveSessionId); // Session still active
}
```

---

### ACT-002.6: Offline Sync Handler (Idempotent)
**Files:**
- `src/Cena.Actors/Students/Handlers/SyncOfflineEventsHandler.cs`

**Acceptance:**
- [ ] Accepts `SyncOfflineEvents` command with list of offline events + idempotency keys
- [ ] Each event: check Redis `SET NX` → skip if key exists (duplicate)
- [ ] Events processed in chronological order (client timestamp, adjusted for clock skew)
- [ ] Three-tier classification: Unconditional (always accept), Conditional (validate context), ServerAuthoritative (recalculate)
- [ ] Conditional: full weight (1.0) if context matches, reduced (0.75) if methodology changed, 0.0 if concept removed
- [ ] ALL events staged → single `FlushEvents()` (atomic)
- [ ] Response: `SyncResult` with per-event status, XP delta, mastery changes, outreach corrections

**Test:**
```csharp
[Fact]
public async Task SyncOffline_ProcessesAndDeduplicates()
{
    var events = GenerateOfflineEvents(5, studentId: "student-14");

    var result1 = await grain.SyncOfflineEvents(new SyncOfflineEventsCommand(events));
    Assert.Equal(5, result1.AcceptedCount);

    // Retry same events
    var result2 = await grain.SyncOfflineEvents(new SyncOfflineEventsCommand(events));
    Assert.Equal(0, result2.AcceptedCount); // All skipped as duplicates

    // Marten has exactly 5 events
    var stream = await _store.LightweightSession().Events.FetchStreamAsync("student-14");
    Assert.Equal(5, stream.Count);
}

[Fact]
public async Task SyncOffline_ReducesWeightWhenMethodologyChanged()
{
    // Server switched methodology while student was offline
    await SetActiveMethodology("student-15", "algebra-1", "feynman");

    var offlineEvent = CreateOfflineAttempt("student-15", "algebra-1", methodology: "socratic");
    var result = await grain.SyncOfflineEvents(new SyncOfflineEventsCommand([offlineEvent]));

    // Event accepted at reduced weight
    Assert.Equal(0.75, result.EventResults[0].Weight);
}

[Fact]
public async Task SyncOffline_AtomicCommit()
{
    var events = GenerateOfflineEvents(10, studentId: "student-16");
    // Make event #5 invalid (references removed concept)
    events[4] = CreateInvalidOfflineEvent();

    // All events processed (invalid one gets weight 0.0, but still recorded)
    var result = await grain.SyncOfflineEvents(new SyncOfflineEventsCommand(events));
    Assert.Equal(10, result.AcceptedCount); // All accepted (some at weight 0)
    Assert.Equal(0.0, result.EventResults[4].Weight);
}
```

---

### ACT-002.7: Memory Budget + Metrics
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — monitoring methods

**Acceptance:**
- [ ] Memory estimate: `EstimateMemoryBytes()` includes state + child actors + overhead
- [ ] Memory budget: 500KB per actor (configurable)
- [ ] Warning at 80% budget, logged + metric emitted
- [ ] Over budget: log ERROR, trim `MethodAttemptHistory` to last 20 per cluster, trim `RecentAttempts` to 20
- [ ] Metrics: `cena.actor.memory_bytes` histogram, `cena.actor.activation_ms` histogram, `cena.actor.events_persisted` counter

**Test:**
```csharp
[Fact]
public void MemoryEstimate_IsReasonable()
{
    var state = CreateTestState(conceptCount: 500, historyDepth: 50);
    var estimate = state.EstimateMemoryBytes();

    Assert.True(estimate > 100_000); // At least 100KB for 500 concepts
    Assert.True(estimate < 512_000); // Under 500KB budget
}

[Fact]
public void MemoryBudgetExceeded_TrimsHistory()
{
    var state = CreateTestState(conceptCount: 2000, historyDepth: 100);
    Assert.True(state.EstimateMemoryBytes() > 512_000);

    state.TrimToMemoryBudget();
    Assert.True(state.EstimateMemoryBytes() <= 512_000);
    Assert.True(state.MethodAttemptHistory.Values.All(v => v.Count <= 20));
}
```

---

## Integration Test (full actor lifecycle)

```csharp
[Fact]
public async Task StudentActor_FullLifecycle()
{
    var grain = _cluster.GetGrain<IStudentGrain>("lifecycle-test");

    // 1. Start session
    var startResult = await grain.StartSession(new StartSessionCommand("math", "socratic"));
    Assert.True(startResult.Success);

    // 2. Attempt 5 concepts
    for (int i = 0; i < 5; i++)
    {
        var result = await grain.AttemptConcept(new AttemptConceptCommand(
            ConceptId: $"concept-{i}", IsCorrect: true, ...));
        Assert.True(result.Success);
    }

    // 3. End session
    await grain.EndSession(new EndSessionCommand("completed"));

    // 4. Verify state
    var profile = await grain.GetProfile();
    Assert.Equal(5, profile.EventVersion); // 5 attempts (XP + mastery events also counted)
    Assert.True(profile.TotalXp > 0);

    // 5. Passivate and reactivate
    await PassivateAndReactivate("lifecycle-test");

    // 6. State preserved
    var restored = await grain.GetProfile();
    Assert.Equal(profile.TotalXp, restored.TotalXp);
    Assert.Equal(profile.EventVersion, restored.EventVersion);

    // 7. Offline sync
    var offlineEvents = GenerateOfflineEvents(3, "lifecycle-test");
    var syncResult = await grain.SyncOfflineEvents(new SyncOfflineEventsCommand(offlineEvents));
    Assert.Equal(3, syncResult.AcceptedCount);
}
```

## Rollback Criteria
- If event sourcing is broken: revert to simple PostgreSQL CRUD (temporary, loses audit trail)
- If activation is too slow (>5s P95): increase snapshot frequency to 50
- If memory exceeds budget: reduce MaxTrackedConcepts from 2000 to 500

## Definition of Done
- [ ] All 7 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=StudentActor"` → 0 failures
- [ ] Load test: 100 concurrent actors, 10 attempts each → all succeed, P95 < 100ms
- [ ] Memory: 100 actors × 500KB = ~50MB (verified via process metrics)
- [ ] PR reviewed by architect (you)
