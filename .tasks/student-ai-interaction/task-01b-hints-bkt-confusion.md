# Task 01b: Hint Content Generation + BKT Credit + Confusion Gating

**Track**: B (parallel with Track A)
**Effort**: 2-3 days
**Depends on**: Nothing (infrastructure exists)
**Blocks**: Task 04

---

## System Context

Cena is an event-sourced .NET educational platform using Proto.Actor virtual actors. Students interact via SignalR WebSocket. The learning session is managed by `LearningSessionActor` (child of `StudentActor`), which handles question delivery, answer evaluation, and hint requests.

Substantial hint infrastructure already exists:
- `RequestHintMessage` is handled by `LearningSessionActor` (line 85 dispatch, lines 254-290 handler)
- `HintRequested_V1` event is emitted with `HintLevel: 1|2|3` (lines 284-286)
- `HintDelivered` SignalR event sends `hintText` (placeholder) and `hasMoreHints` (line 288)
- `ScaffoldingService` determines max hints: Full=3, Partial=2, HintsOnly=1, None=0
- `BusConceptAttempt.HintCountUsed` is transmitted from client

**What's missing**: actual hint text content, BKT credit adjustment for hint usage, and confusion-state gating that respects productive struggle.

The affect/attention system has 3 services that directly impact hint delivery timing:
- `ConfusionDetector` — 4-state machine: NotConfused -> Confused -> ConfusionResolving -> ConfusionStuck
- `DisengagementClassifier` — distinguishes `Bored_TooEasy` (suppress hints) from `Fatigued_Cognitive` (simpler scaffolding)
- `CognitiveLoadService` — 3-factor fatigue model with difficulty adjustment output

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | 85 | `RequestHintMessage req => HandleHint(context, req)` dispatch |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | 254-290 | `HandleHint()` — current implementation emits `HintRequested_V1` and returns placeholder text |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | 371-383 | `RequestHintMessage` and `HintResponse` record definitions |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | 18-30 | `DetermineLevel()` — maps effective mastery + PSI to scaffolding level |
| `src/actors/Cena.Actors/Mastery/ScaffoldingLevel.cs` | 12-18 | Enum: Full, Partial, HintsOnly, None |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 79-85 | `ConfusionState` enum: NotConfused, Confused, ConfusionResolving, ConfusionStuck |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 34-72 | `Detect()` method — state machine transitions |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | 26-92 | `Classify()` method |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | 99-107 | `DisengagementType` enum |
| `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` | Full | 3-factor fatigue model — understand `DifficultyAdjustment` output |
| `src/actors/Cena.Actors/Mastery/BktTracer.cs` | 39 | `float next = posterior + (1f - posterior) * p.P_T;` — this is where hint credit multiplier applies |
| `src/actors/Cena.Actors/Mastery/BktParameters.cs` | 13 | `BktParameters(P_L0, P_T, P_S, P_G)` record |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | Find `BusConceptAttempt` | `HintCountUsed` field — already transmitted from client |
| `contracts/frontend/signalr-messages.ts` | 515-528 | `HintDeliveredPayload` — `hintText: string`, `diagram: string | null`, `hasMoreHints: boolean` |

---

## Implementation Requirements

### 1. Create `HintGenerationService`

**Location**: `src/actors/Cena.Actors/Services/HintGenerationService.cs`

This service generates hint text for 3 progressive disclosure levels. Hints are **NOT LLM calls** in this task — they use template-based generation from existing question metadata.

```
Input:  question metadata (stem, options, DistractorRationale, conceptIds),
        scaffolding level, hint level (1-3), language
Output: string (Markdown + LaTeX)
```

**Level 1 — Conceptual Nudge**:
- Source: concept graph prerequisites via `IConceptGraphCache`
- Template: "Think about [prerequisite concept name]. How does it relate to [current concept]?"
- If concept has no prerequisites: "Review what you know about [concept name]. What are the key properties?"

**Level 2 — Procedural Hint**:
- Source: `DistractorRationale` from the question's options (stored per option in the question aggregate)
- Template: eliminate one wrong option ("Option [X] is incorrect because [rationale]") or show partial approach
- If `DistractorRationale` is empty: "The answer involves applying [concept]. Focus on [Bloom-appropriate verb]."

**Level 3 — Worked Example**:
- Source: L1 explanation from the question aggregate (from Task 01a), or `DistractorRationale` of the correct answer
- Template: step-by-step solution with one step hidden ("Step 1: ... Step 2: ... Step 3: [complete this step]")
- If no explanation available: fall back to showing the correct answer with the rationale

