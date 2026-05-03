# Difficulty-Aware Response Pipeline

**Date**: 2026-03-30
**Status**: Proposal
**Author**: Architecture Review
**Classification**: Enhancement to Student AI Interaction layer

---

## 1. The Gap

Cena's question selection is difficulty-aware — `QuestionSelector` uses ZPD targeting to serve questions at `[mastery - 0.15, mastery + 0.25]` with focus-state adaptation. This is excellent.

But once a question is served and the student gets it wrong, **the response pipeline loses the difficulty signal**. The hint, explanation, and tutoring services know the student's mastery and scaffolding level, but not how hard the specific question was relative to their ability.

### What the system knows at response time

| Signal | Available? | Used? |
|--------|-----------|-------|
| Student mastery P(L) per concept | Yes | Yes — drives scaffolding, BKT, methodology |
| Bloom's level achieved | Yes | Yes — drives question selection, explanation depth |
| Error type (conceptual, procedural, etc.) | Yes | Yes — drives L2 cache key, explanation framing |
| Confusion state | Yes | Yes — gates hint delivery |
| Focus/fatigue state | Yes | Yes — adjusts question difficulty, hint complexity |
| Active methodology | Yes | Yes — drives explanation tone |
| **Question intrinsic difficulty (0.0-1.0)** | **Yes (on PublishedQuestion)** | **No — not passed to any response service** |

### Why it matters

The **difficulty gap** = `questionDifficulty - masteryProbability`:

| Gap | Meaning | Correct Response | Current Response |
|-----|---------|-----------------|-----------------|
| > +0.3 | Stretch question — student attempted above level | "This was a hard one — let's break it down together" | Same as any error |
| -0.1 to +0.3 | ZPD-appropriate — expected challenge | Normal explanation | Normal explanation (correct) |
| < -0.2 | Regression — question was below level | "You should know this — let's check what went wrong" | Same as any error |

Without the difficulty signal, a student who fails a 0.9-difficulty stretch question gets the same explanation depth and tone as one who fails a 0.3-difficulty question they should have gotten right. The first case calls for encouragement; the second calls for investigation.

---

## 2. Research Basis

- **Bjork (2011) Desirable Difficulty**: Difficulty that exceeds current ability but is achievable with effort produces the deepest learning. The system should acknowledge when a question was a stretch — this frames the error as productive, not as failure.
- **Dweck (2006) Growth Mindset**: Framing difficulty as a learning opportunity ("this was a challenging one") vs. a deficiency ("you got this wrong") significantly affects student motivation and persistence. The difficulty gap determines which framing is appropriate.
- **Vygotsky's ZPD**: The QuestionSelector already targets the ZPD. But the response pipeline should also know whether the question was at the bottom (easy end), center (optimal), or top (stretch end) of the student's ZPD — each warrants different scaffolding.
- **Rohrer et al. (2015)**: Interleaved practice with varying difficulty produces better transfer. When the system explains a hard question, it should explicitly connect to easier versions of the same concept the student has mastered.

---

## 3. Design: Thread Difficulty Through 4 Services

### 3.1 Compute Difficulty Gap (shared utility)

```csharp
/// <summary>
/// Computes the gap between question difficulty and student mastery.
/// Positive = stretch (question harder than ability).
/// Negative = regression (question easier than ability).
/// Near zero = ZPD-appropriate.
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
}

public enum DifficultyFrame
{
    Stretch,      // Well above ability — encourage, don't judge
    Challenge,    // Above ability — normal productive struggle
    Appropriate,  // ZPD center — standard explanation
    Expected,     // Below ability — should have gotten right
    Regression    // Well below ability — investigate prerequisites
}
```

### 3.2 L2 Explanation Cache (`ExplanationCacheService`)

Add difficulty framing to the **generation prompt** (not the cache key — difficulty is static per question):

```
Stretch/Challenge → "This was a challenging question. The student attempted above their current level."
Appropriate       → (no special framing)
Expected          → "This question should be within the student's ability. Focus on identifying the specific gap."
Regression        → "This question is below the student's demonstrated level. Check if a prerequisite concept has decayed."
```

### 3.3 L3 Personalized Explanations (`PersonalizedExplanationService`)

Include `difficultyGap` and `difficultyFrame` in the prompt context. The LLM uses this to:

- **Stretch**: Lead with encouragement, then explain. "This was a hard one — here's the approach."
- **Regression**: Lead with diagnostic question. "You've shown you know this — what tripped you up?"
- **Appropriate**: Standard methodology-driven explanation (no change from current behavior).

