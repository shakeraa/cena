# TASK-SAI-04: L3 Personalized Explanation Generation

**Priority**: HIGH — strongest learning intervention (~0.3-0.5 sigma)
**Effort**: 3-4 days
**Depends on**: TASK-SAI-00 (LLM client), TASK-SAI-03 (L2 cache for writeback)
**Track**: C (sequential after TASK-SAI-03)

---

## Context

L3 is the fallback when L2 cache misses — a novel error pattern the system hasn't seen before. It generates a **fully personalized explanation** using the student's mastery state, active methodology, Bloom's level, recent errors, and behavioral signals.

After generation, the explanation is classified and written back to L2 cache, so the next student with the same misconception gets a cache hit.

### Existing Services That Feed Into L3

| Service | Data Provided | File |
|---------|--------------|------|
| `ScaffoldingService` | Support level: Full/Partial/HintsOnly/None | `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` |
| `MethodologyResolver` | Active methodology for this concept | Actor state |
| `ConceptMasteryState` | Per-concept mastery probability, Bloom's level | Student aggregate |
| `ConfusionDetector` | Confusion state (don't explain if resolving) | `src/actors/Cena.Actors/Services/ConfusionDetector.cs` |
| `CognitiveLoadService` | Fatigue level (shorter explanations if fatigued) | `src/actors/Cena.Actors/Services/CognitiveLoadService.cs` |
| `BusConceptAttempt` | BackspaceCount, AnswerChangeCount (uncertainty) | `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` |
| `IConceptGraphCache` | Prerequisite concepts | Singleton |
| `PublishedQuestion` | Stem, options, DistractorRationale, Explanation | In-memory pool |

---

## Architecture

### Input Assembly

**Create**: `src/actors/Cena.Actors/Services/L3ExplanationContextBuilder.cs`

Assembles all available context into a single object for prompt construction:

```csharp
public sealed record L3Context(
    // Question
    string QuestionStem,
    string CorrectAnswer,
    string CorrectOptionRationale,
    string StudentAnswer,
    string? DistractorRationale,       // why the chosen answer is wrong
    string ErrorType,
    int QuestionBloomsLevel,
    string Subject,
    string Topic,

    // Student state
    double ConceptMastery,              // 0.0-1.0
    int StudentBloomsLevel,             // achieved level for this concept
    ScaffoldingLevel ScaffoldLevel,     // Full/Partial/HintsOnly/None
    string ActiveMethodology,           // Socratic/Feynman/WorkedExample/etc.
    string Language,                    // he/ar/en

    // Behavioral signals
    int HintCountUsed,
    int BackspaceCount,
    int AnswerChangeCount,
    double ResponseTimeMs,
    CognitiveLoadLevel FatigueLevel,    // Low/Moderate/High/Critical

    // History
    IReadOnlyList<string> RecentErrorTypes,       // last 3 errors on this concept
    IReadOnlyList<string> PrerequisiteConceptNames // from concept graph
);
```

### Prompt Construction

**Create**: `src/actors/Cena.Actors/Services/L3PromptBuilder.cs`

Builds the LLM prompt from `L3Context`. The prompt must:

1. **Set the pedagogical frame** based on methodology:
   - Socratic: "Ask 1-2 guiding questions that lead the student to discover the error. Never state the answer directly."
   - WorkedExample: "Show a step-by-step solution to a similar problem, then ask the student to apply the same approach."
   - Feynman: "Ask the student to explain the concept in simple terms, then point out where their reasoning diverges."
   - DrillAndPractice: "Give a brief, direct correction in 1-2 sentences."
   - Default: "Provide a clear, concise explanation appropriate for the student's level."

2. **Calibrate depth** based on Bloom's level:
   - Level 1-2 (Remember/Understand): "Use simple language. Define key terms."
   - Level 3-4 (Apply/Analyze): "Explain the relationship between concepts. Show how to apply the rule."
   - Level 5-6 (Evaluate/Create): "Challenge the student to evaluate why this approach fails and propose an alternative."

3. **Adjust length** based on fatigue:
   - Low/Moderate: Standard (3-5 sentences)
   - High: Brief (1-2 sentences)
   - Critical: Minimal ("The key issue is: {one sentence}. Consider taking a break.")

4. **Incorporate behavioral signals**:
   - High `AnswerChangeCount` (>2): "It seems you were deciding between options. Let's clarify the difference between..."
   - High `BackspaceCount` (>5): "You seemed uncertain in your answer. The core question to ask yourself is..."
   - Recent repeated errors on same concept: "This is a concept that takes practice. Let's approach it from a different angle..."

5. **Reference prerequisites** if mastery is low on them.

6. **Generate in the student's language** (he/ar/en).

### Rate Limiting

- Max 3 L3 calls per student per minute (via `LlmCircuitBreakerActor` per-student bucket)
- Max 50 L3 calls per student per day
- If rate exceeded: fall back to L1 static explanation

### Cache Writeback

After L3 generation, classify the explanation and write it back to L2:

```csharp
// If the error can be generalized to an ErrorType, cache it
if (errorType != "unknown")
{
    await _explanationCache.SetAsync(questionId, errorType, methodology, new CachedExplanation(
        explanation.Text, explanation.ModelId, explanation.TokenCount, DateTimeOffset.UtcNow), ct);
}
```

This means L3 is self-healing: every novel error pattern it handles becomes an L2 cache entry for future students.

---

## Wire Into ExplanationOrchestrator

**Modify**: `src/actors/Cena.Actors/Services/ExplanationOrchestrator.cs` (created in TASK-SAI-03)

Replace the L3 placeholder with the real implementation:

```csharp
// L3: generate on-demand with full student context
var l3Context = _contextBuilder.Build(req, studentState, behavioralSignals);
var l3 = await _generator.GenerateL3Async(l3Context, ct);

// Write back to L2 cache
await _cache.SetAsync(req.Question.ItemId, req.ErrorType, req.Methodology,
    new CachedExplanation(l3.Text, l3.ModelId, l3.TokenCount, DateTimeOffset.UtcNow), ct);

return l3.Text;
```

---

## Coding Standards

- `L3ExplanationContextBuilder` is a pure assembly function — no LLM calls, no I/O. It collects data from existing services.
- `L3PromptBuilder` is a pure function — takes `L3Context`, returns `string` (the prompt). No I/O. Trivially testable.
- The LLM call itself goes through `ILlmClient` (TASK-SAI-00) — never call APIs directly.
- Prompt must NEVER include `StudentId` or any PII. Mastery level and behavioral signals are anonymous.
- Log at `Information`: L3 generation triggered, model used, token count, latency.
- Log at `Warning`: rate limit hit, fallback to L1.
- Store generation metadata as a Marten document (`ExplanationGenerationLog`) for cost tracking and prompt improvement.

---

## Acceptance Criteria

1. L3 generates personalized explanations using full student context (mastery, methodology, Bloom's, fatigue, behavioral signals)
2. Explanations are methodology-aware (Socratic asks questions, WorkedExample shows steps)
3. Explanations are Bloom's-calibrated (simpler at level 1-2, more analytical at 5-6)
4. Explanations shorten when student is fatigued (CognitiveLoadLevel.High/Critical)
5. Behavioral signals (BackspaceCount, AnswerChangeCount) influence explanation content
6. Generated in student's preferred language (he/ar/en)
7. L3 results written back to L2 cache (self-healing cache)
8. Rate limited: max 3/min and 50/day per student
9. Rate limit exceeded: graceful fallback to L1 static explanation
10. No PII in LLM prompts
11. `ExplanationGenerationLog` persisted for cost/quality tracking
