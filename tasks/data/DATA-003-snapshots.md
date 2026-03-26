# DATA-003: Deterministic Snapshot Apply Methods & Replay

**Priority:** P0 — actors cannot activate without correct state restoration
**Blocked by:** DATA-001 (Marten setup), DATA-002 (event types)
**Estimated effort:** 3 days
**Contract:** `contracts/data/marten-event-store.cs` (lines 310-395), `contracts/actors/student_actor.cs`

---

## Context
The `StudentProfileSnapshot` is the aggregate state rebuilt from events. Marten snapshots every 100 events and replays only the delta on activation. Every Apply method must be **deterministic** — using `e.Timestamp` instead of wall clock — so that replay produces identical state regardless of when it runs. A single non-deterministic Apply method breaks the entire event sourcing guarantee.

## Subtasks

### DATA-003.1: Apply Methods for All Event Types
**Files:**
- `src/Cena.Data/EventStore/StudentProfileSnapshot.cs` — all Apply overloads
- `src/Cena.Data/EventStore/ConceptMasteryState.cs` — nested state class

**Acceptance:**
- [ ] `Apply(ConceptAttempted_V1 e)`: upserts `ConceptMastery[e.ConceptId]`, sets `PKnown = e.PosteriorMastery`, increments `TotalAttempts`, sets `LastAttemptedAt = e.Timestamp`, sets `LastMethodology = e.MethodologyActive`
- [ ] `Apply(ConceptMastered_V1 e)`: sets `IsMastered = true`, `MasteredAt = e.Timestamp`, writes `HalfLifeMap[e.ConceptId] = e.InitialHalfLifeHours`
- [ ] `Apply(MasteryDecayed_V1 e)`: sets `IsMastered = false`, `PKnown = e.PredictedRecall`
- [ ] `Apply(MethodologySwitched_V1 e)`: updates `ActiveMethodologyMap[e.ConceptId]`, appends to `MethodAttemptHistory`
- [ ] `Apply(XpAwarded_V1 e)`: sets `TotalXp = e.TotalXp` (cumulative, not additive)
- [ ] `Apply(StreakUpdated_V1 e)`: sets `CurrentStreak`, `LongestStreak`, `LastActivityDate`
- [ ] `Apply(SessionStarted_V1 e)`: increments `SessionCount`, sets `ExperimentCohort ??= e.ExperimentCohort`
- [ ] ZERO calls to `DateTimeOffset.UtcNow`, `DateTime.Now`, or `DateTime.UtcNow` anywhere in Apply methods
- [ ] `ConceptMasteryState` class: `PKnown` (double), `IsMastered` (bool), `TotalAttempts` (int), `LastAttemptedAt` (DateTimeOffset?), `MasteredAt` (DateTimeOffset?), `LastMethodology` (string?)

**Test:**
```csharp
[Fact]
public void Apply_ConceptAttempted_UsesEventTimestamp()
{
    var snapshot = new StudentProfileSnapshot();
    var eventTime = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    snapshot.Apply(new ConceptAttempted_V1(
        "s1", "math-addition", "sess1", true, 1500, "q1", "numeric",
        "socratic", "none", 0.5, 0.72, 0, false, "h", 0, 0, false, eventTime));

    Assert.Equal(eventTime, snapshot.ConceptMastery["math-addition"].LastAttemptedAt);
    Assert.Equal(0.72, snapshot.ConceptMastery["math-addition"].PKnown);
    Assert.Equal(1, snapshot.ConceptMastery["math-addition"].TotalAttempts);
}

[Fact]
public void Apply_ConceptMastered_SetsHalfLife()
{
    var snapshot = new StudentProfileSnapshot();
    var ts = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    snapshot.Apply(new ConceptMastered_V1("s1", "c1", "sess1", 0.9, 10, 3, "socratic", 48.0, ts));

    Assert.True(snapshot.ConceptMastery["c1"].IsMastered);
    Assert.Equal(ts, snapshot.ConceptMastery["c1"].MasteredAt);
    Assert.Equal(48.0, snapshot.HalfLifeMap["c1"]);
}

[Fact]
public void Apply_MasteryDecayed_ClearsMastered()
{
    var snapshot = new StudentProfileSnapshot();
    snapshot.Apply(new ConceptMastered_V1("s1", "c1", "sess1", 0.9, 10, 3, "socratic", 48.0, DateTimeOffset.UtcNow));
    Assert.True(snapshot.ConceptMastery["c1"].IsMastered);

    snapshot.Apply(new MasteryDecayed_V1("s1", "c1", 0.45, 24.0, 72.0));
    Assert.False(snapshot.ConceptMastery["c1"].IsMastered);
    Assert.Equal(0.45, snapshot.ConceptMastery["c1"].PKnown);
}

[Fact]
public void Apply_XpAwarded_IsCumulative()
{
    var snapshot = new StudentProfileSnapshot();
    snapshot.Apply(new XpAwarded_V1("s1", 50, "exercise_correct", 50, "recall", 1));
    Assert.Equal(50, snapshot.TotalXp);
    snapshot.Apply(new XpAwarded_V1("s1", 30, "mastery", 80, "comprehension", 2));
    Assert.Equal(80, snapshot.TotalXp); // Uses TotalXp, not additive
}
```

