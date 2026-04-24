# MST-007: Decay Timer

**Priority:** P1 — required for spaced repetition and review scheduling
**Blocked by:** MST-003 (HLR decay engine), MST-006 (StudentActor mastery handler)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 4.1
**Research ref:** `docs/mastery-measurement-research.md` section 1.3.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The decay timer runs inside the `StudentActor` using Proto.Actor's `ReceiveTimeout` mechanism. Every 6 hours (configurable), it scans all mastered concepts in the student's overlay and computes current recall probability. Any concept where `p_recall < 0.70` and `MasteryProbability >= 0.70` emits a `MasteryDecayed` event. These events flow to the Outreach Context via NATS JetStream, triggering WhatsApp/push review reminders. The timer must be efficient — a student with 200 mastered concepts should complete the scan in microseconds.

## Subtasks

### MST-007.1: Decay Timer Setup in StudentActor

**Files to create:**
- `src/Cena.Actors/Learner/StudentActor.DecayTimer.cs`

**Acceptance:**
- [ ] `StudentActor` partial class configures `ReceiveTimeout` at actor startup
- [ ] Default interval: 6 hours, configurable via actor props or config
- [ ] On timeout, calls `ScanForDecay()` method
- [ ] Timer resets after each scan and after each `AttemptConcept` command
- [ ] Timer does NOT fire for inactive students (Proto.Actor passivates idle virtual actors)

### MST-007.2: Decay Scan Logic

**Files to create/modify:**
- `src/Cena.Actors/Learner/StudentActor.DecayTimer.cs` (add `ScanForDecay` method)

**Acceptance:**
- [ ] Iterates all entries in mastery overlay where `MasteryProbability >= 0.70`
- [ ] Computes `recall = Math.Pow(2, -(now - state.LastInteraction).TotalHours / state.HalfLifeHours)`
- [ ] If `recall < 0.70`, emits `MasteryDecayed(conceptId, recall)` event
- [ ] Publishes decay events to NATS JetStream subject `learner.mastery.decayed`
- [ ] Batches all decay events into a single NATS publish operation
- [ ] Scan of 200 concepts completes in < 1ms (pure arithmetic, no I/O per concept)

### MST-007.3: Decay Event Publishing and Configuration

**Files to create/modify:**
- `src/Cena.Domain/Learner/Events/MasteryDecayed.cs` (ensure fields complete)
- `src/Cena.Domain/Learner/Mastery/DecayTimerConfig.cs`

**Acceptance:**
- [ ] `DecayTimerConfig` record: `ScanIntervalHours` (default 6), `DecayThreshold` (default 0.70), `MinMasteryForScan` (default 0.70)
- [ ] `MasteryDecayed` event includes: `StudentId`, `ConceptId`, `RecallProbability`, `HalfLifeHours`, `HoursSinceLastInteraction`, `Timestamp`
- [ ] Config is loaded from `IOptions<DecayTimerConfig>` for DI
- [ ] Decay events include enough data for Outreach Context to compose review reminders without querying back

**Test:**
```csharp
[Fact]
public async Task DecayTimer_MasteredConceptForgotten_EmitsDecayEvent()
{
    var actor = CreateTestStudentActor("student-1");
    actor.SeedMasteryState("quadratics", new ConceptMasteryState
    {
        MasteryProbability = 0.95f,
        HalfLifeHours = 168f, // 1 week
        LastInteraction = DateTimeOffset.UtcNow.AddDays(-14), // 2 weeks ago
        AttemptCount = 20,
        CorrectCount = 18,
        CurrentStreak = 8
    });

    await actor.TriggerDecayScan(DateTimeOffset.UtcNow);

    var events = actor.GetUncommittedEvents();
    var decay = Assert.Single(events.OfType<MasteryDecayed>());
    Assert.Equal("quadratics", decay.ConceptId);
    Assert.InRange(decay.RecallProbability, 0.24f, 0.26f); // 2^(-336/168) = 0.25
}

[Fact]
public async Task DecayTimer_RecentlyPracticed_NoDecayEvent()
{
    var actor = CreateTestStudentActor("student-1");
    actor.SeedMasteryState("trigonometry", new ConceptMasteryState
    {
        MasteryProbability = 0.92f,
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-2), // 2 hours ago
        AttemptCount = 15,
        CorrectCount = 14
    });

    await actor.TriggerDecayScan(DateTimeOffset.UtcNow);

    var events = actor.GetUncommittedEvents();
    Assert.DoesNotContain(events, e => e is MasteryDecayed);
}

[Fact]
public async Task DecayTimer_LowMasteryNotScanned()
{
    var actor = CreateTestStudentActor("student-1");
    actor.SeedMasteryState("complex-analysis", new ConceptMasteryState
    {
        MasteryProbability = 0.40f, // not mastered
        HalfLifeHours = 48f,
        LastInteraction = DateTimeOffset.UtcNow.AddDays(-30), // long ago
        AttemptCount = 5,
        CorrectCount = 2
    });

    await actor.TriggerDecayScan(DateTimeOffset.UtcNow);

    // Low mastery concepts should NOT emit decay events (they were never mastered)
    var events = actor.GetUncommittedEvents();
    Assert.DoesNotContain(events, e => e is MasteryDecayed);
}

[Fact]
public async Task DecayTimer_MultipleDecayingConcepts_EmitsAll()
{
    var actor = CreateTestStudentActor("student-1");
    var twoWeeksAgo = DateTimeOffset.UtcNow.AddDays(-14);

    actor.SeedMasteryState("concept-A", new ConceptMasteryState
        { MasteryProbability = 0.92f, HalfLifeHours = 72f, LastInteraction = twoWeeksAgo });
    actor.SeedMasteryState("concept-B", new ConceptMasteryState
        { MasteryProbability = 0.88f, HalfLifeHours = 48f, LastInteraction = twoWeeksAgo });
    actor.SeedMasteryState("concept-C", new ConceptMasteryState
        { MasteryProbability = 0.95f, HalfLifeHours = 500f, LastInteraction = twoWeeksAgo });

    await actor.TriggerDecayScan(DateTimeOffset.UtcNow);

    var decayEvents = actor.GetUncommittedEvents().OfType<MasteryDecayed>().ToList();
    Assert.Equal(2, decayEvents.Count); // A and B decayed, C has long half-life
    Assert.Contains(decayEvents, e => e.ConceptId == "concept-A");
    Assert.Contains(decayEvents, e => e.ConceptId == "concept-B");
}
```
