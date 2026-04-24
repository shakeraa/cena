# TASK-E2E-D-04: CAS gate fails → LLM response blocked

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@cas @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cas-gate-fails.spec.ts`

## Journey

LLM produces a subtly wrong step → SymPy detects mismatch → response held → student sees "I need to double-check this" fallback → admin queue logs the failure.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `CasVerificationBinding` with `Status=Failed` |
| DOM | Fallback UX visible; wrong math NOT shown |
| Admin | `/apps/system/cas-failures` lists the row |
| Bus | `CasVerificationFailedV1` for ops alert |

## Regression this catches

Failed-CAS-still-shipped (ADR-0002 ship blocker); failure queue invisible to ops; fallback copy wrong.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
