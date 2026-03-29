# DATA-004: CQRS Async Read Model Views (Gap Fill)

**Priority:** P1 — blocks teacher/parent dashboards and analytics pipeline
**Blocked by:** DATA-001 (Marten setup), DATA-002 (event types registered)
**Estimated effort:** 3 days
**Contract:** `contracts/data/marten-event-store.cs` (lines 468-487 — stub projections)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The Marten event store contract registers four async projections: `TeacherDashboardProjection`, `ParentProgressProjection`, `MethodologyEffectivenessProjection`, and `RetentionCohortProjection`. The `TeacherDashboardProjection` is covered elsewhere. This task implements the three remaining read model views and their projections that are currently stubs with only `{ public string Id { get; set; } = ""; }`. Each view needs a full schema, Apply methods for relevant events, and query patterns.

## Subtasks

### DATA-004.1: ParentProgressView Schema + Projection
**Files:**
- `src/Cena.Data/ReadModels/ParentProgressView.cs` — view schema
- `src/Cena.Data/ReadModels/ParentProgressProjection.cs` — `SingleStreamProjection<ParentProgressView>`

**Acceptance:**
- [ ] View schema (replaces the stub in `marten-event-store.cs`):
  ```csharp
  public class ParentProgressView
  {
      public string Id { get; set; } = "";                        // StudentId
      public string StudentNameHe { get; set; } = "";             // Display name (Hebrew)
      public int TotalXp { get; set; }
      public int XpThisWeek { get; set; }
      public int CurrentStreak { get; set; }
      public int LongestStreak { get; set; }
      public int SessionsThisWeek { get; set; }
      public int TotalSessionMinutesThisWeek { get; set; }
      public int ConceptsMastered { get; set; }
      public int ConceptsInProgress { get; set; }
      public Dictionary<string, double> SubjectProgress { get; set; } = new(); // subject → % mastered
      public List<string> RecentBadges { get; set; } = new();     // Last 5 badges
      public DateTimeOffset LastActiveAt { get; set; }
      public DateTimeOffset WeekStartsAt { get; set; }            // Rolling 7-day window anchor
  }
  ```
- [ ] Projection lifecycle: `ProjectionLifecycle.Async` (registered in Marten config)
- [ ] Apply methods for:
  - `SessionStarted_V1` — increment `SessionsThisWeek`, update `LastActiveAt`
  - `SessionEnded_V1` — add `DurationMinutes` to `TotalSessionMinutesThisWeek`
  - `ConceptAttempted_V1` — update `ConceptsInProgress` count
  - `ConceptMastered_V1` — increment `ConceptsMastered`, update `SubjectProgress`
  - `XpAwarded_V1` — update `TotalXp` and `XpThisWeek`
  - `StreakUpdated_V1` — update `CurrentStreak`, track `LongestStreak`
  - `BadgeEarned_V1` — prepend to `RecentBadges` (keep last 5)
- [ ] Weekly rollover: `XpThisWeek`, `SessionsThisWeek`, `TotalSessionMinutesThisWeek` reset when event timestamp crosses week boundary from `WeekStartsAt`
- [ ] All Apply methods use `e.Timestamp` (NOT `DateTimeOffset.UtcNow`)

