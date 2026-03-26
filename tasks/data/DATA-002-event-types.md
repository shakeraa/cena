# DATA-002: Domain Event C# Records (All 20+ Types)

**Priority:** P0 — every actor and projection depends on these types
**Blocked by:** Nothing (pure type definitions)
**Estimated effort:** 2 days
**Contract:** `contracts/data/marten-event-store.cs` (lines 107-305)

---

## Context
Domain events are the single source of truth in Cena's event-sourced architecture. Every event is an immutable C# record with an explicit `Timestamp` field for deterministic replay. Events are append-only and versioned (`_V1` suffix). There are four bounded contexts: Learner, Pedagogy, Engagement, and Outreach, totaling 20 event types.

## Subtasks

### DATA-002.1: Learner Context Events (7 types)
**Files:**
- `src/Cena.Domain/Events/Learner/ConceptAttempted_V1.cs`
- `src/Cena.Domain/Events/Learner/ConceptMastered_V1.cs`
- `src/Cena.Domain/Events/Learner/MasteryDecayed_V1.cs`
- `src/Cena.Domain/Events/Learner/MethodologySwitched_V1.cs`
- `src/Cena.Domain/Events/Learner/StagnationDetected_V1.cs`
- `src/Cena.Domain/Events/Learner/AnnotationAdded_V1.cs`
- `src/Cena.Domain/Events/Learner/CognitiveLoadCooldownComplete_V1.cs`

**Acceptance:**
- [ ] `ConceptAttempted_V1`: 18 fields — `StudentId`, `ConceptId`, `SessionId`, `IsCorrect`, `ResponseTimeMs`, `QuestionId`, `QuestionType` ("multiple_choice"|"numeric"|"expression"|"free_text"), `MethodologyActive`, `ErrorType` ("procedural"|"conceptual"|"motivational"|"none"), `PriorMastery`, `PosteriorMastery`, `HintCountUsed`, `WasSkipped`, `AnswerHash`, `BackspaceCount`, `AnswerChangeCount`, `WasOffline`, `Timestamp` (DateTimeOffset)
- [ ] `ConceptMastered_V1`: `StudentId`, `ConceptId`, `SessionId`, `MasteryLevel`, `TotalAttempts`, `TotalSessions`, `MethodologyAtMastery`, `InitialHalfLifeHours`, `Timestamp`
- [ ] `MasteryDecayed_V1`: `StudentId`, `ConceptId`, `PredictedRecall`, `HalfLifeHours`, `HoursSinceLastReview`
- [ ] `MethodologySwitched_V1`: `StudentId`, `ConceptId`, `PreviousMethodology`, `NewMethodology`, `Trigger` ("stagnation_detected"|"student_requested"|"mcm_recommendation"), `StagnationScore`, `DominantErrorType`, `McmConfidence`
- [ ] `StagnationDetected_V1`: `StudentId`, `ConceptId`, `CompositeScore`, `AccuracyPlateau`, `ResponseTimeDrift`, `SessionAbandonment`, `ErrorRepetition`, `AnnotationSentiment`, `ConsecutiveStagnantSessions`
- [ ] `AnnotationAdded_V1`: `StudentId`, `ConceptId`, `AnnotationId`, `ContentHash` (NOT plaintext), `SentimentScore`, `AnnotationType` ("note"|"question"|"insight"|"confusion")
- [ ] `CognitiveLoadCooldownComplete_V1`: `StudentId`, `SessionId`, `FatigueScoreAtEnd`, `MinutesCooldown`, `QuestionsCompleted`
- [ ] ALL records are `public record` (immutable by design)
- [ ] ALL records have explicit `DateTimeOffset Timestamp` where specified in contract — NO `DateTimeOffset.UtcNow` in event constructors

**Test:**
```csharp
[Fact]
public void ConceptAttempted_V1_IsImmutableRecord()
{
    var e = new ConceptAttempted_V1(
        StudentId: "s1", ConceptId: "c1", SessionId: "sess1",
        IsCorrect: true, ResponseTimeMs: 1500, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.5, PosteriorMastery: 0.65,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 2, AnswerChangeCount: 1, WasOffline: false,
        Timestamp: new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

    Assert.Equal("s1", e.StudentId);
    Assert.Equal(0.65, e.PosteriorMastery);

    // Records support structural equality
    var copy = e with { IsCorrect = false };
    Assert.NotEqual(e, copy);
    Assert.Equal(e.StudentId, copy.StudentId);
}

[Fact]
public void ConceptMastered_V1_HasTimestamp()
{
    var ts = DateTimeOffset.UtcNow;
    var e = new ConceptMastered_V1("s1", "c1", "sess1", 0.9, 15, 4, "feynman", 48.0, ts);
    Assert.Equal(ts, e.Timestamp);
}

[Fact]
public void AnnotationAdded_V1_StoresHashNotPlaintext()
{
    var e = new AnnotationAdded_V1("s1", "c1", "ann-1", "sha256:abc123", 0.3, "confusion");
    Assert.StartsWith("sha256:", e.ContentHash);
}
```

