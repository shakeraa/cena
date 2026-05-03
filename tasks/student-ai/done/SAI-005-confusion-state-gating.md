# SAI-005: Confusion-State Gating for Hints and Explanations

**Priority:** P1 — prevents AI features from harming learning
**Blocked by:** SAI-002 (hint content generation)
**Estimated effort:** 2 days
**Stack:** .NET 9, Proto.Actor

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The `ConfusionDetector` and `ConfusionResolutionTracker` are fully implemented services that track whether a student is working through confusion productively. Research (D'Mello & Graesser 2012, Kapur 2008) shows that confusion that resolves naturally leads to **deeper learning** than immediate scaffolding.

The `DisengagementClassifier` distinguishes boredom from fatigue — these require **opposite interventions**:
- Bored → increase difficulty, DON'T offer hints (signals "I think you're struggling" — counterproductive)
- Fatigued → offer simpler scaffolding, recommend break

This task wires these affect signals into the hint and explanation delivery pipeline so the AI features from SAI-002/003/004 don't accidentally harm learning.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 4 confusion signals, ConfusionState enum |
| `src/actors/Cena.Actors/Services/ConfusionResolutionTracker.cs` | Adaptive patience window (3-7 questions), resolution rate tracking |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | Bored_TooEasy, Bored_NoValue, Fatigued_Cognitive, Fatigued_Motor |
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | FocusLevel enum, PredictRemainingProductiveQuestions() |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Where hints and explanations are delivered |

## Subtasks

### SAI-005.1: IDeliveryGate Service

**Files to create:**
- `src/actors/Cena.Actors/Hints/IDeliveryGate.cs`
- `src/actors/Cena.Actors/Hints/DeliveryGate.cs`

**Implementation:**

A domain service that decides whether a hint or explanation should be delivered NOW, DEFERRED, or SUPPRESSED:

```csharp
public interface IDeliveryGate
{
    DeliveryDecision Evaluate(DeliveryContext context);
}

public sealed record DeliveryContext(
    ConfusionState ConfusionState,
    DisengagementType? DisengagementType,
    FocusLevel FocusLevel,
    bool IsStudentInitiated,          // true if RequestHint, false if auto-explanation
    int QuestionsUntilPatience);      // from ConfusionResolutionTracker

public sealed record DeliveryDecision(
    DeliveryAction Action,
    string? Reason,
    string? StudentMessage);          // shown to student when deferred/suppressed

public enum DeliveryAction
{
    Deliver,       // Proceed with hint/explanation
    Defer,         // Wait — student may resolve on their own
    Suppress       // Don't deliver at all (wrong intervention for this state)
}
```

**Decision matrix:**

| ConfusionState | DisengagementType | Student-Initiated? | Action | StudentMessage |
|---------------|-------------------|-------------------|--------|----------------|
| ConfusionResolving | any | No | **Defer** | null (silent — system just waits) |
| ConfusionResolving | any | Yes (RequestHint) | **Deliver** | null (student explicitly asked) |
| ConfusionStuck | any | any | **Deliver** | null |
| any | Bored_TooEasy | No | **Suppress** | null (difficulty increase instead) |
| any | Bored_TooEasy | Yes | **Deliver** | null (student explicitly asked) |
| any | Fatigued_Cognitive | any | **Deliver** | null (scaffolding helps fatigued students) |
| NotConfused | null | any | **Deliver** | null |

Key rule: **student-initiated hints always pass through** — the student knows they need help.

**Acceptance:**
- [ ] ConfusionResolving + auto = Defer
- [ ] ConfusionResolving + student-initiated = Deliver (explicit override)
- [ ] Bored_TooEasy + auto = Suppress
- [ ] Bored_TooEasy + student-initiated = Deliver
- [ ] All other states = Deliver
- [ ] Pure domain logic — no I/O, no dependencies beyond the enum values
- [ ] Exhaustive pattern match (no default fallthrough that could silently suppress)

---

### SAI-005.2: Wire DeliveryGate into LearningSessionActor

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Implementation:**

**For hint delivery** (in `RequestHintMessage` handler):
1. Build `DeliveryContext` from current session affect state
2. Set `IsStudentInitiated = true` (student explicitly requested hint)
3. Call `IDeliveryGate.Evaluate(context)`
4. If `Defer`: respond with `HintResponse(delivered: false, reason: "working_through_it")`
5. If `Suppress`: respond with `HintResponse(delivered: false, reason: "try_harder_question")`
6. If `Deliver`: proceed with `IHintContentGenerator.Generate()` (from SAI-002)

**For explanation delivery** (in `HandleEvaluateAnswer`):
1. Build `DeliveryContext` with `IsStudentInitiated = false`
2. If `Defer`: set `AnswerEvaluated.explanation = ""` (empty — don't send explanation)
3. If `Suppress`: set `AnswerEvaluated.explanation = ""` (empty)
4. If `Deliver`: proceed with `ExplanationResolver.ResolveAsync()` (from SAI-003)

**Acceptance:**
- [ ] Hint RequestHint path checks DeliveryGate before content generation
- [ ] Explanation AnswerEvaluated path checks DeliveryGate before resolution
- [ ] Deferred explanations result in empty string (not null — client safety)
- [ ] Suppressed hints return `delivered: false` with machine-readable reason
- [ ] `HintRequested_V1` event still emitted even when deferred/suppressed (analytics)
- [ ] Counter: `cena.delivery.gated_total` with `action` tag (deliver/defer/suppress) and `trigger` tag (hint/explanation)

---

### SAI-005.3: ConfusionState Propagation to Session

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Implementation:**

The `ConfusionDetector` and `DisengagementClassifier` produce their outputs during answer evaluation. Ensure these outputs are available in the session actor when the DeliveryGate needs them:

1. After `HandleEvaluateAnswer()` computes BKT + error classification:
   - Call `IConfusionDetector.Detect(signals)` → `ConfusionState`
   - Call `IDisengagementClassifier.Classify(signals)` → `DisengagementType?`
   - Store both in session state (volatile — not persisted)
2. These values are used by `DeliveryGate.Evaluate()` in the same handler

If these services are already called in the session flow, reuse their outputs. Do NOT call them twice.

**Acceptance:**
- [ ] ConfusionState available at delivery gate evaluation time
- [ ] DisengagementType available at delivery gate evaluation time
- [ ] No duplicate service calls (compute once, use for gate + difficulty adjustment)
- [ ] Session state holds current confusion + disengagement (volatile, not event-sourced)

---

## Testing

```csharp
[Fact]
public void ConfusionResolving_AutoExplanation_Deferred()
{
    var gate = new DeliveryGate();
    var ctx = new DeliveryContext(
        ConfusionState.ConfusionResolving, null, FocusLevel.Engaged,
        IsStudentInitiated: false, QuestionsUntilPatience: 3);

    var decision = gate.Evaluate(ctx);

    Assert.Equal(DeliveryAction.Defer, decision.Action);
}

[Fact]
public void ConfusionResolving_StudentRequestsHint_Delivered()
{
    var gate = new DeliveryGate();
    var ctx = new DeliveryContext(
        ConfusionState.ConfusionResolving, null, FocusLevel.Engaged,
        IsStudentInitiated: true, QuestionsUntilPatience: 3);

    var decision = gate.Evaluate(ctx);

    Assert.Equal(DeliveryAction.Deliver, decision.Action);
}

[Fact]
public void BoredTooEasy_AutoExplanation_Suppressed()
{
    var gate = new DeliveryGate();
    var ctx = new DeliveryContext(
        ConfusionState.NotConfused, DisengagementType.Bored_TooEasy, FocusLevel.Engaged,
        IsStudentInitiated: false, QuestionsUntilPatience: 0);

    var decision = gate.Evaluate(ctx);

    Assert.Equal(DeliveryAction.Suppress, decision.Action);
}
```
