# Task 03: L3 Personalized Explanation Generation

**Effort**: 3-4 days | **Track**: C | **Depends on**: Task 02 (L2 cache as fallback) | **Blocks**: 04

---

## Context

You are working on the **Cena Platform** — event-sourced .NET 8, Proto.Actor, Marten, Redis, SignalR.

After Tasks 00-02: the system has real Anthropic SDK calls, persisted L1 explanations, and an L2 Redis cache keyed by (questionId, errorType, language). L2 serves ~80-90% of cases.

This task builds **L3**: when the student's context demands a truly personalized explanation (novel error pattern, Transfer/Systematic errors, or methodology-specific tone), generate on-demand using the student's full learning context.

**Research basis**: Personalized feedback is one of the strongest known interventions (0.3-0.5 SD, Anderson et al. 1995). VanLehn (2011) showed step-level tutoring (+0.76 SD) outperforms problem-level (+0.31 SD). L3 explanations that reference the student's specific mastery gaps are step-level.

---

## Objective

Generate LLM-powered explanations that incorporate: mastery level, scaffolding mode, active methodology, confusion state, behavioral signals (backspace count, answer changes), and error type.

---

## Files to Read First (MANDATORY)

| File | Path | Lines | Key Structure |
|------|------|-------|---------------|
| ScaffoldingService | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | 51 | `DetermineLevel(effectiveMastery, psi)` → Full/Partial/HintsOnly/None. `GetScaffoldingMetadata()` returns MaxHints, ShowWorkedExample. |
| ConfusionDetector | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 96 | 4-state: NotConfused/Confused/ConfusionResolving/ConfusionStuck |
| DisengagementClassifier | `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | 133 | Bored_TooEasy vs Fatigued_Cognitive |
| NatsBusMessages | `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | 112 | `BusConceptAttempt`: BackspaceCount, AnswerChangeCount — uncertainty signals |
| routing-config.yaml | `contracts/llm/routing-config.yaml` | 552 | `answer_evaluation` → Sonnet 4.6, temp 0.3. `socratic_question` → Sonnet, temp 0.4. Section 6: prompt caching with 5-min TTL. |
| LearningSessionActor | `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | 322 | Integration point |
| ExplanationCacheService | `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` | (Task 02) | L2 fallback |

---

## Implementation

### 1. Student Context Assembly

Build a structured context block for the LLM prompt. All data is available from the student's actor state:

```csharp
public record PersonalizedExplanationContext(
    // Question
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    ExplanationErrorType ErrorType,
    string Language,

    // Mastery state (from StudentActor → ConceptMasteryState)
    float MasteryProbability,        // P(L) from BKT
    int BloomLevel,                  // 0-6
    ScaffoldingLevel Scaffolding,    // Full/Partial/HintsOnly/None

    // Methodology (from MethodologyResolver)
    string ActiveMethodology,        // Socratic, WorkedExample, Feynman, etc.

    // Affect (from services)
    ConfusionState ConfusionState,
    string? DisengagementType,       // Bored_TooEasy, Fatigued_Cognitive, null

    // Behavioral signals (from SubmitAnswer)
    int BackspaceCount,              // High = uncertainty
    int AnswerChangeCount,           // High = option confusion
    int HintsUsed,

    // Prerequisite readiness
    float PrerequisiteSatisfactionIndex  // PSI
);
```

### 2. Methodology-Aware Prompt Templates

The LLM prompt MUST vary by methodology:

| Methodology | Prompt Instruction | Tone |
|-------------|-------------------|------|
| **Socratic** | "Ask a guiding question that leads the student to discover the error. NEVER reveal the answer." | Questioning |
| **WorkedExample** | "Show a step-by-step solution to a similar problem. Let the student apply the pattern." | Demonstrating |
| **Feynman** | "Ask the student to explain the concept. Point out where their explanation breaks." | Challenging |
| **Analogy** | "Explain this concept through a comparison to [prerequisite concept]." | Connecting |
| **RetrievalPractice** | "Before explaining, ask: 'What do you remember about [concept]?' Then fill gaps." | Prompting recall |
| **Direct** (default) | "Explain the error clearly: what went wrong, why, and the correct approach." | Instructing |

### 3. Scaffolding-Depth Mapping

ScaffoldingService levels map to explanation depth:
- **Full** (mastery < 0.20): Complete worked example with every step
- **Partial** (mastery < 0.40): Acknowledge what's right, explain the gap
- **HintsOnly** (mastery < 0.70): Brief pointer, no full solution
- **None** (mastery ≥ 0.70): Should not reach L3 — redirect to L2

### 4. Confusion-Aware Delivery

- `ConfusionResolving` → Do NOT deliver L3. Student is in productive struggle. Return L2 cached explanation only if explicitly requested.
- `ConfusionStuck` → Deliver L3 with extra scaffolding (upgrade to next scaffolding level).

### 5. Prompt Caching

Use Anthropic's `cache_control: { type: "ephemeral" }` per routing-config section 6. The student context system prompt (methodology rules, scaffolding instructions) changes per-session but not per-question. Cache the system prompt with 5-min TTL.

### 6. Fallback Chain

```
L3 attempt → Success? → Return personalized explanation
    ↓ (failure: circuit open, timeout, budget exhausted)
L2 cache → Hit? → Return cached per-errortype explanation
    ↓ (miss)
L1 static → Exists? → Return persisted AI explanation
    ↓ (null)
"Review this concept and try again." → Generic fallback (NEVER leave student with nothing)
```

### 7. Cost Guard

Check per-student daily budget before L3 calls:
- `daily_output_token_limit: 25000` per routing config
- Track cumulative tokens in `LearningSessionActor` session state
- If budget exhausted → serve L2 instead, log budget_exhausted metric

---

## What NOT to Do

- Do NOT bypass the circuit breaker — all calls through `LlmCircuitBreakerActor`
- Do NOT build a separate prompt template engine — inline prompt construction is correct
- Do NOT store L3 explanations in Redis — they're personalized, ephemeral, one-time-use
- Do NOT deliver L3 when `ConfusionState == ConfusionResolving`
- Do NOT call L3 when ScaffoldingLevel == None (student doesn't need explanation)

---

## Verification Checklist

- [ ] Wrong answer with high backspace count → explanation acknowledges uncertainty
- [ ] Wrong answer in Socratic mode → explanation asks guiding questions, does NOT reveal answer
- [ ] Wrong answer in WorkedExample mode → step-by-step solution provided
- [ ] `ConfusionResolving` → L3 suppressed, L2 used instead
- [ ] Exhaust daily token budget → fallback to L2, `budget_exhausted` metric emitted
- [ ] Circuit breaker open → fallback to L2/L1
- [ ] Hebrew explanation generated for Hebrew question
- [ ] Arabic explanation generated for Arabic question
- [ ] L3 never called when ScaffoldingLevel == None
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