---

### DATA-002.2: Pedagogy + Engagement Context Events (10 types)
**Files:**
- `src/Cena.Domain/Events/Pedagogy/SessionStarted_V1.cs`
- `src/Cena.Domain/Events/Pedagogy/SessionEnded_V1.cs`
- `src/Cena.Domain/Events/Pedagogy/ExercisePresented_V1.cs`
- `src/Cena.Domain/Events/Pedagogy/HintRequested_V1.cs`
- `src/Cena.Domain/Events/Pedagogy/QuestionSkipped_V1.cs`
- `src/Cena.Domain/Events/Engagement/XpAwarded_V1.cs`
- `src/Cena.Domain/Events/Engagement/StreakUpdated_V1.cs`
- `src/Cena.Domain/Events/Engagement/BadgeEarned_V1.cs`
- `src/Cena.Domain/Events/Engagement/StreakExpiring_V1.cs`
- `src/Cena.Domain/Events/Engagement/ReviewDue_V1.cs`

**Acceptance:**
- [ ] `SessionStarted_V1`: `StudentId`, `SessionId`, `DeviceType`, `AppVersion`, `Methodology`, `ExperimentCohort` (nullable), `IsOffline`, `ClientTimestamp`
- [ ] `SessionEnded_V1`: `StudentId`, `SessionId`, `EndReason` ("completed"|"fatigue"|"abandoned"|"timeout"|"app_backgrounded"), `DurationMinutes`, `QuestionsAttempted`, `QuestionsCorrect`, `AvgResponseTimeMs`, `FatigueScoreAtEnd`
- [ ] `ExercisePresented_V1`: `StudentId`, `SessionId`, `ConceptId`, `QuestionId`, `QuestionType`, `DifficultyLevel` ("recall"|"comprehension"|"application"|"analysis"), `Methodology`
- [ ] `HintRequested_V1`: `StudentId`, `SessionId`, `ConceptId`, `QuestionId`, `HintLevel` (1=nudge, 2=scaffolded, 3=near-answer)
- [ ] `QuestionSkipped_V1`: `StudentId`, `SessionId`, `ConceptId`, `QuestionId`, `TimeSpentBeforeSkipMs`
- [ ] `XpAwarded_V1`: `StudentId`, `XpAmount`, `Source` ("exercise_correct"|"mastery"|"streak_bonus"|"daily_goal"), `TotalXp`, `DifficultyLevel`, `DifficultyMultiplier` (1x recall, 2x comprehension, 3x application, 4x analysis)
- [ ] `StreakUpdated_V1`: `StudentId`, `CurrentStreak`, `LongestStreak`, `LastActivityDate`
- [ ] `BadgeEarned_V1`: `StudentId`, `BadgeId`, `BadgeName`, `BadgeCategory` ("mastery"|"streak"|"exploration"|"methodology")
- [ ] `StreakExpiring_V1`: `StudentId`, `CurrentStreak`, `ExpiresAt`, `HoursUntilExpiry`
- [ ] `ReviewDue_V1`: `StudentId`, `ConceptId`, `PredictedRecall`, `HalfLifeHours`, `Priority` ("urgent"|"standard"|"low")

**Test:**
```csharp
[Fact]
public void XpAwarded_V1_DifficultyMultiplierRange()
{
    var recall = new XpAwarded_V1("s1", 10, "exercise_correct", 10, "recall", 1);
    var analysis = new XpAwarded_V1("s1", 40, "exercise_correct", 50, "analysis", 4);
    Assert.Equal(1, recall.DifficultyMultiplier);
    Assert.Equal(4, analysis.DifficultyMultiplier);
}

[Fact]
public void SessionEnded_V1_EndReasonValues()
{
    var valid = new[] { "completed", "fatigue", "abandoned", "timeout", "app_backgrounded" };
    foreach (var reason in valid)
    {
        var e = new SessionEnded_V1("s1", "sess1", reason, 15, 10, 8, 3200.0, 0.4);
        Assert.Equal(reason, e.EndReason);
    }
}

[Fact]
public void HintRequested_V1_LevelRange()
{
    var e = new HintRequested_V1("s1", "sess1", "c1", "q1", 3);
    Assert.InRange(e.HintLevel, 1, 3);
}
```

---

### DATA-002.3: Outreach Context Events (3 types)
**Files:**
- `src/Cena.Domain/Events/Outreach/OutreachMessageSent_V1.cs`
- `src/Cena.Domain/Events/Outreach/OutreachMessageDelivered_V1.cs`
- `src/Cena.Domain/Events/Outreach/OutreachResponseReceived_V1.cs`

