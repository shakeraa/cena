# TASK-SAI-02: Hint Content Generation + BKT Credit + Confusion-State Gating

**Priority**: HIGH — zero AI cost, extends existing infrastructure
**Effort**: 2-3 days
**Depends on**: Nothing (hint infrastructure already exists)
**Track**: B (parallel with Track A)

---

## Context

Substantial hint infrastructure already exists but delivers **empty placeholder content**:

| Component | Status | File |
|-----------|--------|------|
| `HintRequested_V1` event | Exists | `src/actors/Cena.Actors/Events/PedagogyEvents.cs` |
| `RequestHintMessage` handling | Exists | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` |
| `HintDelivered` SignalR message | Exists | `contracts/frontend/signalr-messages.ts` |
| `ScaffoldingService` (max hints by mastery) | Exists | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` |
| `BusConceptAttempt.HintCountUsed` | Exists | `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` |
| `ConfusionDetector` (4-signal) | Exists | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` |
| `ConfusionResolutionTracker` | Exists | (adaptive patience 3-7 questions) |
| `DisengagementClassifier` (Bored vs Fatigued) | Exists | `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` |
| **Hint text generation** | **MISSING** | — |
| **BKT credit adjustment for hints** | **MISSING** | — |
| **Confusion-state gating** | **MISSING** | — |

This task fills three gaps with **zero LLM cost** — all hint content is derived from existing data.

---

## Part A: Hint Content Generation (3-Level Ladder)

### Design

Each hint level is generated from data that already exists on the question and concept graph:

| Level | Hint Type | Source | Example |
|-------|-----------|--------|---------|
| 1 | **Nudge** — "Think about [prerequisite]" | `IConceptGraphCache` prerequisites for this concept | "Review how to factor quadratic expressions before attempting this." |
| 2 | **Eliminate** — remove one distractor or show partial approach | `DistractorRationale` on the wrong options + aggregate error stats | "Option C is incorrect because it confuses velocity with acceleration." |
| 3 | **Reveal** — full worked approach (not the answer) | `Explanation` field (TASK-SAI-01) or concept-level worked example | Step-by-step approach without giving the final answer |

### Implementation

**Create**: `src/actors/Cena.Actors/Services/HintGenerator.cs`

```csharp
public interface IHintGenerator
{
    HintContent Generate(HintRequest request);
}

public sealed record HintRequest(
    int HintLevel,                              // 1, 2, or 3
    string QuestionId,
    string ConceptId,
    IReadOnlyList<string> PrerequisiteConceptIds,  // from concept graph
    IReadOnlyList<QuestionOptionState> Options,    // includes DistractorRationale
    string? Explanation,                           // from PublishedQuestion (may be null)
    string StudentAnswer);                         // which option the student is considering/chose

public sealed record HintContent(
    string Text,           // Markdown + LaTeX
    byte[]? Diagram,       // null unless visual hint
    bool HasMoreHints);    // true if higher levels available
```

**Logic** (pure function, no I/O):

- Level 1: Look up prerequisite concepts from `IConceptGraphCache`. Format: "Before solving this, make sure you understand **{prerequisiteName}**. How does {prerequisiteName} relate to what's being asked?"
- Level 2: Find the weakest distractor (highest `DistractorRationale` relevance to student's answer). Eliminate it: "You can rule out option {label} — {rationale}." If student hasn't answered yet, eliminate the most commonly wrong option (from aggregate stats if available, else random non-correct).
- Level 3: If `Explanation` exists, reformat as a hint (show approach, not answer). If null, fall back to: "The key concept here is **{conceptName}**. Try applying the definition of {conceptName} to each option."

### Wire into LearningSessionActor

**Modify**: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

In `HandleHint()`, replace the current pass-through with:

```csharp
private Task HandleHint(IContext context, RequestHintMessage req)
{
    var scaffoldLevel = ScaffoldingService.DetermineLevel(currentMastery, currentPsi);
    if (req.HintLevel > scaffoldLevel.MaxHints)
    {
        context.Respond(new HintResponse(req.HintLevel, Delivered: false));
        return Task.CompletedTask;
    }

    var hint = _hintGenerator.Generate(new HintRequest(
        req.HintLevel, req.QuestionId, req.ConceptId,
        _conceptGraph.GetPrerequisites(req.ConceptId),
        _currentQuestion.Options,
        _currentQuestion.Explanation,
        _lastStudentAnswer));

    // Emit event for analytics
    context.Send(context.Parent!, new DelegateEvent(
        new HintRequested_V1(_studentId, _sessionId, req.ConceptId, req.QuestionId, req.HintLevel)));

    // Deliver via SignalR
    context.Respond(new HintResponse(req.HintLevel, Delivered: true, hint.Text, hint.Diagram, hint.HasMoreHints));
    return Task.CompletedTask;
}
```

---

## Part B: BKT Credit Adjustment for Hint Usage

### Design

Hints reduce the evidential value of a correct answer. A student who needed 3 hints to answer correctly has demonstrated less mastery than one who answered unaided.

**Credit curve** (modifies the effective `IsCorrect` signal weight, not the BKT parameters):

| Hints Used | Credit Multiplier | Rationale |
|-----------|-------------------|-----------|
| 0 | 1.0 | Full credit |
| 1 | 0.7 | Nudge is minor assistance |
| 2 | 0.4 | Eliminating a distractor is substantial |
| 3 | 0.1 | Near-reveal, minimal independent reasoning |

### Implementation

**Modify**: `src/actors/Cena.Actors/Services/BktService.cs`

Do NOT change the core `BktService.Update()` method. Instead, create a wrapper:

**Create**: `src/actors/Cena.Actors/Services/HintAdjustedBktService.cs`

```csharp
public sealed class HintAdjustedBktService
{
    private static readonly double[] CreditMultipliers = [1.0, 0.7, 0.4, 0.1];

