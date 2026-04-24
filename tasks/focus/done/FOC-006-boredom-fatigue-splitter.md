# FOC-006: Boredom-Fatigue Splitter

**Priority:** P1 — boredom and fatigue need opposite interventions
**Blocked by:** FOC-001 (focus pipeline), FOC-005 (confusion discriminator)
**Estimated effort:** 3-5 days
**Contract:** Extends `FocusDegradationService` focus levels

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Baker et al. (2010): "Better to be frustrated than bored." Bored students learn LESS than frustrated ones because boredom leads to total disengagement.

Pekrun's Control-Value Theory (2006): Boredom arises when students lack control OR can't see task value. Fatigue arises when cognitive resources are depleted.

**Critical problem:** Cena's `FocusLevel` treats Drifting → Fatigued → Disengaged as a severity continuum. But boredom and fatigue require OPPOSITE interventions:
- **Fatigued** → take a break, rest
- **Bored** → increase challenge, change topic, add game elements

## Subtasks

### FOC-006.1: Boredom vs Fatigue Classifier
**Files:**
- `src/Cena.Actors/Services/DisengagementClassifier.cs` — NEW

**Acceptance:**
- [ ] `IDisengagementClassifier.Classify(DisengagementInput) → DisengagementType`
- [ ] **Boredom signals:**
  - Fast, correct answers (too easy) + declining engagement rate
  - Decreasing RT (rushing through) with high accuracy (not challenged)
  - Low hint usage (doesn't need help, material is trivial)
  - Increasing app backgrounding (seeking stimulation elsewhere)
- [ ] **Fatigue signals:**
  - Increasing RT with declining accuracy (cognitive depletion)
  - High vigilance time (>20 min without break)
  - Decreasing touch pattern consistency (motor fatigue)
  - Session count today >= 3 AND late in session
- [ ] `DisengagementType`: `Bored_TooEasy`, `Bored_NoValue`, `Fatigued_Cognitive`, `Fatigued_Motor`, `Mixed`, `Unknown`

### FOC-006.2: Extended FocusLevel Enum
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify

**Acceptance:**
- [ ] `FocusLevel` gains: `DisengagedBored` and `DisengagedExhausted` (replacing single `Disengaged`)
- [ ] `Drifting` remains as-is (early stage — not yet classified)
- [ ] `Fatigued` now specifically means cognitive/physical fatigue (not boredom)
- [ ] Classification at Drifting/below uses `DisengagementClassifier` to split
- [ ] Existing consumers of `FocusLevel.Disengaged` handle both new variants via pattern matching

### FOC-006.3: Differentiated Interventions
**Files:**
- `src/Cena.Actors/Services/FocusDegradationService.cs` — modify `RecommendBreak()`

**Acceptance:**
- [ ] `DisengagedBored` → increase difficulty, switch to challenge mode, add competition element, change topic. Do NOT suggest a break (breaks won't help boredom).
- [ ] `DisengagedExhausted` → suggest break (existing logic), recommend physical activity, end session if severe
- [ ] `BreakRecommendation` gains `AlternativeAction` for non-break interventions: `IncreaseDifficulty`, `ChangeTopic`, `AddChallenge`, `EnableCompetition`
- [ ] Break message for bored students: "מגיע לך אתגר! בוא ננסה משהו קשה יותר" ("You deserve a challenge! Let's try something harder")

**Test:**
```csharp
[Fact]
public void BoredStudent_GetsChallenge_NotBreak()
{
    var state = new FocusState(FocusScore: 0.15, Level: FocusLevel.DisengagedBored, ...);
    var rec = service.RecommendBreak(state, timeContext);
    Assert.False(rec.ShouldBreak); // NO break for boredom
    Assert.Equal(AlternativeAction.IncreaseDifficulty, rec.AlternativeAction);
}

[Fact]
public void ExhaustedStudent_GetsBreak()
{
    var state = new FocusState(FocusScore: 0.15, Level: FocusLevel.DisengagedExhausted, ...);
    var rec = service.RecommendBreak(state, timeContext);
    Assert.True(rec.ShouldBreak);
    Assert.True(rec.Minutes >= 15);
}
```

## Research References
- Baker et al. (2010): "Better to Be Frustrated than Bored"
- Pekrun (2006): Control-Value Theory of achievement emotions
- Focus Degradation Research doc, Section 2.11
