# TASK-E2E-D-06: LLM token budget exhausted → graceful fallback

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-D](EPIC-E2E-D-ai-tutoring.md)
**Tag**: `@llm @resilience @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/token-budget-exhausted.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Student exhausts weekly token budget → next tutor request → denied with quota message → fallback to cached canonical explanation.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Quota UI rendered; no silent empty response |
| DB | `TokenBudget` rows reflect cap reached |
| Circuit breaker | Cost circuit breaker trips per RATE-001 |
| Fallback | Canonical explanation served instead of LLM call |

## Regression this catches

Quota exceeded but request still hits LLM (runaway cost); fallback never shown; circuit breaker recovery broken.

## Done when

- [ ] Spec lands
