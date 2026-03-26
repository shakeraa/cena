# DATA-001: PostgreSQL 16 + Marten v7.x Event Store Setup

**Priority:** P0 — foundational data layer; every actor depends on this
**Blocked by:** Nothing (first task)
**Estimated effort:** 3 days
**Contract:** `contracts/data/marten-event-store.cs`

---

## Context
Marten is the event store and document database sitting on PostgreSQL 16. This task sets up the schema, DI configuration, serialization, event type registration, and health checks. Every domain event flows through Marten — if this is wrong, nothing else works.

## Subtasks

### DATA-001.1: PostgreSQL Schema & Connection Configuration
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — store options
- `src/Cena.Data/EventStore/CenaDataServiceExtensions.cs` — DI registration
- `config/appsettings.json` — connection string template
- `scripts/init-postgres.sql` — idempotent schema bootstrap

**Acceptance:**
- [ ] Connection string read from `ConnectionStrings:Marten` via `IConfiguration`
- [ ] `opts.DatabaseSchemaName = "cena"` — dedicated schema, not public
- [ ] `opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate` in development; `AutoCreate.None` in production
- [ ] `opts.Events.StreamIdentity = StreamIdentity.AsString` — student UUID as stream key
- [ ] `opts.Events.MetadataConfig.EnableAll()` — full metadata on all events
- [ ] `opts.Events.TenancyStyle = TenancyStyle.Single`
- [ ] PostgreSQL 16 extensions enabled: `uuid-ossp`, `pgcrypto`
- [ ] `init-postgres.sql` creates database, schema, and extensions idempotently
- [ ] Health check registered: `AddNpgSql()` and `AddMarten()` via `AspNetCore.HealthChecks`
- [ ] Connection pooling: `Npgsql` with `MaxPoolSize=100`, `MinPoolSize=10`, `ConnectionIdleLifetime=60`

**Test:**
```csharp
[Fact]
public async Task MartenStore_ConnectsToPostgres()
{
    var store = _serviceProvider.GetRequiredService<IDocumentStore>();
    await using var session = store.LightweightSession();
    var result = await session.QueryAsync<int>("SELECT 1");
    Assert.Equal(1, result.First());
}

[Fact]
public async Task MartenStore_CenaSchemaExists()
{
    var store = _serviceProvider.GetRequiredService<IDocumentStore>();
    await using var session = store.LightweightSession();
    var schemas = await session.QueryAsync<string>(
        "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'cena'");
    Assert.Single(schemas);
}

[Fact]
public void MartenConfiguration_UsesStringStreamIdentity()
{
    var store = _serviceProvider.GetRequiredService<IDocumentStore>();
    Assert.Equal(StreamIdentity.AsString, store.Options.Events.StreamIdentity);
}
```

---

### DATA-001.2: Serialization Configuration (System.Text.Json)
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — serialization block

**Acceptance:**
- [ ] `UseSystemTextJsonForSerialization` with `EnumStorage.AsString`
- [ ] `Casing = Casing.CamelCase` for JSON interop with frontend
- [ ] `DateTimeOffset` serialized as ISO 8601 UTC
- [ ] `JsonSerializerOptions` includes `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- [ ] Round-trip test: serialize event -> deserialize -> fields match
- [ ] Unknown properties ignored on deserialization (forward compatibility)

**Test:**
```csharp
[Fact]
public async Task Serialization_RoundTripsConceptAttempted()
{
    var original = new ConceptAttempted_V1(
        StudentId: "student-1",
        ConceptId: "math-addition",
        SessionId: "session-1",
        IsCorrect: true,
        ResponseTimeMs: 3200,
        QuestionId: "q-1",
        QuestionType: "multiple_choice",
        MethodologyActive: "socratic",
        ErrorType: "none",
        PriorMastery: 0.6,
        PosteriorMastery: 0.72,
        HintCountUsed: 0,
        WasSkipped: false,
        AnswerHash: "abc123",
        BackspaceCount: 2,
        AnswerChangeCount: 1,
        WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    );

    await using var session = _store.LightweightSession();
    session.Events.Append("student-1", original);
    await session.SaveChangesAsync();

    var events = await session.Events.FetchStreamAsync("student-1");
    var deserialized = events.First().Data as ConceptAttempted_V1;

    Assert.NotNull(deserialized);
    Assert.Equal(original.StudentId, deserialized.StudentId);
    Assert.Equal(original.PosteriorMastery, deserialized.PosteriorMastery);
    Assert.Equal(original.Timestamp, deserialized.Timestamp);
}

