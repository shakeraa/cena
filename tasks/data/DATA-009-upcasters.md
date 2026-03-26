# DATA-009: Event Upcasting Pipeline (V1 -> V2)

**Priority:** P2 — required before first schema evolution
**Blocked by:** DATA-001 (Marten setup), DATA-002 (event types)
**Estimated effort:** 2 days
**Contract:** `contracts/data/marten-event-store.cs` (lines 97-103)

---

## Context
Event stores are append-only — you never change historical events. When a domain event schema evolves (e.g., `ConceptAttempted_V1` gains a new required field), Marten's upcasting pipeline transforms old events to the new shape at read time. This task builds the upcasting infrastructure, a sample V1->V2 upcaster, and tests that prove old events remain readable after schema evolution.

## Subtasks

### DATA-009.1: Upcaster Registration Infrastructure
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — `RegisterUpcasters` method
- `src/Cena.Data/EventStore/Upcasters/IEventUpcaster.cs` — marker interface
- `src/Cena.Data/EventStore/Upcasters/UpcasterRegistration.cs` — discovery and registration

**Acceptance:**
- [ ] `RegisterUpcasters(StoreOptions opts)` method in `MartenConfiguration`
- [ ] Upcasters registered via `opts.Events.Upcast<TOld, TNew>(transform)` — Marten 7.x inline upcaster API
- [ ] Upcasters are discoverable by convention: all types implementing `IEventUpcaster` in assembly
- [ ] Upcaster ordering: V1->V2 applied before V2->V3 (chained)
- [ ] Registration is idempotent — safe to call multiple times

**Test:**
```csharp
[Fact]
public void UpcasterRegistration_RegistersAllUpcasters()
{
    var opts = new StoreOptions();
    MartenConfiguration.RegisterUpcasters(opts);

    // After registration, Marten knows about the transform
    // Verify by storing V1 and reading as V2
    // (See DATA-009.2 for the concrete test)
}

[Fact]
public void UpcasterRegistration_IsIdempotent()
{
    var opts = new StoreOptions();
    MartenConfiguration.RegisterUpcasters(opts);
    MartenConfiguration.RegisterUpcasters(opts); // No exception
}
```

---

### DATA-009.2: Sample Upcaster — ConceptAttempted V1 -> V2
**Files:**
- `src/Cena.Domain/Events/Learner/ConceptAttempted_V2.cs` — new event version
- `src/Cena.Data/EventStore/Upcasters/ConceptAttemptedV1ToV2Upcaster.cs` — transform

**Acceptance:**
- [ ] `ConceptAttempted_V2` adds a new field: `CognitiveLoadEstimate` (double, default 0.0) and `BloomLevel` (string, default "recall")
- [ ] Upcaster maps all V1 fields to V2 fields 1:1
- [ ] New fields get sensible defaults: `CognitiveLoadEstimate = 0.0`, `BloomLevel = "recall"`
- [ ] Historical V1 events in the store read back as V2 after registration
- [ ] V2 events written directly include the new fields
- [ ] Both V1 and V2 can coexist in the same stream

**Test:**
```csharp
[Fact]
public async Task Upcaster_V1ToV2_TransformsAtReadTime()
{
    // Write a V1 event
    await using var writeSession = _store.LightweightSession();
    writeSession.Events.Append("upcast-test", new ConceptAttempted_V1(
        "s1", "c1", "sess1", true, 1500, "q1", "numeric", "socratic",
        "none", 0.5, 0.65, 0, false, "h", 2, 1, false,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    await writeSession.SaveChangesAsync();

    // Read — Marten should upcast V1 to V2
    await using var readSession = _store.LightweightSession();
    var events = await readSession.Events.FetchStreamAsync("upcast-test");
    var upcast = events[0].Data as ConceptAttempted_V2;

    Assert.NotNull(upcast);
    Assert.Equal("s1", upcast.StudentId);
    Assert.Equal(0.65, upcast.PosteriorMastery);
    // New fields have defaults
    Assert.Equal(0.0, upcast.CognitiveLoadEstimate);
    Assert.Equal("recall", upcast.BloomLevel);
}

[Fact]
public async Task Upcaster_V2Written_ReadsBackAsV2()
{
    await using var session = _store.LightweightSession();
    session.Events.Append("upcast-v2-test", new ConceptAttempted_V2(
        "s1", "c1", "sess1", true, 1500, "q1", "numeric", "socratic",
        "none", 0.5, 0.65, 0, false, "h", 2, 1, false,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        CognitiveLoadEstimate: 0.4, BloomLevel: "application"));
    await session.SaveChangesAsync();

    var events = await session.Events.FetchStreamAsync("upcast-v2-test");
    var v2 = events[0].Data as ConceptAttempted_V2;
    Assert.Equal(0.4, v2!.CognitiveLoadEstimate);
    Assert.Equal("application", v2.BloomLevel);
}

[Fact]
public async Task Upcaster_MixedStream_HandlesV1AndV2()
{
    await using var session = _store.LightweightSession();
    session.Events.Append("mixed-stream", new ConceptAttempted_V1(
        "s1", "c1", "sess1", true, 1000, "q1", "numeric", "socratic",
        "none", 0.5, 0.6, 0, false, "h", 0, 0, false, DateTimeOffset.UtcNow));
    session.Events.Append("mixed-stream", new ConceptAttempted_V2(
        "s1", "c1", "sess1", false, 2000, "q2", "numeric", "socratic",
        "conceptual", 0.6, 0.55, 1, false, "h2", 3, 2, false, DateTimeOffset.UtcNow,
        CognitiveLoadEstimate: 0.6, BloomLevel: "understand"));
    await session.SaveChangesAsync();

    var events = await session.Events.FetchStreamAsync("mixed-stream");
    Assert.Equal(2, events.Count);
    // Both read as V2
    Assert.All(events, e => Assert.IsType<ConceptAttempted_V2>(e.Data));
}
```

