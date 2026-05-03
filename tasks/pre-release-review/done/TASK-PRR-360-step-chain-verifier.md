# TASK-PRR-360: `StepChainVerifier` — SymPy step-to-step equivalence checker

**Priority**: P0 — core of the epic
**Effort**: L (2-3 weeks)
**Lens consensus**: persona #3 teacher (CAS-auditable), #7 ML safety (hallucination prevention)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + math-SME
**Tags**: epic=epic-prr-j, cas, priority=p0, core
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Given a canonical step sequence, verify each step-to-step transition is mathematically valid. Return the first failing transition with expected-vs-detected expressions, plus the CAS operation that was (in)validly applied.

## Scope

- For each consecutive pair (step_i, step_{i+1}): check symbolic equivalence after candidate operations (expand, factor, simplify, solve, substitute).
- Output: `VerificationResult { firstFailingIndex, expectedExpression, detectedExpression, candidateOperations, failureReason }`.
- Handle: legitimate step-skipping (mathematically valid N-step leap accepted).
- Reject: invalid algebraic moves, sign errors, dropped terms.
- Export format for teacher-audit view ([PRR-363](TASK-PRR-363-cas-chain-export-format.md)).

## Files

- `src/backend/Cena.Diagnostic/CAS/StepChainVerifier.cs`
- `src/backend/Cena.Diagnostic/CAS/VerificationResult.cs`
- SymPy invocation layer.
- Tests: 50+ step-transition scenarios covering distribution, factoring, cancellation, sign, substitution.

## Definition of Done

- All transition scenarios tested.
- First-failing-step detection deterministic.
- Performance: p95 <5 sec for 10-step chain.
- Full sln green.

## Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) — mandatory verification.
- Memory "No stubs — production grade".
- Memory "SymPy CAS oracle".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-361](TASK-PRR-361-canonicalization-layer.md), [PRR-362](TASK-PRR-362-step-skipping-tolerance.md), [PRR-363](TASK-PRR-363-cas-chain-export-format.md), [PRR-374](TASK-PRR-374-template-matching-scorer.md)