**Language handling**: All templates must support Hebrew (he), Arabic (ar), English (en). Use the question's `Language` field. RTL-aware Markdown.

### 2. BKT Credit Adjustment

When a student uses hints before answering correctly, reduce the learning transition probability.

**Where to modify**: `BktTracer.cs` line 39 — `float next = posterior + (1f - posterior) * p.P_T`

The `P_T` (probability of transition to learned) must be multiplied by a credit factor:

| Hints Used | Credit Multiplier | Effective P_T (default P_T=0.20) |
|------------|-------------------|----------------------------------|
| 0 | 1.0 | 0.20 |
| 1 | 0.7 | 0.14 |
| 2 | 0.4 | 0.08 |
| 3 | 0.1 | 0.02 |

**Implementation approach**:
- Add an optional `int hintCount = 0` parameter to the BKT update method (or the context object that feeds it)
- Compute `adjustedPT = p.P_T * HintCreditMultiplier(hintCount)`
- Apply: `float next = posterior + (1f - posterior) * adjustedPT`
- The `hintCount` comes from `BusConceptAttempt.HintCountUsed` which is already transmitted

Do NOT modify `BktParameters` record — the credit adjustment is applied at call time, not stored in the parameters.

### 3. Confusion-State Gating

Before delivering a hint in `HandleHint()`, check `ConfusionDetector` state:

| ConfusionState | Explicit `RequestHint` | Automatic/Proactive Hint |
|----------------|----------------------|--------------------------|
| NotConfused | Deliver normally | Deliver normally |
| Confused | Deliver normally | Deliver normally |
| ConfusionResolving | Deliver (student override) | **SUPPRESS** — productive struggle |
| ConfusionStuck | Deliver normally | **PROACTIVELY PUSH** via SignalR |

Key insight (D'Mello & Graesser 2012): confusion that resolves leads to deeper learning. Do not interrupt productive struggle. But if the student explicitly requests a hint (`RequestHint` command), respect that override.

### 4. Boredom/Fatigue Interaction

Check `DisengagementClassifier` output before hint delivery:

| DisengagementType | Hint Behavior |
|------------------|--------------|
| Bored_TooEasy | Suppress automatic hints. If student requests: deliver but with higher complexity. |
| Bored_NoValue | Deliver hint. Low engagement may indicate confusion disguised as boredom. |
| Fatigued_Cognitive | Deliver simpler scaffolding (reduce hint complexity one level). |
| Fatigued_Motor | No change to hints — motor fatigue doesn't affect cognitive processing. |

### 5. Integration Point

`LearningSessionActor.HandleHint()` (line 254) is the single integration point. Modify this method to:
1. Check scaffolding level (already exists — `ScaffoldingService.DetermineLevel()`)
2. Check confusion state (NEW — `ConfusionDetector.Detect()`)
3. Check disengagement type (NEW — `DisengagementClassifier.Classify()`)
4. Generate hint content (NEW — `HintGenerationService.Generate()`)
5. Emit `HintRequested_V1` event (already exists)
6. Send `HintDelivered` SignalR event with real content (replace placeholder)

Do NOT create a new actor for hints. The `LearningSessionActor` is the correct orchestrator.

---

## What NOT to Do

- Do NOT make LLM calls for hints — use templates until Task 00 is done
- Do NOT modify `ConfusionDetector` or `DisengagementClassifier` — they work correctly, integrate with them
- Do NOT change the SignalR contract — `HintDelivered` already has the right shape
- Do NOT change `ScaffoldingService` — it determines max hints correctly
- Do NOT create a new actor — `LearningSessionActor` orchestrates
- Do NOT modify `BktParameters` record — credit adjustment is applied at call time

---

## Verification Checklist

- [ ] Request hint for concept where scaffolding=Full — 3 levels of progressive hints with real content
- [ ] Request hint for concept where scaffolding=HintsOnly — only level 1 hint, then "no more hints"
- [ ] Request hint for concept where scaffolding=None — hint suppressed, appropriate message
- [ ] Request hint while `ConfusionState == ConfusionResolving` (student explicit request) — hint delivered
- [ ] Automatic hint when `ConfusionState == ConfusionStuck` — proactively pushed
- [ ] Answer correctly after 0 hints — verify BKT P_T multiplier = 1.0
- [ ] Answer correctly after 2 hints — verify BKT P_T multiplier = 0.4
- [ ] Hint in Hebrew — RTL Markdown rendered correctly
- [ ] `BusConceptAttempt.HintCountUsed` matches actual hints delivered
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