Also adjust `DetermineMaxTokens`: stretch questions deserve longer explanations (student is learning something new); regression questions need shorter, more targeted responses (student knows the concept, just needs a nudge).

### 3.4 Hint Generation (`HintGenerationService`)

Adjust hint specificity based on difficulty:

| Difficulty Frame | Level 1 (Nudge) | Level 2 (Scaffold) | Level 3 (Reveal) |
|-----------------|-----------------|-------------------|-----------------|
| **Stretch** | Point to the prerequisite that bridges the gap | Give a more specific procedural hint than usual | Full worked example (justified — student is learning) |
| **Appropriate** | Standard nudge | Standard scaffold | Standard reveal |
| **Regression** | Point to the prerequisite that may have decayed | Minimal — student should be able to self-correct | Abbreviated (student knows this, just needs reminder) |

### 3.5 TutorActor

Include difficulty context in the RAG prompt so the tutor calibrates expectations:

```
"The question that triggered this tutoring session was rated {difficultyLabel}
(difficulty {questionDifficulty:F2} vs student mastery {masteryProbability:F2},
gap = {difficultyGap:+F2}). Frame your responses accordingly."
```

This prevents the tutor from being condescending about a hard question or overly patient about an easy one.

---

## 4. What NOT to Change

| Component | Why Leave It |
|-----------|-------------|
| **QuestionSelector** | Already handles difficulty via ZPD — no change needed |
| **BKT credit / HintAdjustedBktService** | Hint credit is about hint usage, not question difficulty |
| **ScaffoldingService** | Mastery-driven, not difficulty-driven — correct abstraction level |
| **L2 cache key** | Difficulty is per-question and static — no need to vary the cache key |
| **ConfusionDetector** | Confusion is about behavioral signals, not question difficulty |
| **ErrorClassificationService** | Error type is about what went wrong, not how hard the question was |

---

## 5. Implementation

### Data Flow

```
LearningSessionActor (has PublishedQuestion.Difficulty + ConceptMasteryState.MasteryProbability)
    │
    ├── Compute difficultyGap = question.Difficulty - mastery.MasteryProbability
    ├── Classify frame = DifficultyGap.Classify(gap)
    │
    ├──► ExplanationCacheService.GetOrGenerateAsync(..., questionDifficulty, difficultyFrame)
    │       └── Adds framing to LLM generation prompt
    │
    ├──► PersonalizedExplanationService.GenerateAsync(..., questionDifficulty, difficultyGap)
    │       └── Includes gap in prompt context, adjusts max tokens
    │
    ├──► HintGenerationService.GenerateHint(..., questionDifficulty, difficultyFrame)
    │       └── Adjusts hint specificity per frame
    │
    └──► TutorActor start messages (..., questionDifficulty, difficultyGap)
            └── Includes in RAG prompt for tutor calibration
```

### Files to Modify

| File | Change |
|------|--------|
| `src/actors/Cena.Actors/Services/DifficultyGap.cs` | **NEW** — shared utility (20 lines) |
| `src/actors/Cena.Actors/Services/ExplanationCacheService.cs` | Add `float questionDifficulty` param, include frame in generation prompt |
| `src/actors/Cena.Actors/Services/PersonalizedExplanationService.cs` | Add `float questionDifficulty, float difficultyGap` to context record, use in prompt + max tokens |
| `src/actors/Cena.Actors/Services/HintGenerationService.cs` | Add `float questionDifficulty, DifficultyFrame frame` to `HintGenerationContext`, adjust specificity |
| `src/actors/Cena.Actors/Tutoring/TutorActor.cs` | Add `float questionDifficulty` to start messages, include in prompt |
| `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` | Compute gap at call sites, pass through to services |

**Estimated effort**: 1 day. No new infrastructure, no schema changes, no new actors. One new 20-line utility + parameter additions to 4 existing services.

---

## 6. Measurement

Add to the `sai-explanation-tiers` A/B experiment metrics:

```csharp
float? DifficultyGapAtError = null,      // Gap when explanation was triggered
string? DifficultyFrameAtError = null,   // Stretch/Challenge/Appropriate/Expected/Regression
double? StretchRetrySuccessRate = null,   // Success rate on retry for stretch questions specifically
double? RegressionRecoveryRate = null     // Recovery rate for regression errors
```

**Hypothesis**: Difficulty-aware framing will increase retry success rate for stretch questions by 10-15% (encouragement reduces avoidance) and improve regression recovery by 20-30% (diagnostic framing identifies prerequisite decay faster).
