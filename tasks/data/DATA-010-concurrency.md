# DATA-010: Optimistic Concurrency with Expected Version on Marten

**Priority:** P0 — without this, concurrent writes silently corrupt state
**Blocked by:** DATA-001 (Marten setup), DATA-002 (event types)
**Estimated effort:** 1 day
**Contract:** `contracts/data/marten-event-store.cs`, `contracts/actors/student_actor.cs`

---

## Context
The StudentActor uses optimistic concurrency control via Marten's expected version mechanism. When flushing staged events, the actor passes its known `EventVersion` to `session.Events.Append()`. If another writer has appended events since the actor last read the stream, Marten throws `EventStreamUnexpectedMaxEventIdException`. This prevents lost-update anomalies when two actor activations (across cluster nodes) write to the same stream.

## Subtasks

### DATA-010.1: Expected Version on Append
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — `FlushEvents()` method
- `src/Cena.Data/EventStore/EventStoreExtensions.cs` — helper for versioned append

**Acceptance:**
- [ ] `session.Events.Append(streamKey, expectedVersion, events)` used in `FlushEvents()`
- [ ] `expectedVersion` is `_state.EventVersion` — the version after the last successful read or write
- [ ] On successful append: `_state.EventVersion += pendingEvents.Count`
- [ ] On `EventStreamUnexpectedMaxEventIdException`: pending events NOT cleared, log WARNING, retry possible after state refresh
- [ ] State refresh: re-read stream from `_state.EventVersion`, replay delta events, then retry flush
- [ ] NATS publish happens AFTER Marten commit succeeds (outbox pattern)
- [ ] Snapshot trigger check runs AFTER version increment (every 100 events)

**Test:**
```csharp
[Fact]
public async Task FlushEvents_UsesExpectedVersion()
{
    await using var session = _store.LightweightSession();
    session.Events.Append("version-test", 0,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q1","numeric",
            "socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await session.SaveChangesAsync();

    // Second write at correct expected version succeeds
    await using var session2 = _store.LightweightSession();
    session2.Events.Append("version-test", 1,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q2","numeric",
            "socratic","none",0.6,0.7,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await session2.SaveChangesAsync(); // Should succeed
}

[Fact]
public async Task FlushEvents_ThrowsOnVersionConflict()
{
    await using var session1 = _store.LightweightSession();
    session1.Events.Append("conflict-test", 0,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q1","numeric",
            "socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await session1.SaveChangesAsync();

    // Concurrent write: another process appends at version 1
    await using var session2 = _store.LightweightSession();
    session2.Events.Append("conflict-test", 1,
        new XpAwarded_V1("s1",10,"exercise_correct",10,"recall",1));
    await session2.SaveChangesAsync();

    // Our write at expected version 1 fails (stream is now at version 2)
    await using var session3 = _store.LightweightSession();
    session3.Events.Append("conflict-test", 1,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q3","numeric",
            "socratic","none",0.6,0.7,0,false,"h",0,0,false,DateTimeOffset.UtcNow));

    await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(
        () => session3.SaveChangesAsync());
}

[Fact]
public async Task FlushEvents_RetryAfterRefresh()
{
    // Setup: stream at version 1
    await using var setup = _store.LightweightSession();
    setup.Events.Append("retry-test", 0,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q1","numeric",
            "socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await setup.SaveChangesAsync();

    // Simulate actor at version 1; external write moves to version 2
    await using var external = _store.LightweightSession();
    external.Events.Append("retry-test", 1,
        new XpAwarded_V1("s1",10,"exercise_correct",10,"recall",1));
    await external.SaveChangesAsync();

    // Actor refreshes state (re-reads stream)
    await using var refresh = _store.LightweightSession();
    var stream = await refresh.Events.FetchStreamAsync("retry-test");
    var currentVersion = stream.Count; // 2

    // Retry at correct version
    refresh.Events.Append("retry-test", currentVersion,
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q3","numeric",
            "socratic","none",0.6,0.7,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await refresh.SaveChangesAsync(); // Succeeds at version 2
}
```

---

