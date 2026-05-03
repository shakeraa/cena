# SAI-004: L3 Personalized Explanation Generation

**Priority:** P1 — handles L2 cache misses with full student context
**Blocked by:** SAI-003 (L2 cache and ExplanationResolver)
**Estimated effort:** 3-4 days
**Stack:** .NET 9, Proto.Actor, LLM ACL (gRPC), Redis

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

L2 explanations are keyed on `(questionId, ErrorType)` and shared across students. L3 adds full student context — mastery level, prerequisite readiness, confusion state, behavioral signals, method history — to generate a truly personalized explanation. L3 fires only when L2 misses (novel error pattern, Transfer/Systematic errors, or first-encounter questions).

L3 explanations are expensive (~500ms-2s, ~$0.001/call) but rare with a warm L2 cache (expected <10% of explanations). When L3 generates a classifiable explanation, it caches back to L2 for future students.

### Key Files (Read ALL Before Starting)

| File | Why |
|------|-----|
| `src/actors/Cena.Actors/Explanations/ExplanationResolver.cs` | From SAI-003 — add L3 as the final generation layer |
| `src/actors/Cena.Actors/Mastery/ConceptMasteryState.cs` | Full per-concept state: BKT, HLR, Bloom, errors, quality quadrant, method history |
| `src/actors/Cena.Actors/Mastery/PrerequisiteSatisfactionIndex.cs` | PSI computation — prerequisite readiness |
| `src/actors/Cena.Actors/Mastery/ScaffoldingService.cs` | Scaffolding level — determines explanation depth |
| `src/actors/Cena.Actors/Methodology/MethodologyResolver.cs` | Active methodology — determines tone |
| `src/actors/Cena.Actors/Services/ConfusionDetector.cs` | ConfusionState — gate L3 delivery during ConfusionResolving |
| `src/actors/Cena.Actors/Services/DisengagementClassifier.cs` | DisengagementType — suppress for Bored_TooEasy |
| `src/actors/Cena.Actors/Services/FocusDegradationService.cs` | FocusLevel — adjust verbosity |
| `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` | BusConceptAttempt: BackspaceCount, AnswerChangeCount |

## Subtasks

### SAI-004.1: L3 Explanation Request Builder

**Files to create:**
- `src/actors/Cena.Actors/Explanations/L3ExplanationRequest.cs`
- `src/actors/Cena.Actors/Explanations/L3RequestBuilder.cs`

**Implementation:**

Build the full student context for LLM prompt construction:

```csharp
public sealed record L3ExplanationRequest(
    // Question context
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    ErrorType ErrorType,

    // Student mastery context
    float MasteryProbability,
    float RecallProbability,
    int BloomLevel,
    float PSI,
    ErrorType[] RecentErrors,
    MasteryQuality QualityQuadrant,

    // Instructional context
    ScaffoldingLevel ScaffoldingLevel,
    Methodology ActiveMethodology,
    MethodAttempt[] MethodHistory,

    // Affect context
    FocusLevel FocusLevel,
    DisengagementType? DisengagementType,
    ConfusionState ConfusionState,

    // Behavioral signals
    int BackspaceCount,
    int AnswerChangeCount,
    float ResponseTimeMs,
    float MedianResponseTimeMs);
```

`L3RequestBuilder` collects this from `StudentActor` state, `LearningSessionActor` state, and the current attempt payload. It is a pure function — no I/O.

**Acceptance:**
- [ ] All fields populated from existing state (no new data sources needed)
- [ ] Builder is a static method — no allocation beyond the record itself
- [ ] Null-safe: missing ConceptMasteryState (first encounter) uses safe defaults
- [ ] PSI computed via `PrerequisiteSatisfactionIndex.Compute()` (already exists)

---

### SAI-004.2: L3 Generation with Affect-Aware Gating

**Files to create:**
- `src/actors/Cena.Actors/Explanations/L3ExplanationGenerator.cs`

**Implementation:**

Before calling LLM, apply affect-aware gates:

```csharp
public async Task<string?> GenerateAsync(L3ExplanationRequest req, CancellationToken ct)
{
    // Gate 1: Don't interrupt productive struggle
    if (req.ConfusionState == ConfusionState.ConfusionResolving)
        return null; // Let ConfusionResolutionTracker patience window expire first

    // Gate 2: Don't over-explain to bored students
    if (req.DisengagementType == DisengagementType.Bored_TooEasy)
        return null; // QuestionSelector should increase difficulty, not explain

    // Gate 3: Adjust verbosity for fatigued students
    int maxTokens = req.FocusLevel switch
    {
        FocusLevel.Flow or FocusLevel.Engaged => 500,
        FocusLevel.Drifting => 300,
        FocusLevel.Fatigued => 200,
        _ => 150 // Disengaged — shortest possible
    };

    // Build LLM prompt with full context
    var prompt = BuildPrompt(req, maxTokens);

    // Call LLM ACL via gRPC (with circuit breaker check)
    return await CallLlmAclAsync(prompt, maxTokens, ct);
}
```

