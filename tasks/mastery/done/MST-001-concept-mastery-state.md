# MST-001: ConceptMasteryState Value Object

**Priority:** P0 — every other mastery task depends on this
**Blocked by:** —
**Estimated effort:** 1-2 days (S)
**Contract:** `docs/mastery-engine-architecture.md` section 2.1
**Architecture ref:** `docs/mastery-measurement-research.md` section 4.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

The `ConceptMasteryState` is the per-concept knowledge record stored inside the `StudentActor`'s event-sourced state. It holds BKT probability, HLR half-life, Bloom's level, error history, method tracking, and spaced repetition parameters. Every mastery computation in the system reads and writes this object.

## Subtasks

### MST-001.1: Core Record Definition

**Files to create:**
- `src/Cena.Domain/Learner/ConceptMasteryState.cs`
- `src/Cena.Domain/Learner/MasteryQuality.cs`
- `src/Cena.Domain/Learner/ErrorType.cs`
- `src/Cena.Domain/Learner/MethodAttempt.cs`
- `src/Cena.Domain/Learner/MasteryThreshold.cs`

**Acceptance:**
- [ ] `ConceptMasteryState` is an immutable C# `record` (not a class) for value semantics
- [ ] All properties from architecture section 2.1 present: `MasteryProbability`, `HalfLifeHours`, `LastInteraction`, `FirstEncounter`, `AttemptCount`, `CorrectCount`, `CurrentStreak`, `BloomLevel` (0-6), `SelfConfidence` (0-1), `RecentErrors` (max 10), `QualityQuadrant`, `Stability`, `Difficulty`, `MethodHistory`
- [ ] `MasteryQuality` enum: `Mastered`, `Effortful`, `Careless`, `Struggling`
- [ ] `ErrorType` enum: `Procedural`, `Conceptual`, `Careless`, `Systematic`, `Transfer`
- [ ] `MethodAttempt` record: `(string MethodologyId, int SessionCount, string Outcome)`
- [ ] `MasteryThreshold` static class with constants: `NotStarted=0.10`, `Introduced=0.40`, `Developing=0.70`, `Proficient=0.90`, `DecayWarning=0.70`, `PrerequisiteGate=0.60`

### MST-001.2: Computed Properties

**Acceptance:**
- [ ] `RecallProbability(DateTimeOffset now)` method: `Math.Pow(2, -(now - LastInteraction).TotalHours / HalfLifeHours)`
- [ ] Guard: if `HalfLifeHours <= 0`, return `0.0` (never interacted)
- [ ] Guard: if `LastInteraction == default`, return `0.0`
- [ ] `RollingAccuracy` computed property: `CorrectCount / (float)AttemptCount` (guard: AttemptCount == 0 → 0.0)
- [ ] `MasteryLevel` computed property: returns threshold enum based on `MasteryProbability` ranges
- [ ] `IsDecaying(DateTimeOffset now)` method: `MasteryProbability >= MasteryThreshold.Proficient && RecallProbability(now) < MasteryThreshold.DecayWarning`

### MST-001.3: With-Methods for Immutable Updates

**Acceptance:**
- [ ] `WithBktUpdate(float newProbability)` returns new record with updated `MasteryProbability`
- [ ] `WithAttempt(bool correct, DateTimeOffset now)` returns new record with incremented counters, reset/extended streak, updated `LastInteraction`, capped `RecentErrors` at 10
- [ ] `WithHalfLifeUpdate(float newHalfLife)` returns new record
- [ ] `WithBloomLevel(int level)` returns new record, validates 0-6 range
- [ ] `WithMethodAttempt(MethodAttempt attempt)` appends to `MethodHistory`
- [ ] All With-methods return NEW records (immutability enforced by `record` keyword)

