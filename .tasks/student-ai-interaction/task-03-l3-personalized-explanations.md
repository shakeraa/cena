# Task 03: L3 Personalized Explanation Generation

**Track**: C (after Task 02)
**Effort**: 3-4 days
**Depends on**: Task 02 (L2 cache), ScaffoldingService (exists), MethodologyResolver (exists)
**Blocks**: Task 04

---

## System Context

Cena is an event-sourced .NET educational platform. After Tasks 00-02, the system has real LLM calls and a Redis-cached L2 explanation layer keyed by `(questionId, errorType, language)`. L2 covers ~80-90% of cases with generic error-type explanations.

This task builds L3: when L2 is insufficient (novel error pattern, Transfer/Systematic errors, or high-uncertainty behavioral signals), generate an explanation personalized to the student's full learning context. L3 explanations are ephemeral — they are not cached because they incorporate transient student state.

The system already has rich per-student context available in the actor model:
- `ConceptMasteryState`: BKT probability, Bloom level, recent errors, quality quadrant, attempt counts
- `ScaffoldingService`: Full/Partial/HintsOnly/None based on effective mastery + PSI
- `MethodologyResolver`: 5-layer hierarchical resolution of active methodology (9 methodologies)
- `ConfusionDetector`: 4-state machine (NotConfused/Confused/ConfusionResolving/ConfusionStuck)
- `DisengagementClassifier`: Bored_TooEasy vs Fatigued_Cognitive (opposite interventions)
- Behavioral signals: `BackspaceCount`, `AnswerChangeCount` from `BusConceptAttempt`

The routing config (`contracts/llm/routing-config.yaml`) defines:
- `answer_evaluation` task → Sonnet 4.6, temp 0.3
- `socratic_question` task → Sonnet 4.6, temp 0.4
- Prompt caching: student context uses `cache_control: { type: "ephemeral" }` with 5-min TTL

---

## Mandatory Pre-Read

| File | Line(s) | What to look for |
|------|---------|-----------------|
| `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` | Full | L2 cache service from Task 02 — L3 is called when L2 misses or is insufficient |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | 18-50 | `DetermineLevel()` and `GetScaffoldingMetadata()` — mastery → scaffolding level mapping |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | 34-72 | `Detect()` — state machine. L3 should skip if `ConfusionResolving` |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | 26-92 | `Classify()` — affects explanation tone and verbosity |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | Find `BusConceptAttempt` | `BackspaceCount`, `AnswerChangeCount` — uncertainty indicators for prompt |
| `contracts/llm/routing-config.yaml` | 111-201 | `answer_evaluation` and `socratic_question` task configs |
| `contracts/llm/routing-config.yaml` | 343-383 | Prompt caching config — student context caching strategy |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Full | Integration point — where to call L3 after L2 |
| `src/actors/Cena.Actors/Gateway/LlmCircuitBreakerActor.cs` | 34-59 | Circuit breaker — all calls through this |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | Full | All per-concept signals available for prompt context |

---

## Implementation Requirements

### 1. Student Context Assembly

Create a `PersonalizedExplanationService` in `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs`.

The core job is assembling a student context block for the LLM prompt. All data is already available in the actor model:

```csharp
public sealed record StudentExplanationContext(
    // From ConceptMasteryState
    float MasteryProbability,           // P_L from BKT
    int BloomLevel,                     // 0-6
    string QualityQuadrant,             // Mastered/Effortful/Careless/Struggling
    IReadOnlyList<string> RecentErrors, // Last 10 ErrorType values
    int AttemptCount,
    int CurrentStreak,

    // From ScaffoldingService
    string ScaffoldingLevel,            // Full/Partial/HintsOnly/None

    // From MethodologyResolver
    string ActiveMethodology,           // Socratic/Feynman/WorkedExample/etc.

    // From ConfusionDetector
    string ConfusionState,              // NotConfused/Confused/ConfusionResolving/ConfusionStuck

    // From DisengagementClassifier
    string DisengagementType,           // Bored_TooEasy/Fatigued_Cognitive/etc.

    // From behavioral signals
    int BackspaceCount,                 // Uncertainty indicator
    int AnswerChangeCount,              // Option confusion indicator
    int HintsUsed,                      // How many hints before answering

    // From question
    string ErrorType,                   // From L2 classification
    string Language                     // he/ar/en
);
```

### 2. Methodology-Aware Prompt Construction

The active methodology MUST control the explanation style. This is non-negotiable — the methodology system is a core pedagogical design decision.

