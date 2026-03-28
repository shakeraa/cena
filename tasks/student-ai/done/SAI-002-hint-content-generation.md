# SAI-002: Hint Content Generation + BKT Credit Weighting

**Priority:** P0 — highest-impact zero-AI-cost feature
**Blocked by:** None (all infrastructure exists)
**Estimated effort:** 2-3 days
**Stack:** .NET 9, Proto.Actor, Marten event sourcing

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code.

## Context

The hint infrastructure is fully built:
- `LearningSessionActor` handles `RequestHintMessage(conceptId, questionId, hintLevel)` and emits `HintRequested_V1`
- `HintDelivered` SignalR event is sent back with `hintText` (currently placeholder) and `hasMoreHints`
- `ScaffoldingService` determines max hints: Full=3, Partial=2, HintsOnly=1, None=0
- `BusConceptAttempt.HintCountUsed` is already transmitted from client
- `IConceptGraphCache.GetPrerequisites(conceptId)` returns prerequisite edges with strength weights

What's missing:
1. **Hint content** — the actual text for each hint level (nudge, scaffold, reveal)
2. **BKT credit adjustment** — hint usage doesn't reduce mastery credit
3. **ScaffoldingService integration** — hints should respect the max-hints-per-level gate

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | `RequestHintMessage` handler, `HintDelivered` response, fatigue computation |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Full/Partial/HintsOnly/None levels, max hints per level |
| `src/actors/Cena.Actors/Mastery/IConceptGraphCache.cs` | `GetPrerequisites(conceptId)` — prerequisite edges with `Strength` weights |
| `src/actors/Cena.Actors/Mastery/BktTracer.cs` | `Update(float currentP_L, bool isCorrect, BktParameters p)` — sub-microsecond, zero alloc |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | `RecentErrors[]`, `QualityQuadrant`, `BloomLevel`, `MasteryProbability` |
| `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | `PublishedQuestion` — question metadata for hint generation |
| `src/actors/Cena.Actors/Students/StudentActor.cs` | Where BKT update happens after attempt — modify for hint credit weighting |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | `BusConceptAttempt.HintCountUsed` — already transmitted from client |
| `contracts/frontend/signalr-messages.ts` | `HintDelivered` payload contract (hintLevel, hintText, hasMoreHints) |

## Subtasks

### SAI-002.1: Implement IHintContentGenerator Service

**Files to create:**
- `src/actors/Cena.Actors/Hints/IHintContentGenerator.cs`
- `src/actors/Cena.Actors/Hints/HintContentGenerator.cs`

**Implementation:**

A pure domain service (no LLM calls, no I/O) that generates hint text from existing data:

```csharp
public interface IHintContentGenerator
{
    HintContent Generate(HintContext context);
}

public sealed record HintContext(
    int HintLevel,                          // 1, 2, or 3
    string ConceptId,
    string QuestionStem,
    string? Explanation,                    // L1 from SAI-001 (nullable — may not exist yet)
    IReadOnlyList<MasteryPrerequisiteEdge> Prerequisites,
    ConceptMasteryState? ConceptState,      // nullable for first-encounter
    int BloomLevel);

public sealed record HintContent(
    int HintLevel,
    string Text,                            // Markdown with LaTeX support
    bool HasMoreHints);
```

**Hint generation logic (NO LLM — deterministic from data):**

**Level 1 — Nudge** (concept-graph pointer):
- Get prerequisites sorted by `Strength` descending
- Pick the prerequisite with lowest student mastery (or first if no mastery data)
- Template: `"Consider how **{prerequisiteName}** applies here. What did you learn about {prerequisiteName}?"`
- If no prerequisites: `"Re-read the question carefully. Focus on what is being asked, not the numbers."`

**Level 2 — Scaffold** (error-pattern-aware):
- Check `ConceptState.RecentErrors[]` for dominant `ErrorType`
- Procedural: `"Your approach is right. Check your calculation step by step — where does the arithmetic change?"`
- Conceptual: `"This concept is different from what it looks like. Think about the definition of {conceptName}."`
- Careless (fast+wrong): `"Slow down. You know this — re-read each option before choosing."`
- Default: `"Try eliminating answers you know are wrong. What can you rule out?"`

**Level 3 — Reveal** (full solution):
- If `Explanation` exists (from SAI-001): return it directly
- If no explanation: `"The correct approach: apply {conceptName} principles. [Full explanation unavailable — will be added by system.]"`

**Acceptance:**
- [ ] `IHintContentGenerator` registered in DI
- [ ] Level 1 returns prerequisite-aware nudge (verified with concept graph data)
- [ ] Level 2 returns ErrorType-specific scaffold
- [ ] Level 3 returns L1 explanation when available, graceful fallback when not
- [ ] All hint text supports Markdown + LaTeX (`$...$` delimiters)
- [ ] No LLM calls, no HTTP calls, no I/O — pure domain logic
- [ ] Max hint text length: 500 chars for Level 1-2, 2000 chars for Level 3

---

### SAI-002.2: Wire HintContentGenerator into LearningSessionActor

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Implementation:**

In the `RequestHintMessage` handler (currently emits `HintRequested_V1` and returns placeholder):

1. Inject `IHintContentGenerator` and `IConceptGraphCache` via constructor
2. Check `ScaffoldingService` max hints for current scaffolding level — reject if exceeded
3. Build `HintContext` from current question + student state
4. Call `IHintContentGenerator.Generate(context)`
5. Return `HintResponse` with generated `hintText` (replacing placeholder)
6. Delegate `HintRequested_V1` event to parent as before

**Gate logic:**
```csharp
var scaffolding = _scaffoldingService.Determine(effectiveMastery, psi);
if (hintLevel > scaffolding.MaxHints)
    return new HintResponse(hintLevel, delivered: false, reason: "max_hints_exceeded");
