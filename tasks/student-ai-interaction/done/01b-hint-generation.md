# Task 01b: Hint Content Generation + BKT Credit + Confusion Gating

**Effort**: 2-3 days | **Track**: B (parallel with Track A) | **Depends on**: Nothing | **Blocks**: 04

---

## Context

You are working on the **Cena Platform** — an event-sourced .NET 8 adaptive learning system. Students interact via SignalR WebSocket sessions managed by `LearningSessionActor` (Proto.Actor virtual actor, per-student).

The hint **infrastructure** is fully built:
- `LearningSessionActor` handles `RequestHintMessage`, emits `HintRequested_V1` event
- `HintDelivered` SignalR event sends `hintText` (placeholder) + `hasMoreHints` to client
- `ScaffoldingService` (51 lines, stateless) maps mastery → hint limits: Full=3, Partial=2, HintsOnly=1, None=0
- `BusConceptAttempt.HintCountUsed` (line 39 of `NatsBusMessages.cs`) already transmitted from client

What's missing: **actual hint text**, **BKT credit adjustment for hints**, **confusion-state gating**.

### Key Services Already Built (Read These — Do Not Modify Them)

- **ConfusionDetector** (96 lines): 4-state machine `NotConfused → Confused → ConfusionResolving → ConfusionStuck`. Uses 4 signals: wrong-on-mastered, elevated RT+correct, answer changes, hint-then-cancel. **Research basis**: D'Mello & Graesser (2012) — confusion that resolves leads to deeper learning. Do NOT interrupt `ConfusionResolving`.
- **DisengagementClassifier** (133 lines): Distinguishes `Bored_TooEasy` (fast+correct, low engagement) from `Fatigued_Cognitive` (slow+inaccurate, long session). **These require opposite interventions.**
- **ScaffoldingService** (51 lines): Pure function `DetermineLevel(effectiveMastery, psi) → Full|Partial|HintsOnly|None`. Returns `ScaffoldingMetadata` with MaxHints, ShowWorkedExample, RevealAnswer.
- **CognitiveLoadService** (260 lines): 3-factor fatigue model → Low/Moderate/High/Critical.

---

## Objective

1. Create `HintGenerationService` that produces 3-level progressive hint text
2. Wire confusion-state gating into hint delivery in `LearningSessionActor`
3. Implement BKT credit adjustment based on hints used

---

## Files to Read First (MANDATORY)

| File | Path | Lines | Key Structure |
|------|------|-------|---------------|
| LearningSessionActor | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | 322 | `RequestHintMessage` handler at line 316. Integration point. |
| ScaffoldingService | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | 51 | `DetermineLevel()`, `GetScaffoldingMetadata()` |
| ConfusionDetector | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 96 | `Detect(ConfusionInput) → ConfusionState` enum |
| DisengagementClassifier | `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | 133 | `Bored_TooEasy` vs `Fatigued_Cognitive` |
| CognitiveLoadService | `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | 260 | Fatigue model |
| NatsBusMessages | `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | 112 | `BusConceptAttempt.HintCountUsed` at line 39 |
| signalr-messages.ts | `contracts/frontend/signalr-messages.ts` | 749 | `HintDelivered.hintText` at line 528 |

---

## Implementation

### 1. Create `HintGenerationService`

**File**: `src/actors/Cena.Actors/Services/HintGenerationService.cs`

Hints follow a 3-level progressive disclosure. NOT free-form LLM calls — template-based for now:

```csharp
public interface IHintGenerationService
{
    HintContent GenerateHint(HintContext context);
}

public record HintContext(
    int HintLevel,                    // 1, 2, or 3
    string ConceptId,
    string QuestionStem,
    IReadOnlyList<string> PrerequisiteConceptIds,
    IReadOnlyList<string> PrerequisiteNames,
    string? DistractorRationale,      // From QuestionOptionData.DistractorRationale
    ScaffoldingLevel ScaffoldingLevel,
    string Language);                 // "he", "ar", "en"