**Acceptance:**
- [ ] `OutreachMessageSent_V1`: `StudentId`, `MessageId`, `Channel` ("whatsapp"|"telegram"|"push"|"voice"), `TriggerType` ("StreakExpiring"|"ReviewDue"|"StagnationDetected"), `ContentHash`
- [ ] `OutreachMessageDelivered_V1`: `StudentId`, `MessageId`, `Channel`, `DeliveredAt` (DateTimeOffset)
- [ ] `OutreachResponseReceived_V1`: `StudentId`, `MessageId`, `ResponseType` ("quiz_answer"|"dismissed"|"clicked"|"replied"), `ResponseContentHash` (nullable)
- [ ] All outreach events use `ContentHash` / `ResponseContentHash` — never raw content (GDPR)

**Test:**
```csharp
[Fact]
public void OutreachMessageSent_V1_ChannelValues()
{
    foreach (var ch in new[] { "whatsapp", "telegram", "push", "voice" })
    {
        var e = new OutreachMessageSent_V1("s1", "msg-1", ch, "StreakExpiring", "hash");
        Assert.Equal(ch, e.Channel);
    }
}

[Fact]
public void OutreachResponseReceived_V1_NullableContent()
{
    var dismissed = new OutreachResponseReceived_V1("s1", "msg-1", "dismissed", null);
    Assert.Null(dismissed.ResponseContentHash);

    var replied = new OutreachResponseReceived_V1("s1", "msg-1", "replied", "content-hash");
    Assert.NotNull(replied.ResponseContentHash);
}
```

**Edge cases:**
- Future event types (V2) must not break deserialization of V1
- Events with null optional fields serialize and deserialize correctly
- Empty string fields vs null fields — follow contract: only `ExperimentCohort` and `ResponseContentHash` are nullable

---

## Integration Test

```csharp
[Fact]
public async Task AllEventTypes_PersistAndRetrieve()
{
    var allEvents = new object[]
    {
        new ConceptAttempted_V1("s1","c1","sess1",true,1000,"q1","numeric","socratic","none",0.5,0.6,0,false,"h",0,0,false,DateTimeOffset.UtcNow),
        new ConceptMastered_V1("s1","c1","sess1",0.9,10,3,"socratic",48.0,DateTimeOffset.UtcNow),
        new MasteryDecayed_V1("s1","c1",0.6,24.0,48.0),
        new MethodologySwitched_V1("s1","c1","socratic","feynman","stagnation_detected",0.8,"conceptual",0.85),
        new StagnationDetected_V1("s1","c1",0.75,0.8,0.6,0.3,0.7,0.4,3),
        new AnnotationAdded_V1("s1","c1","ann-1","sha256:abc",0.3,"confusion"),
        new CognitiveLoadCooldownComplete_V1("s1","sess1",0.7,15,20),
        new SessionStarted_V1("s1","sess1","mobile","1.0","socratic",null,false,DateTimeOffset.UtcNow),
        new SessionEnded_V1("s1","sess1","completed",20,15,12,2800.0,0.4),
        new ExercisePresented_V1("s1","sess1","c1","q1","numeric","comprehension","socratic"),
        new HintRequested_V1("s1","sess1","c1","q1",2),
        new QuestionSkipped_V1("s1","sess1","c1","q1",5000),
        new XpAwarded_V1("s1",25,"exercise_correct",125,"comprehension",2),
        new StreakUpdated_V1("s1",7,14,DateTimeOffset.UtcNow),
        new BadgeEarned_V1("s1","badge-1","Math Whiz","mastery"),
        new StreakExpiring_V1("s1",7,DateTimeOffset.UtcNow.AddHours(6),6),
        new ReviewDue_V1("s1","c1",0.4,12.0,"urgent"),
        new OutreachMessageSent_V1("s1","msg-1","push","ReviewDue","hash"),
        new OutreachMessageDelivered_V1("s1","msg-1","push",DateTimeOffset.UtcNow),
        new OutreachResponseReceived_V1("s1","msg-1","clicked",null),
    };

    await using var session = _store.LightweightSession();
    session.Events.Append("all-events-test", allEvents);
    await session.SaveChangesAsync();

    var stream = await session.Events.FetchStreamAsync("all-events-test");
    Assert.Equal(20, stream.Count);

    // Verify each type deserialized correctly
    Assert.IsType<ConceptAttempted_V1>(stream[0].Data);
    Assert.IsType<OutreachResponseReceived_V1>(stream[^1].Data);
}
```

## Rollback Criteria
- If C# records cause serialization issues: fall back to sealed classes with init-only properties
- If 20 separate files are too granular: consolidate by bounded context (4 files)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes with all 20 event types
- [ ] `dotnet test --filter "Category=EventTypes"` -> 0 failures
- [ ] All event fields match the contract exactly (field names, types, order)
- [ ] No `DateTimeOffset.UtcNow` in any event record constructor
- [ ] GDPR: `AnnotationAdded_V1.ContentHash` and outreach `ContentHash` store hashes, never plaintext
- [ ] PR reviewed by architect