**Test:**
```csharp
[Fact]
public async Task ParentProgressView_TracksWeeklyProgress()
{
    var studentId = Guid.CreateVersion7().ToString();
    var now = DateTimeOffset.UtcNow;

    await AppendEvents(studentId, new object[]
    {
        new SessionStarted_V1(studentId, "s1", "mobile", "1.0", "Socratic", null, false, now),
        new ConceptAttempted_V1(studentId, "alg-1", "s1", true, 3000, "q1",
            "MC", "Socratic", "None", 0.3, 0.5, 0, false, "h1", 1, 0, false),
        new XpAwarded_V1(studentId, 10, "correct", 10),
        new ConceptMastered_V1(studentId, "alg-1", 0.90, 5, "Socratic", now),
        new BadgeEarned_V1(studentId, "first-mastery", "First Mastery!", now),
        new SessionEnded_V1(studentId, "s1", SessionEndReason.Completed, 15, 5, 4, now),
    });

    await WaitForAsyncProjection<ParentProgressView>();

    var view = await QueryView<ParentProgressView>(studentId);

    Assert.Equal(10, view.TotalXp);
    Assert.Equal(10, view.XpThisWeek);
    Assert.Equal(1, view.ConceptsMastered);
    Assert.Equal(1, view.SessionsThisWeek);
    Assert.Equal(15, view.TotalSessionMinutesThisWeek);
    Assert.Contains("first-mastery", view.RecentBadges);
}

[Fact]
public async Task ParentProgressView_ResetsWeeklyCounters()
{
    var studentId = Guid.CreateVersion7().ToString();
    var lastWeek = DateTimeOffset.UtcNow.AddDays(-8);
    var thisWeek = DateTimeOffset.UtcNow;

    await AppendEvents(studentId, new object[]
    {
        new XpAwarded_V1(studentId, 50, "correct", 50) { Timestamp = lastWeek },
        new XpAwarded_V1(studentId, 10, "correct", 60) { Timestamp = thisWeek },
    });

    await WaitForAsyncProjection<ParentProgressView>();
    var view = await QueryView<ParentProgressView>(studentId);

    Assert.Equal(60, view.TotalXp);        // Cumulative
    Assert.Equal(10, view.XpThisWeek);     // Only this week
}
```

---

### DATA-004.2: MethodologyEffectivenessView Schema + Projection
**Files:**
- `src/Cena.Data/ReadModels/MethodologyEffectivenessView.cs` — view schema
- `src/Cena.Data/ReadModels/MethodologyEffectivenessProjection.cs` — `MultiStreamProjection<MethodologyEffectivenessView, string>`

**Acceptance:**
- [ ] View schema (replaces stub):
  ```csharp
  public class MethodologyEffectivenessView
  {
      public string Id { get; set; } = "";  // "{methodology}:{conceptCategory}" composite key
      public string Methodology { get; set; } = "";
      public string ConceptCategory { get; set; } = "";
      public int TotalAttempts { get; set; }
      public int CorrectAttempts { get; set; }
      public double AccuracyRate { get; set; }
      public double AvgMasteryDelta { get; set; }           // Average mastery improvement per attempt
      public int StudentsUsing { get; set; }                 // Distinct students
      public int ConceptsMasteredUnderMethod { get; set; }   // Mastery events under this methodology
      public double AvgAttemptsToMastery { get; set; }
      public int StagnationEventsUnderMethod { get; set; }   // Stagnation while using this method
      public double EffectivenessScore { get; set; }         // Composite: accuracy * masteryRate - stagnationRate
      public DateTimeOffset LastUpdated { get; set; }
  }
  ```
- [ ] Multi-stream projection keyed by `"{methodology}:{conceptCategory}"` — aggregates across ALL students
- [ ] Apply methods for:
  - `ConceptAttempted_V1` — increment `TotalAttempts`, `CorrectAttempts` (if correct), recompute `AccuracyRate`, accumulate `MasteryDelta` (posteriorMastery - priorMastery)
  - `ConceptMastered_V1` — increment `ConceptsMasteredUnderMethod` where `ActiveMethodology` matches
  - `MethodologySwitched_V1` — track `StudentsUsing` (HashSet of studentIds internally)
  - `StagnationDetected_V1` — increment `StagnationEventsUnderMethod` where methodology matches
- [ ] `EffectivenessScore` recomputed on each Apply: `(AccuracyRate * 0.4) + (masteryRate * 0.4) - (stagnationRate * 0.2)` where `masteryRate = ConceptsMastered / max(1, TotalAttempts / 10)` and `stagnationRate = StagnationEvents / max(1, TotalAttempts / 50)`
- [ ] This view feeds the MCM confidence recalculation in Flywheel 3