---

### DATA-003.2: Snapshot Configuration & Lifecycle
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — snapshot registration
- `src/Cena.Data/EventStore/StudentProfileSnapshot.cs` — snapshot class

**Acceptance:**
- [ ] `opts.Projections.Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline, 100)` — snapshot every 100 events
- [ ] Snapshot stored in the same PostgreSQL schema as events
- [ ] Snapshot includes all fields from `StudentProfileSnapshot`: `StudentId`, `ConceptMastery`, `ActiveMethodologyMap`, `MethodAttemptHistory`, `HalfLifeMap`, `TotalXp`, `CurrentStreak`, `LongestStreak`, `LastActivityDate`, `ExperimentCohort`, `BaselineAccuracy`, `BaselineResponseTimeMs`, `SessionCount`, `CreatedAt`
- [ ] Snapshot survives schema evolution: new nullable fields with defaults
- [ ] Snapshot size does NOT grow unbounded: `MethodAttemptHistory` capped at 20 entries per cluster

**Test:**
```csharp
[Fact]
public async Task Snapshot_IsCreatedAfter100Events()
{
    await using var session = _store.LightweightSession();
    for (int i = 0; i < 100; i++)
    {
        session.Events.Append("snapshot-lifecycle", new ConceptAttempted_V1(
            "snapshot-lifecycle", $"concept-{i % 10}", "sess1", i % 2 == 0, 1500,
            $"q-{i}", "numeric", "socratic", "none", 0.5, 0.6, 0, false,
            "h", 0, 0, false, DateTimeOffset.UtcNow.AddMinutes(i)));
    }
    await session.SaveChangesAsync();

    // Aggregate should have snapshot at version 100
    var aggregate = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>("snapshot-lifecycle");
    Assert.NotNull(aggregate);
    Assert.Equal(10, aggregate.ConceptMastery.Count); // 10 unique concepts
}

[Fact]
public async Task Snapshot_ActivationLoadsFromSnapshotPlusDelta()
{
    await using var session = _store.LightweightSession();
    // Write 150 events
    for (int i = 0; i < 150; i++)
    {
        session.Events.Append("snapshot-delta", new ConceptAttempted_V1(
            "snapshot-delta", "concept-0", "sess1", true, 1500,
            $"q-{i}", "numeric", "socratic", "none", 0.5, 0.5 + i * 0.001, 0, false,
            "h", 0, 0, false, DateTimeOffset.UtcNow.AddMinutes(i)));
    }
    await session.SaveChangesAsync();

    // Aggregate reflects all 150 events
    var aggregate = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>("snapshot-delta");
    Assert.Equal(150, aggregate.ConceptMastery["concept-0"].TotalAttempts);
}
```

---

### DATA-003.3: Replay Determinism Tests
**Files:**
- `tests/Cena.Data.Tests/Snapshots/ReplayDeterminismTests.cs`

**Acceptance:**
- [ ] Two independent replays of the same event stream produce identical state (byte-for-byte JSON comparison)
- [ ] Replay does NOT call `RecalculateBaselines()` per event — only once at the end
- [ ] Replay of 10,000 events completes in < 1 second
- [ ] Replay order matches event stream order (version-ordered)

