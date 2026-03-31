# MOB-048: Teach-Back Mode — Student Explanations

**Priority:** P4.2 — High
**Phase:** 4 — Advanced Intelligence (Months 8-12)
**Source:** learning-science-srs-research.md Section 5
**Blocked by:** MOB-036 (SRSActor), LLM-* (LLM Layer)
**Estimated effort:** L (3-6 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Context

Elaborative interrogation — explaining concepts in your own words — produces deeper understanding than passive review (Chi et al., 2014). The "Feynman Technique" applied to mobile.

## Subtasks

### MOB-048.1: Teach-Back Prompt
- [ ] After mastering a concept (P(known) > 0.85): "Can you explain this to a classmate?"
- [ ] Free-text input with voice-to-text option
- [ ] Appears 1-2x per session maximum (not every mastered concept)
- [ ] Skippable — no penalty for declining

### MOB-048.2: LLM Evaluation
- [ ] Student explanation evaluated by LLM (Claude on Haiku tier)
- [ ] Rubric: completeness, accuracy, clarity
- [ ] Feedback: "Great explanation! You covered X and Y. Consider also mentioning Z."
- [ ] 2.5x XP bonus for teach-back completion

### MOB-048.3: Peer Viewing
- [ ] Verified explanations available as peer solutions (after teacher approval for under-16)
- [ ] "See how others explained it" button on concept detail
- [ ] Anonymous display (author shown as "A classmate")

### MOB-048.4: Events
- [ ] `TeachBackSubmitted_V1`: `{StudentId, ConceptId, ExplanationText, WordCount}`
- [ ] `TeachBackEvaluated_V1`: `{ConceptId, CompletenessScore, AccuracyScore, XpAwarded}`

**Definition of Done:**
- Teach-back prompt appears after concept mastery
- LLM evaluates explanation quality with constructive feedback
- 2.5x XP bonus incentivizes participation
- Verified explanations available as peer solutions

**Test:**
```csharp
[Fact]
public async Task TeachBack_EvaluatesExplanation()
{
    var evaluator = new TeachBackEvaluator(llmGateway);
    var result = await evaluator.Evaluate(
        conceptId: "pythagorean-theorem",
        explanation: "In a right triangle, the square of the hypotenuse equals the sum of squares of the other two sides."
    );
    Assert.True(result.Accuracy >= 0.8);
    Assert.True(result.Completeness >= 0.7);
    Assert.True(result.XpAwarded > 0);
}
```