| Methodology | Explanation Style | Prompt Instruction |
|-------------|------------------|-------------------|
| Socratic | Guided questions, never reveals answer | "Ask 2-3 guiding questions that lead the student to discover the answer. Do NOT reveal the answer directly." |
| DirectInstruction | Step-by-step solution | "Explain the solution step-by-step, clearly showing each reasoning step." |
| WorkedExample | Analogous solved problem | "Show a similar but different problem with its complete solution, then ask the student to apply the same approach to the original." |
| Feynman | Teach-back prompts | "Ask the student to explain the concept in simple terms. Then identify the gap in their explanation." |
| RetrievalPractice | Recall prompts | "Before explaining, ask the student what they remember about [concept]. Then fill the gaps." |
| SpacedRepetition | Brief reinforcement | "Give a concise refresher on [concept] — this student knew it before but memory has decayed." |
| Analogy | Comparison | "Explain using an analogy to [prerequisite concept] that the student has already mastered." |
| DrillAndPractice | Procedural correction | "Show the correct procedure step by step. Highlight where the student's execution went wrong." |
| BloomsProgression | Level-appropriate | Adjust depth to current Bloom level (1-2: state facts; 3-4: explain relationships; 5-6: ask to evaluate) |

### 3. Confusion and Engagement Gating

Before generating L3:
- If `ConfusionState == ConfusionResolving`: do NOT generate L3. Return L2 only. The student is in productive struggle — a full explanation would short-circuit learning.
- If `DisengagementType == Bored_TooEasy`: use terse explanation, don't over-explain. Student is bored, not confused.
- If `DisengagementType == Fatigued_Cognitive`: use simpler language, shorter explanation, more concrete examples.

### 4. Prompt Caching

Use Anthropic's prompt caching per `routing-config.yaml` section 6:
- System prompt (methodology instructions, format rules): cached with `cache_control: { type: "ephemeral" }`
- Student context block: cached with 5-min TTL. The student context changes per-session but not per-question. This saves ~60% of input tokens on consecutive questions.
- Question-specific content: not cached (changes every question)

### 5. Cost Guard

Before making an L3 call, check the per-student daily token budget:
- `daily_output_token_limit: 25000` (from routing config)
- Track cumulative tokens per student per day (in-memory counter on `StudentActor`, reset on passivation with daily check)
- If budget exhausted: serve L2 cached explanation instead. Log the degradation.

### 6. Fallback Chain

```
L3 personalized → fails (circuit breaker, timeout, budget)
  → L2 cached (Redis) → fails (Redis down)
    → L1 static (from question aggregate) → null
      → Generic message: "Review [concept name] and try again."
```

Never leave the student with no explanation. The fallback chain must be robust.

### 7. Integration with LearningSessionActor

Extend the answer evaluation flow from Task 02:

```
If incorrect:
  1. Classify error type (existing)
  2. Check L2 cache (ExplanationCacheService from Task 02)
  3. If L2 hit AND no high-uncertainty signals → return L2 (fast path)
  4. If L2 miss OR high uncertainty (backspaceCount > 5 || answerChangeCount > 2) → call L3
  5. If L3 succeeds → return personalized explanation
  6. If L3 fails → fall back to L2 or L1
```

The decision to escalate from L2 to L3 should be deliberate, not default. L3 is expensive. Use it when the student demonstrates genuine confusion, not for every wrong answer.

---

## What NOT to Do

- Do NOT bypass the circuit breaker — all LLM calls through `LlmCircuitBreakerActor`
- Do NOT build a separate prompt template system — inline prompt construction with assembled context
- Do NOT store personalized explanations in Redis — they're ephemeral, one-time
- Do NOT call L3 for every wrong answer — only when L2 is insufficient or uncertainty signals are high
- Do NOT create a new actor for this — it's a service called by `LearningSessionActor`
- Do NOT modify the methodology definitions — consume them as-is

---

## Verification Checklist

- [ ] Wrong answer with high backspace count → L3 triggered, explanation acknowledges uncertainty
- [ ] Wrong answer in Socratic mode → explanation asks guiding questions, does NOT reveal answer
- [ ] Wrong answer in WorkedExample mode → explanation shows analogous solved problem
- [ ] `ConfusionState == ConfusionResolving` → L3 NOT triggered, L2 served instead
- [ ] `DisengagementType == Bored_TooEasy` → terse L3 explanation
- [ ] `DisengagementType == Fatigued_Cognitive` → simpler language, shorter explanation
- [ ] Daily token budget exhausted → fallback to L2 with log entry
- [ ] Circuit breaker open → graceful fallback to L2/L1
- [ ] Prompt caching active (verify via Anthropic response headers: `cache_creation_input_tokens`, `cache_read_input_tokens`)
- [ ] Hebrew/Arabic explanations with correct RTL
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes
