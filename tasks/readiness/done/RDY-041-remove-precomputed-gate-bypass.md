# RDY-041: Remove `preComputedGateResult` Trust-The-Caller Bypass (Fix #6)

- **Priority**: **Critical / ship-blocker** — architectural bypass of the single-writer invariant
- **Complexity**: Mid-senior engineer
- **Source**: Senior-architect review of `claude-code/cas-gate-residuals` (2026-04-15)
- **Tier**: 1
- **Effort**: 2-4 hours
- **Dependencies**: RDY-037 (merged), RDY-038 (recommended first)

## Problem

`CasGatedQuestionPersister.PersistAsync` accepts `CasGateResult? preComputedGateResult` ([src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs:133](../../src/actors/Cena.Actors/Cas/CasGatedQuestionPersister.cs#L133)) and the persister "does not re-run the gate" when one is supplied. `QuestionBankService.CreateQuestionAsync` already relies on this ([QuestionBankService.cs:413](../../src/api/Cena.Admin.Api/QuestionBankService.cs#L413)) so it can inspect the outcome before deciding on auto-approval events.

This defeats the single-writer invariant's entire purpose: a future (or AI-generated, or hostile) caller can forge a `Verified` result and persist an unverified question. The arch-test does not catch it — it only pattern-matches for `StartStream<QuestionState>`.

## Scope

### 1. Replace the bypass with a two-phase API

New contract:

```csharp
// Phase 1 — gate-only (no persistence). Returns outcome for the caller's
// decision-making. Idempotent + cacheable.
Task<CasGateResult> EvaluateAsync(GatedPersistContext context, CancellationToken ct);

// Phase 2 — persists. Runs the gate itself from `context`; the caller
// cannot supply a result. If the gate has been evaluated already via the
// same (questionId, hash) in the last N seconds, the persister hits the
// idempotency cache (existing path in CasVerificationGate).
Task<GatedPersistOutcome> PersistAsync(
    IDocumentSession session,
    string questionId,
    object creationEvent,
    GatedPersistContext context,
    IReadOnlyList<object>? extraEventsOnNewStream = null,
    IReadOnlyList<object>? companionDocuments = null,
    CancellationToken ct = default);
```

`preComputedGateResult` is removed. The idempotency cache (already keyed on `(QuestionId, CorrectAnswerHash)`) absorbs the double round-trip cost for the `QuestionBankService` use case.

### 2. Rewire `QuestionBankService.CreateQuestionAsync`

- Call `_casGate.EvaluateAsync(...)` or equivalent to inspect outcome for the auto-approve decision.
- Call `_persister.PersistAsync(...)` — the persister re-runs the gate, hits the cache, returns the same result.
- Net latency: +1 cache lookup; +0 CAS calls.

### 3. Extend arch-test

`SeedLoaderMustUseQuestionBankServiceTest` additionally asserts:
- No file in `src/` (except `CasGatedQuestionPersister.cs` + its test) constructs `CasGateResult` directly. Forging a result in a non-test file fails the build.

### 4. Tests

- `CasGateForgeryPreventionTests` — compile-time test that attempts to pass a forged `CasGateResult` to the persister and confirms the API no longer accepts it
- Behavioural: `QuestionBankService_TwoPhase_HitsIdempotencyCache` — confirm the second gate call returns from cache

### 5. Update ADR-0032 §16.1

Document the two-phase contract and the "no caller-supplied gate result" rule.

## Acceptance Criteria

- [ ] `preComputedGateResult` parameter removed from `ICasGatedQuestionPersister`
- [ ] `QuestionBankService.CreateQuestionAsync` uses the two-phase pattern; auto-approve logic preserved
- [ ] Arch-test fails on any non-persister construction of `CasGateResult`
- [ ] Idempotency cache absorbs the extra round-trip (verified by test)
- [ ] ADR-0032 §16.1 updated
- [ ] No CasGatingTests regress
- [ ] Full sln builds green