---

### DATA-009.3: Snapshot Compatibility After Upcasting
**Files:**
- `src/Cena.Data/EventStore/StudentProfileSnapshot.cs` — Add Apply(ConceptAttempted_V2) method

**Acceptance:**
- [ ] `StudentProfileSnapshot.Apply(ConceptAttempted_V2 e)` handles new fields (CognitiveLoadEstimate stored or ignored as appropriate)
- [ ] Existing snapshots remain valid — they were built from V1 events
- [ ] New snapshots built from upcast events include the new data
- [ ] Snapshot determinism preserved: two replays of the same mixed V1+V2 stream produce identical state

**Test:**
```csharp
[Fact]
public void Snapshot_Apply_V2_HandlesNewFields()
{
    var snapshot = new StudentProfileSnapshot();
    snapshot.Apply(new ConceptAttempted_V2(
        "s1", "c1", "sess1", true, 1500, "q1", "numeric", "socratic",
        "none", 0.5, 0.72, 0, false, "h", 0, 0, false, DateTimeOffset.UtcNow,
        CognitiveLoadEstimate: 0.3, BloomLevel: "application"));

    Assert.Equal(0.72, snapshot.ConceptMastery["c1"].PKnown);
}

[Fact]
public void Snapshot_MixedV1V2_DeterministicReplay()
{
    var events = new object[]
    {
        new ConceptAttempted_V2("s1","c1","sess1",true,1000,"q1","numeric","socratic","none",0.5,0.6,0,false,"h",0,0,false,
            new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero), CognitiveLoadEstimate: 0.0, BloomLevel: "recall"),
        new ConceptAttempted_V2("s1","c1","sess1",true,1200,"q2","numeric","socratic","none",0.6,0.72,0,false,"h",0,0,false,
            new DateTimeOffset(2026,1,1,1,0,0,TimeSpan.Zero), CognitiveLoadEstimate: 0.2, BloomLevel: "understand"),
    };

    var s1 = new StudentProfileSnapshot();
    var s2 = new StudentProfileSnapshot();
    foreach (var e in events) { ApplyEvent(s1, e); ApplyEvent(s2, e); }

    Assert.Equal(JsonSerializer.Serialize(s1), JsonSerializer.Serialize(s2));
}
```

**Edge cases:**
- Upcast chain V1->V2->V3: Marten applies upcasters in registration order
- Corrupted V1 event (missing field after schema change) -> upcaster provides default, logs WARNING
- Snapshot built from V1 events, then V2 events arrive -> Apply(V2) handles gracefully
- Rollback from V2 to V1 code -> V2 events in store become unreadable; need down-caster or ignore unknown

---

## Integration Test

```csharp
[Fact]
public async Task Upcasting_EndToEnd()
{
    // 1. Write V1 events
    await using var write1 = _store.LightweightSession();
    for (int i = 0; i < 50; i++)
        write1.Events.Append("upcast-e2e", new ConceptAttempted_V1(
            "s1", $"c-{i%5}", "sess1", true, 1000, $"q-{i}", "numeric",
            "socratic", "none", 0.5, 0.6, 0, false, "h", 0, 0, false,
            DateTimeOffset.UtcNow.AddMinutes(i)));
    await write1.SaveChangesAsync();

    // 2. Read back — all upcast to V2
    await using var read = _store.LightweightSession();
    var events = await read.Events.FetchStreamAsync("upcast-e2e");
    Assert.All(events, e => Assert.IsType<ConceptAttempted_V2>(e.Data));

    // 3. Aggregate still works
    var snapshot = await read.Events.AggregateStreamAsync<StudentProfileSnapshot>("upcast-e2e");
    Assert.Equal(5, snapshot!.ConceptMastery.Count);

    // 4. Write V2 events to the same stream
    await using var write2 = _store.LightweightSession();
    write2.Events.Append("upcast-e2e", new ConceptAttempted_V2(
        "s1", "c-0", "sess2", true, 1000, "q-51", "numeric", "socratic",
        "none", 0.6, 0.75, 0, false, "h", 0, 0, false, DateTimeOffset.UtcNow,
        CognitiveLoadEstimate: 0.3, BloomLevel: "apply"));
    await write2.SaveChangesAsync();

    // 5. All 51 events readable
    var allEvents = await read.Events.FetchStreamAsync("upcast-e2e");
    Assert.Equal(51, allEvents.Count);
}
```

## Rollback Criteria
- If upcasting breaks read performance: disable and keep V1 types, add V2 as separate event type
- If Marten upcaster API changes in a minor version: pin Marten version
- If snapshot incompatibility: full rebuild snapshots from raw events

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=Upcasters"` -> 0 failures
- [ ] V1 events persisted before upcaster registration read back as V2
- [ ] Mixed V1+V2 streams aggregate correctly
- [ ] Snapshot determinism maintained across upcast events
- [ ] PR reviewed by architect