**Test:**
```csharp
[Fact]
public void RecallProbability_AtHalfLife_Returns50Percent()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.95f,
        HalfLifeHours = 168f, // 1 week
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-168)
    };

    var recall = state.RecallProbability(DateTimeOffset.UtcNow);
    Assert.InRange(recall, 0.49f, 0.51f); // ≈ 0.50
}

[Fact]
public void RecallProbability_AtDoubleHalfLife_Returns25Percent()
{
    var state = new ConceptMasteryState
    {
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.UtcNow.AddHours(-336) // 2 weeks
    };

    var recall = state.RecallProbability(DateTimeOffset.UtcNow);
    Assert.InRange(recall, 0.24f, 0.26f); // ≈ 0.25
}

[Fact]
public void WithAttempt_Correct_ExtendsStreak()
{
    var state = new ConceptMasteryState { CurrentStreak = 3, AttemptCount = 5, CorrectCount = 3 };
    var updated = state.WithAttempt(correct: true, DateTimeOffset.UtcNow);

    Assert.Equal(4, updated.CurrentStreak);
    Assert.Equal(6, updated.AttemptCount);
    Assert.Equal(4, updated.CorrectCount);
}

[Fact]
public void WithAttempt_Incorrect_ResetsStreak()
{
    var state = new ConceptMasteryState { CurrentStreak = 7, AttemptCount = 10, CorrectCount = 7 };
    var updated = state.WithAttempt(correct: false, DateTimeOffset.UtcNow);

    Assert.Equal(0, updated.CurrentStreak);
    Assert.Equal(11, updated.AttemptCount);
    Assert.Equal(7, updated.CorrectCount);
}

[Fact]
public void IsDecaying_MasteredButForgotten_ReturnsTrue()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.95f,
        HalfLifeHours = 48f,
        LastInteraction = DateTimeOffset.UtcNow.AddDays(-7)
    };

    Assert.True(state.IsDecaying(DateTimeOffset.UtcNow));
}

[Fact]
public void MasteryLevel_CorrectThresholds()
{
    Assert.Equal(MasteryLevel.NotStarted, new ConceptMasteryState { MasteryProbability = 0.05f }.MasteryLevel);
    Assert.Equal(MasteryLevel.Introduced, new ConceptMasteryState { MasteryProbability = 0.25f }.MasteryLevel);
    Assert.Equal(MasteryLevel.Developing, new ConceptMasteryState { MasteryProbability = 0.55f }.MasteryLevel);
    Assert.Equal(MasteryLevel.Proficient, new ConceptMasteryState { MasteryProbability = 0.80f }.MasteryLevel);
    Assert.Equal(MasteryLevel.Mastered, new ConceptMasteryState { MasteryProbability = 0.95f }.MasteryLevel);
}

[Fact]
public void Immutability_WithMethods_ReturnNewInstances()
{
    var original = new ConceptMasteryState { MasteryProbability = 0.5f };
    var updated = original.WithBktUpdate(0.8f);

    Assert.Equal(0.5f, original.MasteryProbability); // unchanged
    Assert.Equal(0.8f, updated.MasteryProbability);   // new value
    Assert.NotSame(original, updated);
}
```

### MST-001.4: Serialization Compatibility

**Acceptance:**
- [ ] `ConceptMasteryState` serializes to/from JSON (System.Text.Json) correctly
- [ ] Marten can persist it as part of `StudentState` document snapshot
- [ ] Default values are sensible: `MasteryProbability=0`, `HalfLifeHours=0`, `BloomLevel=0`, `AttemptCount=0`, `CurrentStreak=0`
- [ ] Empty `RecentErrors` and `MethodHistory` arrays (not null)

**Test:**
```csharp
[Fact]
public void Serialization_RoundTrip_PreservesAllFields()
{
    var state = new ConceptMasteryState
    {
        MasteryProbability = 0.85f,
        HalfLifeHours = 168f,
        LastInteraction = DateTimeOffset.Parse("2026-03-20T10:00:00Z"),
        BloomLevel = 4,
        SelfConfidence = 0.7f,
        RecentErrors = new[] { ErrorType.Procedural, ErrorType.Careless },
        QualityQuadrant = MasteryQuality.Mastered,
        CurrentStreak = 5,
        AttemptCount = 20,
        CorrectCount = 17
    };

    var json = JsonSerializer.Serialize(state);
    var deserialized = JsonSerializer.Deserialize<ConceptMasteryState>(json);

    Assert.Equal(state, deserialized); // record equality
}
```
