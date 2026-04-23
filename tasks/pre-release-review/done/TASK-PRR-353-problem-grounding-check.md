# TASK-PRR-353: Original-problem grounding check

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #6 engineering (prevents screenshot-of-wrong-problem attack)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, priority=p0, safety
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Extracted steps must trace back to the currently-posed problem's initial expression. If not, reject with "I couldn't connect this to the problem you're solving."

## Scope

- Compare step-0 of extracted sequence against posed problem's initial expression via SymPy equivalence.
- Tolerance: numerical & symbolic equivalence accepted; unrelated expression rejected.
- Prevents: student uploads photo of different problem, abuse / gaming attempts.

## Files

- `src/backend/Cena.Diagnostic/StepExtraction/ProblemGroundingChecker.cs`
- Tests: matching / mismatching / equivalent-restatement cases.

## Definition of Done

- Matching problem → passes.
- Mismatched problem → rejected with helpful message.
- Full sln green.

## Non-negotiable references

- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-350](TASK-PRR-350-step-extraction-service.md), [PRR-360](TASK-PRR-360-step-chain-verifier.md)