```

**Acceptance:**
- [ ] Hint text populated from `IHintContentGenerator` (no more placeholder)
- [ ] ScaffoldingService gate enforced: Full=3 max, Partial=2, HintsOnly=1, None=0
- [ ] Requesting hint beyond max returns `delivered: false` with reason
- [ ] `HintDelivered` SignalR event carries generated text + correct `hasMoreHints`
- [ ] `HintRequested_V1` event still emitted for analytics (unchanged)

---

### SAI-002.3: BKT Credit Weighting for Hint Usage

**Files to modify:**
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` (or whichever partial handles `AttemptConcept`)
- `src/actors/Cena.Actors/Mastery/BktTracer.cs`

**Implementation:**

After an attempt is evaluated, the BKT update currently uses `BktParameters.Default.P_T = 0.20f`. Modify to scale P_T based on hints used:

```csharp
public static BktParameters AdjustForHints(BktParameters baseParams, int hintsUsed)
{
    float creditMultiplier = hintsUsed switch
    {
        0 => 1.0f,
        1 => 0.7f,
        2 => 0.4f,
        _ => 0.1f  // 3+ hints
    };
    return baseParams with { P_T = baseParams.P_T * creditMultiplier };
}
```

This reduces the learning transition probability — meaning a correct answer with 3 hints barely moves the mastery needle.

Wire this into `StudentActor.HandleAttemptConcept()`:
1. Read `HintCountUsed` from the attempt payload (already in `BusConceptAttempt`)
2. Call `BktParameters.AdjustForHints(BktParameters.Default, hintCountUsed)`
3. Pass adjusted parameters to `BktTracer.Update()`

**Acceptance:**
- [ ] 0 hints = P_T 0.20 (unchanged baseline)
- [ ] 1 hint = P_T 0.14 (0.20 * 0.7)
- [ ] 2 hints = P_T 0.08 (0.20 * 0.4)
- [ ] 3 hints = P_T 0.02 (0.20 * 0.1)
- [ ] `BktParameters.AdjustForHints` is a pure static method, zero allocation
- [ ] `ConceptAttempted_V1` event includes `HintCountUsed` field for audit
- [ ] Mastery delta in `AnswerEvaluated` reflects hint-adjusted BKT

---

### SAI-002.4: Add HintCountUsed to ConceptAttempted Event

**Files to modify:**
- `src/actors/Cena.Actors/Events/QuestionEvents.cs` (or learner events file)
- `src/actors/Cena.Actors/Students/StudentState.cs`

**Implementation:**

Add `int HintCountUsed` to `ConceptAttempted_V1`. This is backward-compatible (nullable int or default 0). Update `StudentState.Apply(ConceptAttempted_V1)` to record hint usage in the `ConceptMasteryState` if needed for analytics.

**Acceptance:**
- [ ] `ConceptAttempted_V1.HintCountUsed` persisted
- [ ] Existing events deserialize with default 0 (backward compatible)
- [ ] NATS `cena.events.concept.attempted` payload includes hint count
- [ ] NatsOutboxPublisher publishes hint count (automatic via event shape)

---

## Testing

```csharp
[Fact]
public void HintLevel1_ReturnsPrerequisiteNudge()
{
    var generator = new HintContentGenerator();
    var context = new HintContext(
        HintLevel: 1,
        ConceptId: "quadratic-formula",
        QuestionStem: "Solve x^2 + 5x + 6 = 0",
        Explanation: null,
        Prerequisites: new[] { new MasteryPrerequisiteEdge("factoring", "quadratic-formula", 0.9f) },
        ConceptState: null,
        BloomLevel: 3);

    var hint = generator.Generate(context);

    Assert.Equal(1, hint.HintLevel);
    Assert.Contains("factoring", hint.Text, StringComparison.OrdinalIgnoreCase);
    Assert.True(hint.HasMoreHints);
}

[Fact]
public void BktCreditWeighting_3Hints_MinimalCredit()
{
    var adjusted = BktParameters.AdjustForHints(BktParameters.Default, hintsUsed: 3);
    Assert.Equal(0.02f, adjusted.P_T, precision: 3);

    // Correct answer with 3 hints should barely move mastery
    float mastery = BktTracer.Update(0.30f, isCorrect: true, adjusted);
    float masteryNoHints = BktTracer.Update(0.30f, isCorrect: true, BktParameters.Default);

    Assert.True(mastery < masteryNoHints, "Hint-penalized mastery should be lower");
}

[Fact]
public async Task ScaffoldingGate_RejectsHintBeyondMax()
{
    // Student with mastery 0.50 → HintsOnly level → max 1 hint
    // Request hintLevel: 2 → should be rejected
}
```
