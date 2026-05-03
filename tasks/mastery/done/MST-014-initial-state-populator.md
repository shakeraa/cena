# MST-014: Initial State Populator

**Priority:** P1 — bridges diagnostic results to mastery engine
**Blocked by:** MST-013 (onboarding diagnostic), MST-001 (ConceptMasteryState)
**Estimated effort:** 1-2 days (S)
**Contract:** `docs/mastery-engine-architecture.md` section 9.2
**Research ref:** `docs/mastery-measurement-research.md` section 5.2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

After the onboarding diagnostic completes, the `InitialStatePopulator` converts the diagnostic result into initial `ConceptMasteryState` entries on the `StudentActor`. Mastered concepts get optimistic defaults (mastery=0.85, half-life=168h, Bloom=3), while gap concepts remain at zero. This provides a warm start for the mastery engine so students immediately see a populated knowledge graph and receive appropriately calibrated items rather than starting from a blank slate.

## Subtasks

### MST-014.1: Diagnostic Result to Mastery State Conversion

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/InitialStatePopulator.cs`

**Acceptance:**
- [ ] `InitialStatePopulator.Populate(DiagnosticResult result, DateTimeOffset now) → IReadOnlyDictionary<string, ConceptMasteryState>`
- [ ] For each concept in `result.MasteredConcepts`:
  - `MasteryProbability = 0.85f` (confident but not certain)
  - `HalfLifeHours = 168f` (1 week default)
  - `BloomLevel = 3` (assumed Apply level from diagnostic)
  - `AttemptCount = 1`
  - `CorrectCount = 1`
  - `LastInteraction = now`
  - `FirstEncounter = now`
  - `CurrentStreak = 1`
- [ ] For each concept in `result.GapConcepts`: no entry (default ConceptMasteryState with mastery=0.0)
- [ ] Method is `static`, pure function

### MST-014.2: StudentActor Initial State Application

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/InitialStatePopulator.cs` (add `ApplyToActor` method)
- `src/Cena.Domain/Learner/Events/DiagnosticCompleted.cs`

**Acceptance:**
- [ ] `DiagnosticCompleted` event record: `StudentId`, `MasteredConceptIds` (IReadOnlyList<string>), `GapConceptIds` (IReadOnlyList<string>), `QuestionsAsked` (int), `Confidence` (float), `Timestamp`
- [ ] `InitialStatePopulator.CreateEvent(string studentId, DiagnosticResult result, DateTimeOffset now) → DiagnosticCompleted`
- [ ] The `StudentActor` handles `DiagnosticCompleted` by populating its mastery overlay with the initial states
- [ ] `Apply(DiagnosticCompleted e)` on event replay rebuilds the same initial state
- [ ] Diagnostic can only run once per student (guard: if overlay already has entries, reject)

### MST-014.3: Confidence-Adjusted Initial Values

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/InitialStatePopulator.cs` (add confidence scaling)

**Acceptance:**
- [ ] When diagnostic confidence is high (>= 0.80), use standard defaults (mastery=0.85)
- [ ] When diagnostic confidence is lower (< 0.80), scale down initial mastery: `mastery = 0.85 * confidence`
- [ ] Half-life also scales: `halfLife = 168 * confidence` (less confident → shorter memory assumed)
- [ ] Bloom level stays at 3 regardless of confidence (assessment was at Apply level)

**Test:**
```csharp
[Fact]
public void Populate_MasteredConcepts_GetOptimisticDefaults()
{
    var result = new DiagnosticResult(
        MasteredConcepts: new HashSet<string> { "algebra", "geometry" }.ToImmutableHashSet(),
        GapConcepts: new HashSet<string> { "calculus" }.ToImmutableHashSet(),
        Confidence: 0.90f,
        QuestionsAsked: 12);

    var now = DateTimeOffset.Parse("2026-03-26T10:00:00Z");
    var states = InitialStatePopulator.Populate(result, now);

    Assert.Equal(2, states.Count); // only mastered concepts get entries
    Assert.True(states.ContainsKey("algebra"));
    Assert.True(states.ContainsKey("geometry"));
    Assert.False(states.ContainsKey("calculus")); // gap — no entry

    var algebraState = states["algebra"];
    Assert.Equal(0.85f, algebraState.MasteryProbability);
    Assert.Equal(168f, algebraState.HalfLifeHours);
    Assert.Equal(3, algebraState.BloomLevel);
    Assert.Equal(1, algebraState.AttemptCount);
    Assert.Equal(1, algebraState.CorrectCount);
    Assert.Equal(now, algebraState.LastInteraction);
}

[Fact]
public void Populate_LowConfidence_ScalesDownMastery()
{
    var result = new DiagnosticResult(
        MasteredConcepts: new HashSet<string> { "algebra" }.ToImmutableHashSet(),
        GapConcepts: ImmutableHashSet<string>.Empty,
        Confidence: 0.60f, // low confidence
        QuestionsAsked: 10);

    var states = InitialStatePopulator.Populate(result, DateTimeOffset.UtcNow);

    var state = states["algebra"];
    // mastery = 0.85 * 0.60 = 0.51
    Assert.InRange(state.MasteryProbability, 0.50f, 0.52f);
    // half-life = 168 * 0.60 = 100.8
    Assert.InRange(state.HalfLifeHours, 100f, 102f);
}

[Fact]
public void Populate_HighConfidence_UsesFullDefaults()
{
    var result = new DiagnosticResult(
        MasteredConcepts: new HashSet<string> { "algebra" }.ToImmutableHashSet(),
        GapConcepts: ImmutableHashSet<string>.Empty,
        Confidence: 0.95f,
        QuestionsAsked: 15);

    var states = InitialStatePopulator.Populate(result, DateTimeOffset.UtcNow);

    Assert.Equal(0.85f, states["algebra"].MasteryProbability);
    Assert.Equal(168f, states["algebra"].HalfLifeHours);
}

[Fact]
public void CreateEvent_ProducesCorrectEvent()
{
    var result = new DiagnosticResult(
        MasteredConcepts: new HashSet<string> { "A", "B", "C" }.ToImmutableHashSet(),
        GapConcepts: new HashSet<string> { "D" }.ToImmutableHashSet(),
        Confidence: 0.85f,
        QuestionsAsked: 12);

    var evt = InitialStatePopulator.CreateEvent("student-1", result, DateTimeOffset.UtcNow);

    Assert.Equal("student-1", evt.StudentId);
    Assert.Equal(3, evt.MasteredConceptIds.Count);
    Assert.Single(evt.GapConceptIds);
    Assert.Equal(12, evt.QuestionsAsked);
    Assert.Equal(0.85f, evt.Confidence);
}
```