**Test:**
```csharp
[Fact]
public async Task MethodologyEffectivenessView_AggregatesAcrossStudents()
{
    var student1 = Guid.CreateVersion7().ToString();
    var student2 = Guid.CreateVersion7().ToString();

    await AppendEvents(student1, new object[]
    {
        new ConceptAttempted_V1(student1, "alg-1", "s1", true, 3000, "q1",
            "MC", "Socratic", "None", 0.3, 0.5, 0, false, "h1", 1, 0, false),
        new ConceptMastered_V1(student1, "alg-1", 0.90, 5, "Socratic",
            DateTimeOffset.UtcNow),
    });

    await AppendEvents(student2, new object[]
    {
        new ConceptAttempted_V1(student2, "alg-2", "s2", false, 5000, "q2",
            "MC", "Socratic", "None", 0.3, 0.25, 0, false, "h2", 1, 0, false),
    });

    await WaitForAsyncProjection<MethodologyEffectivenessView>();

    var view = await QueryView<MethodologyEffectivenessView>("Socratic:algebra");

    Assert.Equal(2, view.TotalAttempts);
    Assert.Equal(1, view.CorrectAttempts);
    Assert.Equal(0.5, view.AccuracyRate, precision: 2);
    Assert.Equal(1, view.ConceptsMasteredUnderMethod);
}

[Fact]
public async Task MethodologyEffectivenessView_TracksStagnation()
{
    var studentId = Guid.CreateVersion7().ToString();

    await AppendEvents(studentId, new object[]
    {
        new StagnationDetected_V1(studentId, "alg-1", 0.75, "accuracy_plateau",
            3, "Socratic", DateTimeOffset.UtcNow),
    });

    await WaitForAsyncProjection<MethodologyEffectivenessView>();
    var view = await QueryView<MethodologyEffectivenessView>("Socratic:algebra");

    Assert.Equal(1, view.StagnationEventsUnderMethod);
}
```

---

### DATA-004.3: RetentionCohortView Schema + Projection
**Files:**
- `src/Cena.Data/ReadModels/RetentionCohortView.cs` — view schema
- `src/Cena.Data/ReadModels/RetentionCohortProjection.cs` — `MultiStreamProjection<RetentionCohortView, string>`

**Acceptance:**
- [ ] View schema (replaces stub):
  ```csharp
  public class RetentionCohortView
  {
      public string Id { get; set; } = "";  // "{cohortWeek}" e.g., "2026-W13"
      public string CohortWeek { get; set; } = "";           // ISO week of first session
      public int TotalStudents { get; set; }                  // Students who started this week
      public int ActiveD1 { get; set; }                       // Active within 1 day of signup
      public int ActiveD7 { get; set; }                       // Active within 7 days
      public int ActiveD30 { get; set; }                      // Active within 30 days
      public double RetentionD1 { get; set; }                 // ActiveD1 / TotalStudents
      public double RetentionD7 { get; set; }                 // ActiveD7 / TotalStudents
      public double RetentionD30 { get; set; }                // ActiveD30 / TotalStudents
      public Dictionary<string, int> MethodologyBreakdown { get; set; } = new();  // methodology → student count
      public int AvgSessionsPerRetainedStudent { get; set; }
      public DateTimeOffset LastUpdated { get; set; }
  }
  ```
- [ ] Multi-stream projection keyed by ISO week of first `SessionStarted_V1`
- [ ] Apply methods for:
  - `SessionStarted_V1` — determine cohort week from first session; increment `TotalStudents` on first session; track D1/D7/D30 activity windows; recompute retention rates
  - `SessionEnded_V1` — update `AvgSessionsPerRetainedStudent`
  - `MethodologySwitched_V1` — update `MethodologyBreakdown`
- [ ] Cohort assignment: student's cohort = ISO week of their FIRST `SessionStarted_V1` event
- [ ] D1/D7/D30 windows measured from cohort week start (Monday 00:00 UTC)
- [ ] Retention recalculated on each new session event