**Test:**
```csharp
[Fact]
public void Replay_IsDeterministic()
{
    var events = GenerateTestEventSequence(500, seed: 42);

    var snapshot1 = new StudentProfileSnapshot();
    foreach (var e in events)
        ApplyEvent(snapshot1, e);

    var snapshot2 = new StudentProfileSnapshot();
    foreach (var e in events)
        ApplyEvent(snapshot2, e);

    var json1 = JsonSerializer.Serialize(snapshot1);
    var json2 = JsonSerializer.Serialize(snapshot2);
    Assert.Equal(json1, json2);
}

[Fact]
public void Replay_DoesNotUseWallClock()
{
    // Create events with timestamps 1 year apart
    var e1 = new ConceptAttempted_V1("s1", "c1", "sess1", true, 1000, "q1", "numeric",
        "socratic", "none", 0.5, 0.6, 0, false, "h", 0, 0, false,
        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    var e2 = new ConceptAttempted_V1("s1", "c1", "sess2", true, 1000, "q2", "numeric",
        "socratic", "none", 0.6, 0.7, 0, false, "h", 0, 0, false,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    var snapshot = new StudentProfileSnapshot();
    snapshot.Apply(e1);
    snapshot.Apply(e2);

    // LastAttemptedAt is from the event, not from when replay ran
    Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        snapshot.ConceptMastery["c1"].LastAttemptedAt);
}

[Fact]
public void Replay_10kEvents_UnderOneSecond()
{
    var events = GenerateTestEventSequence(10_000, seed: 99);
    var snapshot = new StudentProfileSnapshot();
    var sw = Stopwatch.StartNew();

    foreach (var e in events)
        ApplyEvent(snapshot, e);

    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds < 1000, $"Replay took {sw.ElapsedMilliseconds}ms");
}

[Fact]
public void Replay_SnapshotPlusDelta_MatchesFullReplay()
{
    var events = GenerateTestEventSequence(250, seed: 42);

    // Full replay
    var full = new StudentProfileSnapshot();
    foreach (var e in events) ApplyEvent(full, e);

    // Snapshot at 100, then replay 101-250
    var partial = new StudentProfileSnapshot();
    foreach (var e in events.Take(100)) ApplyEvent(partial, e);

    // Simulate restoring from snapshot (serialize/deserialize)
    var snapshotJson = JsonSerializer.Serialize(partial);
    var restored = JsonSerializer.Deserialize<StudentProfileSnapshot>(snapshotJson)!;

    foreach (var e in events.Skip(100)) ApplyEvent(restored, e);

    Assert.Equal(JsonSerializer.Serialize(full), JsonSerializer.Serialize(restored));
}
```

**Edge cases:**
- Snapshot is corrupt (deserialization fails) -> fall back to full replay from event 0, log ERROR
- Stream has 10,000+ events with no snapshot -> replay succeeds but is slow; log WARNING and trigger immediate snapshot write
- Event stream contains an unknown event type -> skip it with WARNING log, do not crash
- Concurrent snapshot writes -> Marten handles via versioned append

---

## Integration Test

```csharp
[Fact]
public async Task Snapshot_EndToEnd_PersistRestoreReplay()
{
    // 1. Write 150 events
    await using var writeSession = _store.LightweightSession();
    for (int i = 0; i < 150; i++)
    {
        writeSession.Events.Append("e2e-snapshot", new ConceptAttempted_V1(
            "e2e-snapshot", $"concept-{i % 5}", "sess1", i % 3 != 0, 1000 + i * 10,
            $"q-{i}", "numeric", "socratic", i % 3 == 0 ? "procedural" : "none",
            0.5, 0.5 + (i % 5) * 0.05, 0, false, $"hash-{i}", 0, 0, false,
            DateTimeOffset.UtcNow.AddMinutes(i)));
    }
    await writeSession.SaveChangesAsync();

    // 2. Aggregate (uses snapshot + replay)
    await using var readSession = _store.LightweightSession();
    var snapshot = await readSession.Events.AggregateStreamAsync<StudentProfileSnapshot>("e2e-snapshot");

    // 3. Verify state
    Assert.NotNull(snapshot);
    Assert.Equal(5, snapshot.ConceptMastery.Count);
    Assert.True(snapshot.ConceptMastery.Values.All(c => c.TotalAttempts == 30));

    // 4. Full replay for comparison
    var events = await readSession.Events.FetchStreamAsync("e2e-snapshot");
    var fullReplay = new StudentProfileSnapshot();
    foreach (var e in events)
        ApplyEvent(fullReplay, e.Data);

    Assert.Equal(
        JsonSerializer.Serialize(snapshot.ConceptMastery),
        JsonSerializer.Serialize(fullReplay.ConceptMastery));
}
```

## Rollback Criteria
- If snapshot deserialization breaks on upgrade: disable snapshots temporarily, full replay only (slower but correct)
- If Apply method performance degrades: profile and optimize the hot path (ConceptAttempted_V1)
- If snapshot size exceeds 1MB: reduce collection caps or move to summary snapshots

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Snapshots"` -> 0 failures
- [ ] Determinism verified: two independent replays of 500-event stream produce identical JSON
- [ ] Performance: 10,000-event replay < 1 second
- [ ] No wall-clock calls in any Apply method (verified by code review and static analysis)
- [ ] Snapshot + delta replay matches full replay (property-based test)
- [ ] PR reviewed by architect