**Prompt construction:**
- Include student mastery level, prerequisite readiness, error history
- Include methodology constraint as a system-level instruction
- Include scaffolding depth as a format constraint
- If `BackspaceCount > 5` or `AnswerChangeCount > 2`: acknowledge uncertainty ("I see you reconsidered your answer — let's look at why both options seemed plausible")
- If `ResponseTimeMs > 2 * MedianResponseTimeMs`: acknowledge effort ("You took time to think this through...")
- Language: Hebrew or Arabic based on question language (from `PublishedQuestion.Language`)

**Acceptance:**
- [ ] ConfusionResolving gate: returns null, no LLM call
- [ ] Bored_TooEasy gate: returns null, no LLM call
- [ ] Verbosity scales with FocusLevel (500 tokens for Flow, 150 for Disengaged)
- [ ] Behavioral signals acknowledged in explanation when above thresholds
- [ ] Methodology constraint enforced in system prompt
- [ ] Hebrew/Arabic language support via question language field
- [ ] Circuit breaker checked before LLM call
- [ ] OpenTelemetry span: `cena.explanation.generate_l3` with `methodology`, `scaffolding_level`, `gated_reason` attributes
- [ ] Counter: `cena.explanation.l3_gated_total` with `reason` tag (confusion_resolving, bored, none)

---

### SAI-004.3: Integrate L3 into ExplanationResolver

**Files to modify:**
- `src/actors/Cena.Actors/Explanations/ExplanationResolver.cs` (from SAI-003)

**Implementation:**

Extend the resolution pipeline to include L3 after L2 miss:

```
L2 cache → L1 static → L2 generate → L3 personalized → fallback
```

L3 is attempted when:
- L2 generation returned null (circuit breaker open, or error type is novel)
- AND affect gates pass (not ConfusionResolving, not Bored)

When L3 generates and the ErrorType is classifiable (not Transfer/Systematic), cache back to L2:
```csharp
if (errorType is not (ErrorType.Transfer or ErrorType.Systematic))
    _ = _cache.SetL2Async(questionId, errorType, generated, ct);
```

**Acceptance:**
- [ ] L3 fires only after L2 miss
- [ ] L3 result cached back to L2 when ErrorType is classifiable
- [ ] Transfer and Systematic errors never cached to L2 (too context-dependent)
- [ ] Metrics: `cena.explanation.resolved_total` with `source=l3_personalized` tag
- [ ] Total explanation resolution latency tracked: `cena.explanation.resolve_duration_ms`

---

### SAI-004.4: Per-Student Rate Limiting

**Files to modify:**
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`

**Implementation:**

Track LLM generation calls per student per session (sliding window):

```csharp
private int _llmCallsThisMinute;
private DateTimeOffset _llmWindowStart;

private bool CanCallLlm()
{
    if (DateTimeOffset.UtcNow - _llmWindowStart > TimeSpan.FromMinutes(1))
    {
        _llmCallsThisMinute = 0;
        _llmWindowStart = DateTimeOffset.UtcNow;
    }
    return _llmCallsThisMinute < 3; // Max 3 per minute
}
```

When rate limit exceeded, skip L3 generation entirely (fall back to L1).

**Acceptance:**
- [ ] Max 3 LLM calls per student per minute
- [ ] Window resets after 1 minute of no calls
- [ ] Rate limit exceeded logged at Warning level (not Error)
- [ ] Counter: `cena.explanation.rate_limited_total`

---

## Testing

```csharp
[Fact]
public async Task L3_SkippedDuringConfusionResolving()
{
    var generator = CreateL3Generator();
    var request = CreateRequest(confusionState: ConfusionState.ConfusionResolving);

    var result = await generator.GenerateAsync(request, CancellationToken.None);

    Assert.Null(result); // Gated — don't interrupt productive struggle
}

[Fact]
public async Task L3_ShorterForFatiguedStudents()
{
    // FocusLevel.Fatigued → maxTokens=200
    // FocusLevel.Flow → maxTokens=500
    // Verify prompt includes different maxTokens
}

[Fact]
public async Task L3_AcknowledgesUncertainty()
{
    var request = CreateRequest(backspaceCount: 8, answerChangeCount: 3);
    var result = await generator.GenerateAsync(request, CancellationToken.None);

    // Explanation should acknowledge the student reconsidered their answer
    Assert.Contains("reconsidered", result, StringComparison.OrdinalIgnoreCase);
}
```
