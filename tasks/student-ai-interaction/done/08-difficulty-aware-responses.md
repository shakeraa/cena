# Task 08: Thread Question Difficulty Through Response Pipeline

**Effort**: 1 day | **Track**: Post-SAI enhancement | **Depends on**: Tasks 01b, 02, 03, 07 (all complete)

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor virtual actors, Marten, Redis, SignalR.

All Student AI Interaction tasks (00-07) are complete. The response pipeline (hints, L2/L3 explanations, TutorActor) works but has a gap: **question intrinsic difficulty is not passed to any response service**. The `QuestionSelector` uses difficulty for ZPD targeting, but once a question is served and the student gets it wrong, the difficulty signal is lost.

`PublishedQuestion.Difficulty` (float 0.0-1.0) is available in `LearningSessionActor` at the point where it calls the response services. It just isn't passed through.

**Design doc**: `docs/design/difficulty-aware-responses.md`

### Why This Matters

The **difficulty gap** (`questionDifficulty - masteryProbability`) determines whether to encourage ("this was a stretch") or investigate ("you should know this"). Without it, a student failing a 0.9-difficulty stretch question gets the same response as one failing a 0.3-difficulty question below their level.

---

## Files to Read First (MANDATORY)

| File | Path | Why |
|------|------|-----|
| Design doc | `docs/design/difficulty-aware-responses.md` | Full rationale and data flow diagram |
| QuestionSelector | `src/actors/Cena.Actors/Serving/QuestionSelector.cs` | How difficulty is used in selection (ZPD, lines 65-66, 208-230) |
| LearningSessionActor | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Where response services are called — the integration point |
| PublishedQuestion | `src/actors/Cena.Actors/Serving/QuestionPoolActor.cs` | `Difficulty` field at line ~162 |
| ExplanationCacheService | `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` | L2 cache — add difficulty framing to generation prompt |
| PersonalizedExplanationService | `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` | L3 — add gap to context, adjust max tokens |
| HintGenerationService | `src/actors/Cena.Actors/Services/HintGenerationService.cs` | Adjust hint specificity based on difficulty frame |
| TutorActor | `src/actors/Cena.Actors/Tutoring/TutorActor.cs` | Add to start messages and RAG prompt |

---

## Implementation

### Step 1: Create `DifficultyGap.cs` (NEW — ~25 lines)

**File**: `src/actors/Cena.Actors/Services/DifficultyGap.cs`

```csharp
namespace Cena.Actors.Services;

/// <summary>
/// Computes and classifies the gap between question difficulty and student mastery.
/// Positive = stretch (question harder than ability).
/// Negative = regression (question easier than ability).
/// </summary>
public static class DifficultyGap
{
    public static float Compute(float questionDifficulty, float masteryProbability)
        => questionDifficulty - masteryProbability;

    public static DifficultyFrame Classify(float gap) => gap switch
    {
        > 0.3f  => DifficultyFrame.Stretch,
        > 0.1f  => DifficultyFrame.Challenge,
        > -0.1f => DifficultyFrame.Appropriate,
        > -0.3f => DifficultyFrame.Expected,
        _       => DifficultyFrame.Regression
    };

    /// <summary>Human-readable framing for LLM prompts.</summary>
    public static string ToPromptFrame(DifficultyFrame frame) => frame switch
    {
        DifficultyFrame.Stretch    => "This was a very challenging question above the student's current level. Encourage effort, don't assume they should have known.",
        DifficultyFrame.Challenge  => "This question was above the student's comfort zone — a productive challenge.",
        DifficultyFrame.Appropriate => "", // No special framing needed
        DifficultyFrame.Expected   => "This question should be within the student's ability. Focus on identifying the specific gap or misconception.",
        DifficultyFrame.Regression => "This question is below the student's demonstrated level. Check whether a prerequisite concept has decayed or if this was a careless error.",
        _ => ""
    };
}

public enum DifficultyFrame
{
    Stretch,      // Well above ability
    Challenge,    // Above ability
    Appropriate,  // ZPD center
    Expected,     // Below ability
    Regression    // Well below ability
}
```

### Step 2: Add difficulty to `ExplanationCacheService`

Add `float questionDifficulty` parameter to the generation method. When generating a NEW cached explanation (cache miss), include the difficulty frame in the LLM prompt:

```csharp
var frame = DifficultyGap.Classify(DifficultyGap.Compute(questionDifficulty, masteryProbability));
var framePrompt = DifficultyGap.ToPromptFrame(frame);
// Prepend to existing generation prompt if non-empty
```

Do NOT change the cache key — difficulty is per-question, already implicit in `questionId`.

### Step 3: Add difficulty to `PersonalizedExplanationService`

Add `float QuestionDifficulty` and `float DifficultyGap` to the context record. Use in:

1. **Prompt context**: Include difficulty frame and gap in the user prompt
2. **Max tokens**: Stretch questions get +50% tokens (student is learning); regression questions get -30% (student just needs a reminder)
3. **Confusion escalation**: If stretch + ConfusionStuck → extra encouragement in the scaffolding upgrade

### Step 4: Add difficulty to `HintGenerationService`

Add `float QuestionDifficulty` and `DifficultyFrame Frame` to `HintGenerationContext`. Adjust:

- **Stretch + Level 2**: More specific procedural hint (student needs more scaffolding on hard questions)
- **Regression + Level 1**: Point to prerequisites that may have decayed (not the question itself)
- **Regression + Level 3**: Abbreviated reveal (student knows this, just needs a reminder)

### Step 5: Add difficulty to `TutorActor`

Add `float QuestionDifficulty` to all 4 start message records. Include in the RAG system prompt:

```
"The question that triggered this session was rated {difficultyLabel}
(difficulty {difficulty:F2} vs mastery {mastery:F2}, gap = {gap:+F2}).
Frame your responses accordingly."
```

### Step 6: Wire in `LearningSessionActor`

At each call site where the actor invokes response services, compute the gap and pass through:

```csharp
var difficulty = currentQuestion.Difficulty;
var mastery = (float)conceptMasteryState.MasteryProbability;
var gap = DifficultyGap.Compute(difficulty, mastery);
var frame = DifficultyGap.Classify(gap);
```

---

## What NOT to Do

- Do NOT change `QuestionSelector` — already handles difficulty via ZPD
- Do NOT change the L2 cache key — difficulty is per-question, implicit in questionId
- Do NOT change `BktService` or `HintAdjustedBktService` — BKT credit is about hints, not difficulty
- Do NOT change `ScaffoldingService` — mastery-driven, correct abstraction
- Do NOT change `ConfusionDetector` — behavioral signals, not difficulty
- Do NOT change `ErrorClassificationService` — error type is orthogonal to difficulty

---

## Verification Checklist

- [ ] `DifficultyGap.Classify(0.4f)` returns `Stretch` (gap > 0.3)
- [ ] `DifficultyGap.Classify(0.0f)` returns `Appropriate` (gap ~ 0)
- [ ] `DifficultyGap.Classify(-0.4f)` returns `Regression` (gap < -0.3)
- [ ] L2 explanation for a stretch question includes encouraging framing
- [ ] L2 explanation for a regression question includes diagnostic framing
- [ ] L3 explanation max tokens are +50% for stretch, -30% for regression
- [ ] Level 2 hint is more specific for stretch questions
- [ ] Level 1 hint points to prerequisites for regression questions
- [ ] TutorActor prompt includes difficulty context
- [ ] All existing tests still pass (no regressions)
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
