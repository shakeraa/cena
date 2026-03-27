# MST-002: Bayesian Knowledge Tracing Engine

**Priority:** P0 — core mastery signal at launch
**Blocked by:** MST-001 (ConceptMasteryState)
**Estimated effort:** 3-5 days (M)
**Contract:** `docs/mastery-engine-architecture.md` section 3.1 step 1
**Research ref:** `docs/mastery-measurement-research.md` section 1.1.1

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

BKT is the primary mastery model at launch. It's a hidden Markov model with two states (known/unknown) and four parameters per knowledge component. The update rule runs on every `ConceptAttempted` event inside the `StudentActor` — it must execute in < 1 microsecond (it's on the hot path, no allocation).

## Subtasks

### MST-002.1: BKT Parameters

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/BktParameters.cs`
- `src/Cena.Domain/Learner/Mastery/BktParameterProvider.cs`

**Acceptance:**
- [ ] `BktParameters` record: `P_L0` (prior), `P_T` (transition), `P_S` (slip), `P_G` (guess)
- [ ] Validation: all parameters in [0, 1]; P_S + P_G < 1 (identifiability constraint)
- [ ] `BktParameterProvider` loads per-KC parameters from JSON config (defaults for launch, updated later by MST-015 trainer)
- [ ] Default parameters: P_L0=0.10, P_T=0.20, P_S=0.05, P_G=0.25
- [ ] `BktParameterProvider` implements `IBktParameterProvider` interface for DI

### MST-002.2: BKT Update Rule

**Files to create:**
- `src/Cena.Domain/Learner/Mastery/BktTracer.cs`

**Acceptance:**
- [ ] `BktTracer.Update(float currentP_L, bool isCorrect, BktParameters params) → float`
- [ ] Correct response update: `P(L|correct) = (1-P_S) * P_L / [(1-P_S)*P_L + P_G*(1-P_L)]`
- [ ] Incorrect response update: `P(L|incorrect) = P_S * P_L / [P_S*P_L + (1-P_G)*(1-P_L)]`
- [ ] Learning transition: `P(L_next) = P(L|obs) + (1 - P(L|obs)) * P_T`
- [ ] Output clamped to [0.001, 0.999] to prevent numerical issues
- [ ] Zero allocations on hot path (pure math, no object creation)
- [ ] Method is `static` (no instance state needed)

### MST-002.3: Integration with ConceptMasteryState

**Files to create/modify:**
- `src/Cena.Domain/Learner/Mastery/BktTracer.cs` (add `UpdateState` method)

**Acceptance:**
- [ ] `BktTracer.UpdateState(ConceptMasteryState state, bool isCorrect, BktParameters params) → ConceptMasteryState`
- [ ] Returns new `ConceptMasteryState` with updated `MasteryProbability` via `WithBktUpdate()`
- [ ] Does NOT modify any other fields (HLR, Bloom, errors — those have their own updaters)

**Test:**
```csharp
[Fact]
public void BktUpdate_CorrectAnswer_IncreasesMastery()
{
    var pL = 0.50f;
    var p = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

    var updated = BktTracer.Update(pL, isCorrect: true, p);

    // After correct: posterior should increase
    Assert.True(updated > pL, $"Expected > {pL}, got {updated}");
    // Worked example from research doc:
    // P(L|correct) = 0.95*0.50 / (0.95*0.50 + 0.25*0.50) = 0.475/0.60 = 0.7917
    // P(L_next) = 0.7917 + (1-0.7917)*0.20 = 0.7917 + 0.0417 = 0.8333
    Assert.InRange(updated, 0.82f, 0.84f);
}

[Fact]
public void BktUpdate_IncorrectAnswer_DecreasesMastery()
{
    var pL = 0.80f;
    var p = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

    var updated = BktTracer.Update(pL, isCorrect: false, p);

    Assert.True(updated < pL, $"Expected < {pL}, got {updated}");
}

[Fact]
public void BktUpdate_FromZero_CorrectGuess_StillLow()
{
    var pL = 0.01f; // nearly certain unknown
    var p = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

    var updated = BktTracer.Update(pL, isCorrect: true, p);

    // Even with correct answer, should still be relatively low (likely a guess)
    Assert.True(updated < 0.30f, $"Guessing student should stay low, got {updated}");
}

[Fact]
public void BktUpdate_HighMastery_SlipDoesNotCrash()
{
    var pL = 0.99f;
    var p = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

    var updated = BktTracer.Update(pL, isCorrect: false, p);

    // Should decrease but not crash or go negative
    Assert.InRange(updated, 0.001f, 0.999f);
}

[Fact]
public void BktUpdate_ZeroAllocation()
{
    var pL = 0.50f;
    var p = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);

    var before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 10_000; i++)
    {
        pL = BktTracer.Update(pL, i % 2 == 0, p);
    }
    var after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(0, after - before); // zero heap allocations
}
```