public record HintContent(
    string HintText,                  // Markdown+LaTeX
    bool HasMoreHints);
```

**Level 1 — Conceptual Nudge**: "Think about what happens when..." — derived from prerequisite concept names via `IConceptGraphCache.GetPrerequisites()`.

**Level 2 — Procedural Scaffold**: Eliminate one distractor or show partial approach — derived from `DistractorRationale` field on question options + aggregate error stats.

**Level 3 — Reveal**: Full worked solution — use L1 persisted explanation (from Task 01a) or static text. When L1 is null (pre-01a questions), use "Review the concept [name] in your study materials."

**Language-aware**: All hint templates must support Hebrew and Arabic. Use a simple resource/template approach — not i18n library overhead.

### 2. BKT Credit Adjustment

Find where BKT updates happen in the student mastery flow. The `BktService` exists at `src/actors/Cena.Actors/Services/BktService.cs`. Apply a P(T) multiplier based on hints used:

| Hints Used | P(T) Multiplier | Rationale |
|------------|----------------|-----------|
| 0 | 1.0 | Full evidence of learning |
| 1 | 0.7 | Mild scaffold, mostly independent |
| 2 | 0.4 | Significant help, partial credit |
| 3 | 0.1 | Near-full reveal, minimal independent learning |

The multiplier modifies the `P_T` (transition probability) in the BKT update formula:
```
P(L_{n+1}) = P(L_n | obs) + (1 - P(L_n | obs)) × P_T × hintMultiplier
```

### 3. Confusion-State Gating

In `LearningSessionActor`, **before delivering any hint**, check `ConfusionDetector` state:

```csharp
var confusionState = _confusionDetector.Detect(BuildConfusionInput());

switch (confusionState)
{
    case ConfusionState.ConfusionResolving:
        // Productive struggle — DO NOT auto-deliver hints
        // But if student EXPLICITLY requested via RequestHint, allow it
        if (!isExplicitStudentRequest)
            return; // Suppress automatic hint
        break;

    case ConfusionState.ConfusionStuck:
        // Student stuck — proactively push hint via SignalR
        // Do not wait for request
        break;
}
```

### 4. Boredom Suppression

Check `DisengagementClassifier` before hint delivery:
- `Bored_TooEasy` → Suppress automatic hints. Student is fast+correct — hints signal the system thinks they're struggling, which is counterproductive. Instead, increase difficulty via `QuestionSelector`.
- `Fatigued_Cognitive` → Offer simpler scaffolding, reduce hint complexity (use Level 1 style even for Level 2/3).

### 5. Integration Point

`LearningSessionActor` orchestrates. It already handles `RequestHintMessage`. Add confusion check and content generation there. Do NOT create a new actor for hint delivery.

---

## What NOT to Do

- Do NOT make LLM calls for hints — use templates until Task 00 is done
- Do NOT modify `ConfusionDetector` or `DisengagementClassifier` — they're correct, just consume them
- Do NOT change the SignalR contract — `HintDelivered` already has the right shape
- Do NOT change `ScaffoldingService` — it correctly determines max hints
- Do NOT create a separate actor for hints — `LearningSessionActor` is the orchestrator

---

## Verification Checklist

- [ ] Request hint for concept where scaffolding=Full → 3 levels of progressive hints returned
- [ ] Request hint for scaffolding=HintsOnly → only 1 hint available
- [ ] Request hint while `ConfusionState == ConfusionResolving` → suppressed (unless explicit request)
- [ ] `ConfusionState == ConfusionStuck` → hint proactively pushed
- [ ] Answer correct after 2 hints → BKT credit reduced by 0.4× multiplier
- [ ] Answer correct after 0 hints → BKT credit at full 1.0× multiplier
- [ ] `DisengagementClassifier == Bored_TooEasy` → automatic hints suppressed
- [ ] Hint text is language-appropriate (Hebrew and Arabic)
- [ ] `BusConceptAttempt.HintCountUsed` matches actual hints delivered
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
