# ACT-024: Review BKT Forgetting Factor — Align with Corbett & Anderson

**Priority:** P2 — affects mastery accuracy for all students
**Blocked by:** None (domain decision required)
**Estimated effort:** 0.5 days (implementation) + domain review
**Source:** Actor system review M6 — BKT applies forgetting after learning transition

---

## Context
`BktService.Update()` applies `posterior = posterior * (1 - pForget)` after the learning transition step. The standard Corbett & Anderson (1994) model does NOT include forgetting in the within-trial update — longer-term decay is handled separately by HLR (Half-Life Regression).

With `pForget = 0.02`, mastery is depressed by 2% on every single attempt, making it harder to reach the 0.85 mastery threshold than the paper specifies. A student needs ~6 more correct answers to reach mastery compared to the standard model.

## Decision Required

**Option A: Remove the forgetting factor** (line 130 of `BktService.cs`)
- Pro: Aligns with published Corbett & Anderson model
- Pro: Mastery progression matches calibrated K-12 benchmarks
- Con: Loses the within-session micro-decay signal

**Option B: Keep but reduce** (`pForget = 0.005` instead of `0.02`)
- Pro: Slight within-session decay prevents mastery from ratcheting up too fast on lucky guesses
- Con: Still deviates from published model

**Option C: Keep as-is but document the deviation**
- Pro: No code change
- Con: May cause students to feel stuck if mastery plateau is unexpected

## Subtasks

### ACT-024.1: Implement Chosen Option
**Files:**
- `src/actors/Cena.Actors/Services/BktService.cs` — modify line 130

**Acceptance:**
- [ ] Domain owner (product/pedagogy team) signs off on chosen option
- [ ] Implementation matches decision
- [ ] If Option A: remove line 130 (`posterior = posterior * (1.0 - pForget)`) and the pForget parameter usage
- [ ] If Option B: change `PForget: 0.02` to `PForget: 0.005` in `BktParameters.Default`
- [ ] If Option C: add XML doc comment explaining the deviation and its impact
- [ ] Unit test: verify mastery reaches 0.85 within expected number of correct answers