    public BktUpdateResult UpdateWithHints(BktUpdateInput input, int hintCountUsed)
    {
        var result = _bktService.Update(input);

        if (hintCountUsed > 0 && input.IsCorrect)
        {
            var multiplier = CreditMultipliers[Math.Min(hintCountUsed, 3)];
            // Blend posterior toward prior — hints reduce the mastery update magnitude
            var adjustedPosterior = input.PriorMastery + (result.PosteriorMastery - input.PriorMastery) * multiplier;
            return result with { PosteriorMastery = adjustedPosterior };
        }

        return result;
    }
}
```

**Modify**: `src/actors/Cena.Actors/Students/StudentActor.Commands.cs`

Replace direct `_bktService.Update()` calls with `_hintAdjustedBktService.UpdateWithHints()`, passing `cmd.HintCountUsed`.

---

## Part C: Confusion-State Gating

### Design

The `ConfusionDetector` already classifies students into states:
- `NotConfused` — hints available normally
- `Confused` — confusion just detected, patience window starts
- `ConfusionResolving` — student is working through confusion. **DO NOT interrupt with unsolicited hints.** (D'Mello & Graesser 2012: confusion that resolves leads to deeper learning)
- `ConfusionStuck` — patience window exhausted. **NOW offer scaffolding.**

The `DisengagementClassifier` distinguishes:
- `Bored_TooEasy` — **suppress hints**, increase difficulty instead
- `Fatigued_Cognitive` — offer simpler scaffolding, suggest break

### Implementation

**Modify**: `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

Add gating logic before hint delivery:

```csharp
private bool ShouldDeliverHint(RequestHintMessage req, bool isStudentInitiated)
{
    var confusion = _confusionDetector.Detect(BuildConfusionInput());
    var disengagement = _disengagementClassifier.Classify(BuildDisengagementInput());

    // Student-initiated hints always allowed (explicit request overrides gating)
    if (isStudentInitiated) return true;

    // System-suggested hints: respect confusion state
    return confusion switch
    {
        ConfusionState.ConfusionResolving => false,  // don't interrupt productive struggle
        ConfusionState.ConfusionStuck => true,        // patience exhausted, intervene
        _ => disengagement != DisengagementType.Bored_TooEasy  // bored students don't need hints
    };
}
```

**Key rule**: `isStudentInitiated` = true when the hint comes from `RequestHint` SignalR message. `isStudentInitiated` = false when the system proactively suggests a hint (future feature). For now, all hints are student-initiated, so this is future-proofing.

---

## Coding Standards

- `HintGenerator` must be a **pure function** — no I/O, no state, no async. It takes data in, returns content out. This makes it trivially testable.
- `HintAdjustedBktService` wraps `BktService` via composition, not inheritance. The original `BktService` remains untouched.
- Confusion gating is a **guard clause** at the top of the hint handler, not deeply nested logic.
- All hint text must support Markdown + LaTeX (consistent with the `HintDelivered` SignalR contract).
- Write tests for: each hint level generation, BKT credit curve math, confusion-state gating truth table.

---

## Acceptance Criteria

1. Level 1 hints reference prerequisite concepts from the concept graph
2. Level 2 hints eliminate a distractor using existing `DistractorRationale` data
3. Level 3 hints show approach from `Explanation` field (or generic fallback)
4. `ScaffoldingService` limits max hints by mastery level (Full=3, Partial=2, HintsOnly=1, None=0)
5. BKT credit is reduced proportionally to hint usage (0/1/2/3 hints → 1.0/0.7/0.4/0.1)
6. Confusion-resolving students are not interrupted with system hints
7. Bored students (`Bored_TooEasy`) do not receive system-suggested hints
8. Student-initiated hints (`RequestHint` SignalR) always bypass gating
9. `HintRequested_V1` event emitted for every delivered hint (analytics)
10. Zero LLM calls — all content derived from existing data