**Test:**
```csharp
[Fact]
public async Task RetentionCohortView_TracksD1D7D30()
{
    var student1 = Guid.CreateVersion7().ToString();
    var student2 = Guid.CreateVersion7().ToString();
    var cohortStart = new DateTimeOffset(2026, 3, 23, 0, 0, 0, TimeSpan.Zero); // Monday

    // Student 1: active D1, D7
    await AppendEvents(student1, new object[]
    {
        new SessionStarted_V1(student1, "s1", "mobile", "1.0", "Socratic",
            null, false, cohortStart),
        new SessionStarted_V1(student1, "s2", "mobile", "1.0", "Socratic",
            null, false, cohortStart.AddDays(5)),
    });

    // Student 2: active D1 only
    await AppendEvents(student2, new object[]
    {
        new SessionStarted_V1(student2, "s3", "mobile", "1.0", "Drill",
            null, false, cohortStart.AddHours(12)),
    });

    await WaitForAsyncProjection<RetentionCohortView>();

    var view = await QueryView<RetentionCohortView>("2026-W13");

    Assert.Equal(2, view.TotalStudents);
    Assert.Equal(2, view.ActiveD1);
    Assert.Equal(1.0, view.RetentionD1, precision: 2);
    Assert.Equal(1, view.ActiveD7);  // Only student1 returned within 7 days
    Assert.Equal(0.5, view.RetentionD7, precision: 2);
}

[Fact]
public async Task RetentionCohortView_AssignsCohortByFirstSession()
{
    var studentId = Guid.CreateVersion7().ToString();
    var week13Start = new DateTimeOffset(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);
    var week14Start = week13Start.AddDays(7);

    await AppendEvents(studentId, new object[]
    {
        new SessionStarted_V1(studentId, "s1", "mobile", "1.0", "Socratic",
            null, false, week13Start.AddDays(2)),  // First session in W13
        new SessionStarted_V1(studentId, "s2", "mobile", "1.0", "Socratic",
            null, false, week14Start.AddDays(1)),  // Second session in W14
    });

    await WaitForAsyncProjection<RetentionCohortView>();

    var w13 = await QueryView<RetentionCohortView>("2026-W13");
    var w14 = await QueryView<RetentionCohortView>("2026-W14");

    Assert.Equal(1, w13.TotalStudents);   // Assigned to W13
    Assert.Equal(0, w14?.TotalStudents ?? 0); // NOT assigned to W14
}
```

---

## Integration Test (all three views)

```csharp
[Fact]
public async Task AllAsyncProjections_ProcessSameEventStream()
{
    var studentId = Guid.CreateVersion7().ToString();
    var now = DateTimeOffset.UtcNow;

    await AppendEvents(studentId, GenerateFullSessionEvents(studentId, now));
    await WaitForAllAsyncProjections();

    var parent = await QueryView<ParentProgressView>(studentId);
    var methodology = await QueryView<MethodologyEffectivenessView>("Socratic:algebra");
    var cohort = await QueryView<RetentionCohortView>(GetIsoWeek(now));

    Assert.NotNull(parent);
    Assert.NotNull(methodology);
    Assert.NotNull(cohort);
    Assert.True(parent.TotalXp > 0);
    Assert.True(methodology.TotalAttempts > 0);
    Assert.True(cohort.TotalStudents > 0);
}
```

## Rollback Criteria
- If async projections fall behind (lag > 5 minutes): increase daemon polling interval
- If projection errors on unknown event type: add `default` handler that logs WARNING and skips
- If view schema migration fails: drop and rebuild from event stream (async projections are rebuildable)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=ReadModels"` -- 0 failures
- [ ] Async projection daemon catches up within 30 seconds on 10K events
- [ ] Views queryable via Marten `QuerySession.LoadAsync<T>(id)`
- [ ] PR reviewed by architect
