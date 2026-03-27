# MST-006: StudentActor Mastery Handler

**Priority:** P0 — the integration point where mastery computation meets event sourcing
**Blocked by:** MST-005 (effective mastery), ACT-002 (StudentActor base), DATA-002 (Marten event store)
**Estimated effort:** 1-2 weeks (L)
**Contract:** `docs/mastery-engine-architecture.md` section 3.1, section 8
**Research ref:** `docs/mastery-measurement-research.md` section 4

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The `StudentActor` is a Proto.Actor virtual actor that owns all mastery state for a single student. When it receives an `AttemptConcept` command, it runs the full mastery pipeline (BKT → HLR → effective mastery → threshold check), persists a `ConceptAttempted` event via Marten, and publishes domain events to NATS JetStream. This is the hot path — every student interaction flows through here. The actor's state is event-sourced: on recovery it replays events to rebuild the mastery overlay.

## Subtasks

### MST-006.1: Command and Event Definitions

**Files to create:**
- `src/Cena.Domain/Learner/Commands/AttemptConcept.cs`
- `src/Cena.Domain/Learner/Events/ConceptAttempted.cs`
- `src/Cena.Domain/Learner/Events/ConceptMastered.cs`
- `src/Cena.Domain/Learner/Events/MasteryDecayed.cs`
- `src/Cena.Domain/Learner/Events/StagnationDetected.cs`

**Acceptance:**
- [ ] `AttemptConcept` command record: `StudentId`, `ConceptId`, `ItemId`, `IsCorrect`, `ResponseTimeMs`, `ErrorType?`, `BloomLevel`, `Timestamp`
- [ ] `ConceptAttempted` event record: all command fields + `NewMasteryProbability`, `NewHalfLifeHours`, `EffectiveMastery`, `PrereqSupport`
- [ ] `ConceptMastered` event record: `StudentId`, `ConceptId`, `EffectiveMastery`, `Timestamp`
- [ ] `MasteryDecayed` event record: `StudentId`, `ConceptId`, `RecallProbability`, `Timestamp`
- [ ] `StagnationDetected` event record: `StudentId`, `ConceptId`, `CompositeScore`, `DominantErrorType`, `Timestamp`
- [ ] All events are immutable records compatible with Marten event store serialization

### MST-006.2: StudentActor Mastery Handler (Partial Class)

**Files to create:**
- `src/Cena.Actors/Learner/StudentActor.Mastery.cs`

**Acceptance:**
- [ ] `StudentActor` partial class handles `AttemptConcept` command
- [ ] Retrieves current `ConceptMasteryState` from mastery overlay (or creates default for first encounter)
- [ ] Loads BKT parameters from `IBktParameterProvider` for the concept's KC
- [ ] Builds `HlrFeatures` from state + graph cache
- [ ] Calls `MasteryPipeline.ProcessAttempt()` for pure computation
- [ ] Updates the mastery overlay with new state
- [ ] Persists `ConceptAttempted` event to Marten stream
- [ ] If threshold crossed upward to 0.90 → persists and publishes `ConceptMastered`
- [ ] If threshold crossed downward below 0.70 → persists and publishes `MasteryDecayed`
- [ ] If 3+ same error type in `RecentErrors` → publishes `StagnationDetected`
- [ ] Publishes all events to NATS JetStream (`learner.mastery.*` subjects)

### MST-006.3: Event Replay and State Recovery

**Files to create/modify:**
- `src/Cena.Actors/Learner/StudentActor.Mastery.cs` (add `Apply` methods)

**Acceptance:**
- [ ] `Apply(ConceptAttempted e)` rebuilds mastery overlay entry from event data
- [ ] `Apply(ConceptMastered e)` — no-op (informational event, state already updated by ConceptAttempted)
- [ ] `Apply(MasteryDecayed e)` — no-op (informational event)
- [ ] After full event replay, mastery overlay matches what would be computed from scratch
- [ ] Actor recovery from 1000 events completes in < 100ms

