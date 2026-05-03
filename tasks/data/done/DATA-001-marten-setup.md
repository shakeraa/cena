# DATA-001: PostgreSQL + Marten v7 Event Store Configuration

**Priority:** P0 — blocks ALL actor state persistence
**Blocked by:** INF-001 (PostgreSQL RDS instance running)
**Estimated effort:** 3 days
**Contract:** `contracts/data/marten-event-store.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Marten v7 is the event store and document database backing every StudentActor. Every domain event (ConceptAttempted, ConceptMastered, MethodologySwitched, etc.) is appended to a PostgreSQL-backed event stream keyed by student UUID. The snapshot strategy rebuilds `StudentProfileSnapshot` every 100 events. Inline projections provide zero-latency reads for the knowledge graph UI, while async projections feed teacher dashboards and analytics. This task sets up the Marten configuration, registers all event types, and wires the inline + async projections defined in `marten-event-store.cs`.

## Subtasks

### DATA-001.1: NuGet Dependencies & Marten StoreOptions
**Files to create/modify:**
- `src/Cena.Data/Cena.Data.csproj` — new .NET 9 class library
- `src/Cena.Data/EventStore/MartenConfiguration.cs` — implements `ConfigureCenaEventStore()` from contract

**Acceptance:**
- [ ] `dotnet new classlib -n Cena.Data -f net9.0`
- [ ] NuGet packages installed:
  ```xml
  <PackageReference Include="Marten" Version="7.*" />
  <PackageReference Include="Weasel.Core" Version="7.*" />
  <PackageReference Include="Npgsql" Version="9.*" />
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.*" />
  ```
- [ ] `StoreOptions` configured exactly as in `marten-event-store.cs`:
  - `opts.Connection(connectionString)` from env `CENA_POSTGRES_CONNECTION`
  - `opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate`
  - `opts.DatabaseSchemaName = "cena"`
  - `opts.Events.StreamIdentity = StreamIdentity.AsString` (student UUID as stream key)
  - `opts.Events.MetadataConfig.EnableAll()` (full metadata on all events)
  - `opts.Events.TenancyStyle = TenancyStyle.Single`
- [ ] Serialization: `System.Text.Json` with `EnumStorage.AsString`, `Casing.CamelCase`
- [ ] Snapshot: `Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline, 100)` — every 100 events
- [ ] Solution builds: `dotnet build` exits 0

**Test:**
```csharp
[Fact]
public void MartenConfiguration_SetsCorrectSchema()
{
    var services = new ServiceCollection();
    services.AddMarten(opts => opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test"));
    var provider = services.BuildServiceProvider();
    var store = provider.GetRequiredService<IDocumentStore>();

    Assert.Equal("cena", store.Options.DatabaseSchemaName);
    Assert.Equal(StreamIdentity.AsString, store.Options.Events.StreamIdentity);
    Assert.Equal(AutoCreate.CreateOrUpdate, store.Options.AutoCreateSchemaObjects);
}

[Fact]
public void MartenConfiguration_UsesSystemTextJson()
{
    var opts = new StoreOptions();
    opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");

    // Verify enum serialization
    Assert.Equal(EnumStorage.AsString, opts.Serializer().EnumStorage);
}
```

---

### DATA-001.2: Event Type Registration
**Files to create/modify:**
- `src/Cena.Data/EventStore/Events/LearnerEvents.cs` — all 7 learner context events
- `src/Cena.Data/EventStore/Events/PedagogyEvents.cs` — all 5 pedagogy events
- `src/Cena.Data/EventStore/Events/EngagementEvents.cs` — all 5 engagement events
- `src/Cena.Data/EventStore/Events/OutreachEvents.cs` — all 3 outreach events

**Acceptance:**
- [ ] All 20 event types registered from `marten-event-store.cs`:
  - **Learner (7):** `ConceptAttempted_V1`, `ConceptMastered_V1`, `MasteryDecayed_V1`, `MethodologySwitched_V1`, `StagnationDetected_V1`, `AnnotationAdded_V1`, `CognitiveLoadCooldownComplete_V1`
  - **Pedagogy (5):** `SessionStarted_V1`, `SessionEnded_V1`, `ExercisePresented_V1`, `HintRequested_V1`, `QuestionSkipped_V1`
  - **Engagement (5):** `XpAwarded_V1`, `StreakUpdated_V1`, `BadgeEarned_V1`, `StreakExpiring_V1`, `ReviewDue_V1`
  - **Outreach (3):** `OutreachMessageSent_V1`, `OutreachMessageDelivered_V1`, `OutreachResponseReceived_V1`
- [ ] All events are C# records (immutable by design) matching exact field signatures from contract
- [ ] `ConceptAttempted_V1` includes all 17 fields: `StudentId`, `ConceptId`, `SessionId`, `IsCorrect`, `ResponseTimeMs`, `QuestionId`, `QuestionType`, `MethodologyActive`, `ErrorType`, `PriorMastery`, `PosteriorMastery`, `HintCountUsed`, `WasSkipped`, `AnswerHash`, `BackspaceCount`, `AnswerChangeCount`, `WasOffline`, `Timestamp`
- [ ] `ConceptMastered_V1` includes: `StudentId`, `ConceptId`, `SessionId`, `MasteryLevel`, `TotalAttempts`, `TotalSessions`, `MethodologyAtMastery`, `InitialHalfLifeHours`, `Timestamp`
- [ ] `XpAwarded_V1` includes difficulty-scaled fields: `DifficultyLevel`, `DifficultyMultiplier` (1x recall, 2x comprehension, 3x application, 4x analysis)
- [ ] `SessionEnded_V1.EndReason` supports: "completed", "fatigue", "abandoned", "timeout", "app_backgrounded"
- [ ] Upcaster registration method exists (empty — for future V1->V2 migrations)

**Test:**
```csharp
[Fact]
public async Task AllEventTypes_CanBeAppendedAndRead()
{
    var store = CreateTestStore();
    var session = store.LightweightSession();
    var streamId = "test-student-001";

    // Append one event of each type
    session.Events.Append(streamId, new ConceptAttempted_V1(
        StudentId: streamId, ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 2, AnswerChangeCount: 1, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    ));

    session.Events.Append(streamId, new ConceptMastered_V1(
        StudentId: streamId, ConceptId: "math-fractions", SessionId: "sess-1",
        MasteryLevel: 0.90, TotalAttempts: 15, TotalSessions: 3,
        MethodologyAtMastery: "socratic", InitialHalfLifeHours: 48.0,
        Timestamp: DateTimeOffset.UtcNow
    ));

    session.Events.Append(streamId, new SessionStarted_V1(
        StudentId: streamId, SessionId: "sess-1", DeviceType: "tablet",
        AppVersion: "2.1.0", Methodology: "socratic",
        ExperimentCohort: "control", IsOffline: false,
        ClientTimestamp: DateTimeOffset.UtcNow
    ));

    await session.SaveChangesAsync();

    // Read back
    var events = await session.Events.FetchStreamAsync(streamId);
    Assert.Equal(3, events.Count);
    Assert.IsType<ConceptAttempted_V1>(events[0].Data);
}

[Fact]
public void AllTwentyEventTypes_AreRegistered()
{
    var opts = new StoreOptions();
    opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");

    var registeredTypes = opts.Events.AllKnownEventTypes();
    Assert.True(registeredTypes.Count() >= 20,
        $"Expected 20+ event types, got {registeredTypes.Count()}");
}

[Fact]
public void XpAwarded_HasDifficultyFields()
{
    var xp = new XpAwarded_V1(
        StudentId: "s1", XpAmount: 30, Source: "exercise_correct",
        TotalXp: 500, DifficultyLevel: "application", DifficultyMultiplier: 3
    );
    Assert.Equal(3, xp.DifficultyMultiplier);
    Assert.Equal("application", xp.DifficultyLevel);
}
```

**Edge cases:**
- Event with null optional fields (e.g., `ExperimentCohort = null`) -> serializes correctly as JSON null
- Event timestamp in different timezone -> `DateTimeOffset` preserves timezone, but all storage is UTC
- Future V2 event added -> upcaster maps V1 fields to V2 with defaults for new fields

---

### DATA-001.3: StudentProfileSnapshot & Apply Methods
**Files to create/modify:**
- `src/Cena.Data/EventStore/Snapshots/StudentProfileSnapshot.cs` — snapshot aggregate matching contract
- `src/Cena.Data/EventStore/Snapshots/ConceptMasteryState.cs` — per-concept mastery state

**Acceptance:**
- [ ] `StudentProfileSnapshot` has all fields from contract:
  - `StudentId`, `ConceptMastery` (dict), `ActiveMethodologyMap` (conceptId -> methodology), `MethodAttemptHistory` (conceptCluster -> methods tried), `HalfLifeMap` (conceptId -> hours), `TotalXp`, `CurrentStreak`, `LongestStreak`, `LastActivityDate`, `ExperimentCohort`, `BaselineAccuracy`, `BaselineResponseTimeMs`, `SessionCount`, `CreatedAt`
- [ ] `ConceptMasteryState` has: `PKnown`, `IsMastered`, `TotalAttempts`, `LastAttemptedAt`, `MasteredAt`, `LastMethodology`
- [ ] Apply methods match contract exactly:
  - `Apply(ConceptAttempted_V1 e)`: updates PKnown=PosteriorMastery, increments TotalAttempts, sets LastAttemptedAt=**e.Timestamp** (NOT wall clock — deterministic replay)
  - `Apply(ConceptMastered_V1 e)`: sets IsMastered=true, MasteredAt=**e.Timestamp**, updates HalfLifeMap
  - `Apply(MasteryDecayed_V1 e)`: sets IsMastered=false, PKnown=PredictedRecall
  - `Apply(MethodologySwitched_V1 e)`: updates ActiveMethodologyMap, appends to MethodAttemptHistory
  - `Apply(XpAwarded_V1 e)`: sets TotalXp
  - `Apply(StreakUpdated_V1 e)`: sets CurrentStreak, LongestStreak, LastActivityDate
  - `Apply(SessionStarted_V1 e)`: increments SessionCount, sets ExperimentCohort (first write wins)
- [ ] Snapshot is rebuilt every 100 events (configured in DATA-001.1)
- [ ] Snapshot Apply uses event timestamp, NOT `DateTimeOffset.UtcNow` (deterministic replay)

**Test:**
```csharp
[Fact]
public void Snapshot_AppliesConceptAttempted()
{
    var snapshot = new StudentProfileSnapshot { StudentId = "s1" };
    var timestamp = DateTimeOffset.Parse("2026-03-15T10:00:00Z");

    snapshot.Apply(new ConceptAttempted_V1(
        StudentId: "s1", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 2, AnswerChangeCount: 1, WasOffline: false,
        Timestamp: timestamp
    ));

    Assert.True(snapshot.ConceptMastery.ContainsKey("math-fractions"));
    Assert.Equal(0.55, snapshot.ConceptMastery["math-fractions"].PKnown);
    Assert.Equal(1, snapshot.ConceptMastery["math-fractions"].TotalAttempts);
    Assert.Equal(timestamp, snapshot.ConceptMastery["math-fractions"].LastAttemptedAt);
}

[Fact]
public void Snapshot_AppliesConceptMastered_UseEventTimestamp()
{
    var snapshot = new StudentProfileSnapshot { StudentId = "s1" };
    var eventTime = DateTimeOffset.Parse("2026-03-15T10:30:00Z");

    snapshot.Apply(new ConceptMastered_V1(
        StudentId: "s1", ConceptId: "math-fractions", SessionId: "sess-1",
        MasteryLevel: 0.90, TotalAttempts: 15, TotalSessions: 3,
        MethodologyAtMastery: "socratic", InitialHalfLifeHours: 48.0,
        Timestamp: eventTime
    ));

    Assert.True(snapshot.ConceptMastery["math-fractions"].IsMastered);
    Assert.Equal(eventTime, snapshot.ConceptMastery["math-fractions"].MasteredAt);
    Assert.Equal(48.0, snapshot.HalfLifeMap["math-fractions"]);
}

[Fact]
public void Snapshot_MethodologySwitchAppends()
{
    var snapshot = new StudentProfileSnapshot { StudentId = "s1" };
    snapshot.Apply(new MethodologySwitched_V1(
        StudentId: "s1", ConceptId: "math-fractions",
        PreviousMethodology: "socratic", NewMethodology: "worked_examples",
        Trigger: "stagnation_detected", StagnationScore: 0.72,
        DominantErrorType: "procedural", McmConfidence: 0.87
    ));

    Assert.Equal("worked_examples", snapshot.ActiveMethodologyMap["math-fractions"]);
    Assert.Contains("worked_examples", snapshot.MethodAttemptHistory["math-fractions"]);
}

[Fact]
public void Snapshot_SessionStarted_FirstCohortWins()
{
    var snapshot = new StudentProfileSnapshot { StudentId = "s1" };
    snapshot.Apply(new SessionStarted_V1("s1", "sess-1", "tablet", "2.0", "socratic", "cohort-A", false, DateTimeOffset.UtcNow));
    snapshot.Apply(new SessionStarted_V1("s1", "sess-2", "phone", "2.1", "socratic", "cohort-B", false, DateTimeOffset.UtcNow));

    Assert.Equal("cohort-A", snapshot.ExperimentCohort);  // First write wins
    Assert.Equal(2, snapshot.SessionCount);
}
```

**Edge cases:**
- 100+ events replayed from scratch (no snapshot) -> all Apply methods idempotent, order matters
- Unknown event type in stream (future V2 event) -> Marten skips it, logs WARNING
- Snapshot serialization with empty dictionaries -> no null reference exceptions

---

### DATA-001.4: CQRS Projections (Inline + Async)
**Files to create/modify:**
- `src/Cena.Data/EventStore/Projections/StudentMasteryProjection.cs` — inline, per-student mastery view
- `src/Cena.Data/EventStore/Projections/ClassOverviewProjection.cs` — inline, class-level summary
- `src/Cena.Data/EventStore/Projections/TeacherDashboardProjection.cs` — async
- `src/Cena.Data/EventStore/Projections/ParentProgressProjection.cs` — async
- `src/Cena.Data/EventStore/Projections/MethodologyEffectivenessProjection.cs` — async
- `src/Cena.Data/EventStore/Projections/RetentionCohortProjection.cs` — async

**Acceptance:**
- [ ] Inline projections registered:
  - `StudentMasteryProjection` (ProjectionLifecycle.Inline) -> `StudentMasteryView`
  - `ClassOverviewProjection` (ProjectionLifecycle.Inline) -> `ClassOverviewView`
- [ ] Async projections registered:
  - `TeacherDashboardProjection` (ProjectionLifecycle.Async)
  - `ParentProgressProjection` (ProjectionLifecycle.Async)
  - `MethodologyEffectivenessProjection` (ProjectionLifecycle.Async)
  - `RetentionCohortProjection` (ProjectionLifecycle.Async)
- [ ] `StudentMasteryProjection.Apply(ConceptAttempted_V1)`: updates `MasteryMap[conceptId]` = `PosteriorMastery`, `LastUpdated` = **event Timestamp**, `ConceptsInProgress` = count where 0.3 < P(known) < 0.85
- [ ] `StudentMasteryProjection.Apply(ConceptMastered_V1)`: `MasteryMap[conceptId]` = `MasteryLevel`, `ConceptsMastered` = count where P(known) >= 0.85, `LastUpdated` = **event Timestamp**
- [ ] `StudentMasteryView` has: `Id` (StudentId), `MasteryMap`, `ConceptsMastered`, `ConceptsInProgress`, `TotalXp`, `CurrentStreak`, `LastUpdated`

**Test:**
```csharp
[Fact]
public async Task StudentMasteryProjection_UpdatesMasteryMap()
{
    var store = CreateTestStore();
    var session = store.LightweightSession();

    session.Events.Append("student-001", new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "s1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    ));
    await session.SaveChangesAsync();

    var view = await session.LoadAsync<StudentMasteryView>("student-001");
    Assert.NotNull(view);
    Assert.Equal(0.55, view.MasteryMap["math-fractions"]);
    Assert.Equal(1, view.ConceptsInProgress);
}

[Fact]
public async Task StudentMasteryProjection_CountsMastered()
{
    var store = CreateTestStore();
    var session = store.LightweightSession();

    session.Events.Append("student-001", new ConceptMastered_V1(
        StudentId: "student-001", ConceptId: "math-addition", SessionId: "s1",
        MasteryLevel: 0.92, TotalAttempts: 10, TotalSessions: 2,
        MethodologyAtMastery: "socratic", InitialHalfLifeHours: 48.0,
        Timestamp: DateTimeOffset.UtcNow
    ));
    await session.SaveChangesAsync();

    var view = await session.LoadAsync<StudentMasteryView>("student-001");
    Assert.Equal(1, view.ConceptsMastered);
}
```

**Edge cases:**
- Async projection daemon lags behind -> dashboard shows stale data (acceptable for async views)
- Projection rebuild after schema migration -> Marten supports full rebuild from event stream
- Inline projection failure -> Marten throws on SaveChanges, event NOT committed (transactional)

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullMartenSetup_EndToEnd()
{
    var store = CreateTestStore();
    var session = store.LightweightSession();
    var studentId = "integration-test-student";

    // 1. Append a session start
    session.Events.Append(studentId, new SessionStarted_V1(
        studentId, "sess-1", "tablet", "2.0", "socratic", "control", false, DateTimeOffset.UtcNow
    ));

    // 2. Append concept attempts
    for (int i = 0; i < 5; i++)
    {
        session.Events.Append(studentId, new ConceptAttempted_V1(
            StudentId: studentId, ConceptId: "math-fractions", SessionId: "sess-1",
            IsCorrect: i >= 3, ResponseTimeMs: 5000, QuestionId: $"q{i}",
            QuestionType: "numeric", MethodologyActive: "socratic",
            ErrorType: i < 3 ? "procedural" : "none",
            PriorMastery: 0.4 + i * 0.05, PosteriorMastery: 0.45 + i * 0.05,
            HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
            BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
            Timestamp: DateTimeOffset.UtcNow
        ));
    }

    // 3. Append mastery event
    session.Events.Append(studentId, new ConceptMastered_V1(
        StudentId: studentId, ConceptId: "math-fractions", SessionId: "sess-1",
        MasteryLevel: 0.90, TotalAttempts: 5, TotalSessions: 1,
        MethodologyAtMastery: "socratic", InitialHalfLifeHours: 48.0,
        Timestamp: DateTimeOffset.UtcNow
    ));

    await session.SaveChangesAsync();

    // 4. Verify inline projection
    var masteryView = await session.LoadAsync<StudentMasteryView>(studentId);
    Assert.NotNull(masteryView);
    Assert.Equal(1, masteryView.ConceptsMastered);

    // 5. Verify event stream
    var events = await session.Events.FetchStreamAsync(studentId);
    Assert.Equal(7, events.Count); // 1 session + 5 attempts + 1 mastered

    // 6. Verify snapshot (after 100 events — not triggered here, but config verified)
    var snapshotConfig = store.Options.Projections.All.OfType<SnapshotProjection>();
    // Snapshot lifecycle is Inline
}
```

## Rollback Criteria
If Marten configuration causes data issues:
- Revert to raw PostgreSQL event table (no projections, manual queries)
- All actor state persistence degrades but is not lost (events are append-only)
- Async projections can be independently stopped without affecting core write path

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=MartenSetup"` -> 0 failures
- [ ] All 20 event types registered and round-trip serializable
- [ ] Snapshot Apply uses event timestamps (deterministic replay verified)
- [ ] Inline projections update on SaveChanges
- [ ] Schema creates correctly on fresh PostgreSQL database
- [ ] PR reviewed by architect (you)
