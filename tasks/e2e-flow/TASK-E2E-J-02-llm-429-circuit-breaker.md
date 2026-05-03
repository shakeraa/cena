# TASK-E2E-J-02: LLM provider 429 → cost circuit breaker trips

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @llm @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/llm-429-circuit-breaker.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped)

## Journey

LLM provider returns 429 → `ICostCircuitBreaker` trips for N seconds → subsequent requests fail fast → breaker auto-recovers.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Quota UI |
| Admin | Breaker state visible |
| Bus | `LlmQuotaTrippedV1` |
| Metrics | Tripped duration recorded |

## Regression this catches

Breaker doesn't recover; trips for wrong reason (mistook 500 for 429).

## Done when

- [ ] Spec lands