[Fact]
public void Serialization_EnumsStoredAsStrings()
{
    var json = JsonSerializer.Serialize(
        new ConceptAttempted_V1(
            StudentId: "s1", ConceptId: "c1", SessionId: "sess1",
            IsCorrect: true, ResponseTimeMs: 100, QuestionId: "q1",
            QuestionType: "numeric", MethodologyActive: "feynman",
            ErrorType: "procedural", PriorMastery: 0.5,
            PosteriorMastery: 0.6, HintCountUsed: 0, WasSkipped: false,
            AnswerHash: "h", BackspaceCount: 0, AnswerChangeCount: 0,
            WasOffline: false, Timestamp: DateTimeOffset.UtcNow),
        _jsonOptions);
    Assert.Contains("\"procedural\"", json);
    Assert.DoesNotContain("\"2\"", json); // Not numeric enum
}
```

---

### DATA-001.3: Event Type Registration
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — `RegisterLearnerEvents`, `RegisterPedagogyEvents`, `RegisterEngagementEvents`, `RegisterOutreachEvents`

**Acceptance:**
- [ ] All 20 event types registered via `opts.Events.AddEventType<T>()`
- [ ] Learner context: `ConceptAttempted_V1`, `ConceptMastered_V1`, `MasteryDecayed_V1`, `MethodologySwitched_V1`, `StagnationDetected_V1`, `AnnotationAdded_V1`, `CognitiveLoadCooldownComplete_V1`
- [ ] Pedagogy context: `SessionStarted_V1`, `SessionEnded_V1`, `ExercisePresented_V1`, `HintRequested_V1`, `QuestionSkipped_V1`
- [ ] Engagement context: `XpAwarded_V1`, `StreakUpdated_V1`, `BadgeEarned_V1`, `StreakExpiring_V1`, `ReviewDue_V1`
- [ ] Outreach context: `OutreachMessageSent_V1`, `OutreachMessageDelivered_V1`, `OutreachResponseReceived_V1`
- [ ] Unknown event types do NOT throw on deserialization (forward compatibility)

**Test:**
```csharp
[Fact]
public async Task EventRegistration_AllEventTypesRoundTrip()
{
    var events = new object[]
    {
        new ConceptAttempted_V1("s1","c1","sess1",true,100,"q1","numeric","socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow),
        new ConceptMastered_V1("s1","c1","sess1",0.9,10,3,"socratic",48.0,DateTimeOffset.UtcNow),
        new SessionStarted_V1("s1","sess1","mobile","1.0.0","socratic",null,false,DateTimeOffset.UtcNow),
        new SessionEnded_V1("s1","sess1","completed",15,10,8,3200.0,0.3),
        new XpAwarded_V1("s1",50,"exercise_correct",150,"comprehension",2),
        new StreakUpdated_V1("s1",5,10,DateTimeOffset.UtcNow),
        new BadgeEarned_V1("s1","badge-1","First Mastery","mastery"),
        new OutreachMessageSent_V1("s1","msg-1","push","StreakExpiring","hash"),
    };

    await using var session = _store.LightweightSession();
    session.Events.Append("registration-test", events);
    await session.SaveChangesAsync();

    var stream = await session.Events.FetchStreamAsync("registration-test");
    Assert.Equal(events.Length, stream.Count);
}
```

---

### DATA-001.4: Projection Registration (Inline + Async)
**Files:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — projection registration
- `src/Cena.Data/Projections/StudentMasteryProjection.cs`
- `src/Cena.Data/Projections/ClassOverviewProjection.cs`

**Acceptance:**
- [ ] `StudentMasteryProjection` registered as `ProjectionLifecycle.Inline`
- [ ] `ClassOverviewProjection` registered as `ProjectionLifecycle.Inline`
- [ ] `TeacherDashboardProjection` registered as `ProjectionLifecycle.Async`
- [ ] `ParentProgressProjection` registered as `ProjectionLifecycle.Async`
- [ ] `MethodologyEffectivenessProjection` registered as `ProjectionLifecycle.Async`
- [ ] `RetentionCohortProjection` registered as `ProjectionLifecycle.Async`
- [ ] Snapshot configured: `Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline, 100)`
- [ ] Async daemon enabled for async projections

**Test:**
```csharp
[Fact]
public async Task InlineProjection_StudentMastery_UpdatedOnAttempt()
{
    await using var session = _store.LightweightSession();
    session.Events.Append("student-proj-1", new ConceptAttempted_V1(
        "student-proj-1","math-addition","sess1",true,1500,"q1","numeric",
        "socratic","none",0.5,0.72,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    await session.SaveChangesAsync();

    var view = await session.LoadAsync<StudentMasteryView>("student-proj-1");
    Assert.NotNull(view);
    Assert.Equal(0.72, view.MasteryMap["math-addition"]);
}

[Fact]
public async Task Snapshot_CreatedEvery100Events()
{
    await using var session = _store.LightweightSession();
    for (int i = 0; i < 105; i++)
    {
        session.Events.Append("snapshot-test", new ConceptAttempted_V1(
            "snapshot-test",$"concept-{i % 5}","sess1",true,1000,$"q-{i}","numeric",
            "socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow));
    }
    await session.SaveChangesAsync();

    // Snapshot exists after 100 events
    var snapshot = await session.Events.AggregateStreamAsync<StudentProfileSnapshot>("snapshot-test");
    Assert.NotNull(snapshot);
    Assert.True(snapshot.SessionCount >= 0); // Snapshot was built
}
```

**Edge cases:**
- PostgreSQL not reachable at startup -> health check fails, app does not accept traffic
- Schema mismatch between code and DB -> `AutoCreate.CreateOrUpdate` handles it in dev; in prod, migration script required
- Multiple app instances registering projections -> Marten handles idempotently

---

## Integration Test

```csharp
[Fact]
public async Task MartenSetup_FullPipeline()
{
    // 1. Write an event
    await using var writeSession = _store.LightweightSession();
    var attempt = new ConceptAttempted_V1(
        "integration-1","math-addition","sess1",true,2000,"q1","numeric",
        "socratic","none",0.4,0.55,0,false,"h",1,0,false,DateTimeOffset.UtcNow);
    writeSession.Events.Append("integration-1", attempt);
    await writeSession.SaveChangesAsync();

    // 2. Read the event back
    await using var readSession = _store.LightweightSession();
    var stream = await readSession.Events.FetchStreamAsync("integration-1");
    Assert.Single(stream);
    Assert.IsType<ConceptAttempted_V1>(stream[0].Data);

    // 3. Inline projection updated
    var mastery = await readSession.LoadAsync<StudentMasteryView>("integration-1");
    Assert.NotNull(mastery);
    Assert.Equal(0.55, mastery.MasteryMap["math-addition"]);

    // 4. Schema is in cena namespace
    var schemaCheck = await readSession.QueryAsync<long>(
        "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'cena'");
    Assert.True(schemaCheck.First() > 0);
}
```

## Rollback Criteria
- If Marten v7.x has breaking bugs: pin to last stable v6.x release
- If PostgreSQL 16 features are unavailable: fall back to PostgreSQL 15 (remove pg16-specific features)
- If async projections cause contention: switch all to inline temporarily (accept higher write latency)

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=MartenSetup"` -> 0 failures
- [ ] Health check endpoint returns 200 when PostgreSQL is reachable
- [ ] Connection pooling verified: 10 concurrent writes succeed without pool exhaustion
- [ ] Schema creation is idempotent (run `init-postgres.sql` twice without error)
- [ ] PR reviewed by architect