### DATA-010.2: Actor-Level Concurrency Guard
**Files:**
- `src/Cena.Actors/Students/StudentActor.cs` — concurrency handling in message handler
- `tests/Cena.Actors.Tests/Students/ConcurrencyTests.cs`

**Acceptance:**
- [ ] Single-writer guarantee within an actor: Proto.Actor ensures only one message processed at a time per grain
- [ ] Cross-node conflict: when two nodes activate the same grain (split-brain), expected version catches it
- [ ] On conflict: log ERROR with stream key, expected version, actual version
- [ ] Retry policy: max 3 retries with state refresh between each
- [ ] After 3 failures: return error to caller, do NOT corrupt state
- [ ] Metric: `cena.actor.version_conflict_total` counter

**Test:**
```csharp
[Fact]
public async Task Actor_HandlesVersionConflict_WithRetry()
{
    // This test simulates a version conflict by externally appending
    // to the stream between the actor's read and write.
    var actor = CreateTestStudentActor("conflict-actor");
    await actor.RestoreState(); // Loads stream, sets EventVersion

    // External write to simulate split-brain
    await AppendExternalEvent("conflict-actor");

    // Actor tries to flush — gets conflict, refreshes, retries
    actor.StageEvent(new ConceptAttempted_V1("conflict-actor","c1","sess1",
        true,1000,"q1","numeric","socratic","none",0.5,0.6,
        0,false,"h",0,0,false,DateTimeOffset.UtcNow));

    var result = await actor.FlushEventsWithRetry(maxRetries: 3);
    Assert.True(result.Success);
    Assert.Equal(1, result.RetryCount); // One retry needed
}

[Fact]
public async Task Actor_FailsAfterMaxRetries()
{
    var actor = CreateTestStudentActor("max-retry-actor");
    await actor.RestoreState();

    // Keep appending externally to exhaust retries
    _concurrentWriter.StartContinuousAppend("max-retry-actor");

    actor.StageEvent(new ConceptAttempted_V1("max-retry-actor","c1","sess1",
        true,1000,"q1","numeric","socratic","none",0.5,0.6,
        0,false,"h",0,0,false,DateTimeOffset.UtcNow));

    var result = await actor.FlushEventsWithRetry(maxRetries: 3);
    Assert.False(result.Success);
    Assert.Equal(3, result.RetryCount);

    _concurrentWriter.Stop();
}
```

**Edge cases:**
- Stream does not exist yet (first write) -> expected version = 0
- Rapid sequential writes from same actor -> version increments correctly between flushes
- Network partition heals, both nodes try to write -> first succeeds, second gets conflict
- PostgreSQL transaction timeout during append -> treated as failure, retry

---

## Integration Test

```csharp
[Fact]
public async Task Concurrency_TwoActors_OnlyOneWins()
{
    // Simulate two actor activations writing to the same stream
    var task1 = Task.Run(async () =>
    {
        await using var session = _store.LightweightSession();
        session.Events.Append("race-test", 0,
            new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q1","numeric",
                "socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();
        return "task1";
    });

    var task2 = Task.Run(async () =>
    {
        await Task.Delay(10); // Slight delay to increase race likelihood
        await using var session = _store.LightweightSession();
        session.Events.Append("race-test", 0,
            new XpAwarded_V1("s1",10,"exercise_correct",10,"recall",1));
        await session.SaveChangesAsync();
        return "task2";
    });

    var results = await Task.WhenAll(
        Task.Run(async () => { try { return await task1; } catch { return "failed"; } }),
        Task.Run(async () => { try { return await task2; } catch { return "failed"; } })
    );

    // Exactly one should succeed, one should fail
    Assert.Contains("failed", results);
    Assert.True(results.Count(r => r != "failed") == 1);
}
```

## Rollback Criteria
- If optimistic concurrency causes too many retries (>5% of writes): investigate cluster topology; ensure single-activation guarantee
- If Marten version check is too slow: switch to PostgreSQL advisory locks

## Definition of Done
- [ ] All 2 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Concurrency"` -> 0 failures
- [ ] Version conflict is caught and logged with full context
- [ ] Retry with state refresh succeeds on transient conflicts
- [ ] No silent data corruption under concurrent writes
- [ ] PR reviewed by architect