**Test:**
```csharp
[Fact]
public async Task HandleAttemptConcept_CorrectAnswer_UpdatesMasteryAndEmitsEvent()
{
    // Arrange
    var actor = CreateTestStudentActor(studentId: "student-1");
    var command = new AttemptConcept(
        StudentId: "student-1",
        ConceptId: "quadratic-equations",
        ItemId: "item-42",
        IsCorrect: true,
        ResponseTimeMs: 12_000,
        ErrorType: null,
        BloomLevel: 3,
        Timestamp: DateTimeOffset.UtcNow);

    // Act
    await actor.Handle(command);

    // Assert
    var state = actor.GetMasteryState("quadratic-equations");
    Assert.True(state.MasteryProbability > 0.0f, "Mastery should increase from default after correct answer");
    Assert.Equal(1, state.AttemptCount);
    Assert.Equal(1, state.CorrectCount);
    Assert.True(state.HalfLifeHours > 0, "Half-life should be computed");

    var events = actor.GetUncommittedEvents();
    Assert.Contains(events, e => e is ConceptAttempted);
}

[Fact]
public async Task HandleAttemptConcept_CrossesMasteryThreshold_EmitsConceptMastered()
{
    // Arrange — student is at 0.89 mastery, one correct answer should push past 0.90
    var actor = CreateTestStudentActor(studentId: "student-1");
    actor.SeedMasteryState("derivatives", new ConceptMasteryState
    {
        MasteryProbability = 0.89f,
        HalfLifeHours = 200f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-1),
        AttemptCount = 15,
        CorrectCount = 13,
        CurrentStreak = 5,
        BloomLevel = 4
    });

    var command = new AttemptConcept(
        StudentId: "student-1",
        ConceptId: "derivatives",
        ItemId: "item-99",
        IsCorrect: true,
        ResponseTimeMs: 8_000,
        ErrorType: null,
        BloomLevel: 4,
        Timestamp: DateTimeOffset.UtcNow);

    // Act
    await actor.Handle(command);

    // Assert
    var events = actor.GetUncommittedEvents();
    Assert.Contains(events, e => e is ConceptMastered);
}

[Fact]
public async Task HandleAttemptConcept_RepeatedErrors_EmitsStagnation()
{
    var actor = CreateTestStudentActor(studentId: "student-1");
    actor.SeedMasteryState("integration", new ConceptMasteryState
    {
        MasteryProbability = 0.45f,
        HalfLifeHours = 48f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-2),
        AttemptCount = 10,
        CorrectCount = 4,
        RecentErrors = new[] { ErrorType.Procedural, ErrorType.Procedural },
        CurrentStreak = 0,
        BloomLevel = 2
    });

    var command = new AttemptConcept(
        StudentId: "student-1",
        ConceptId: "integration",
        ItemId: "item-77",
        IsCorrect: false,
        ResponseTimeMs: 25_000,
        ErrorType: ErrorType.Procedural, // 3rd procedural error
        BloomLevel: 2,
        Timestamp: DateTimeOffset.UtcNow);

    await actor.Handle(command);

    var events = actor.GetUncommittedEvents();
    Assert.Contains(events, e => e is StagnationDetected sd && sd.DominantErrorType == ErrorType.Procedural);
}

[Fact]
public async Task EventReplay_RebuildsCorrectState()
{
    var events = new object[]
    {
        new ConceptAttempted("student-1", "algebra-basics", "item-1", true, 10_000, null, 2,
            DateTimeOffset.Parse("2026-03-20T10:00:00Z"), 0.30f, 72f, 0.30f, 1.0f),
        new ConceptAttempted("student-1", "algebra-basics", "item-2", true, 8_000, null, 2,
            DateTimeOffset.Parse("2026-03-20T11:00:00Z"), 0.55f, 96f, 0.55f, 1.0f),
        new ConceptAttempted("student-1", "algebra-basics", "item-3", false, 15_000,
            ErrorType.Procedural, 2,
            DateTimeOffset.Parse("2026-03-20T12:00:00Z"), 0.42f, 80f, 0.42f, 1.0f),
    };

    var actor = CreateTestStudentActor("student-1");
    actor.ReplayEvents(events);

    var state = actor.GetMasteryState("algebra-basics");
    Assert.Equal(3, state.AttemptCount);
    Assert.Equal(2, state.CorrectCount);
    Assert.Equal(0, state.CurrentStreak); // last was incorrect
    Assert.InRange(state.MasteryProbability, 0.40f, 0.45f);
}
```
