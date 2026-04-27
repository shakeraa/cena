# TASK-E2E-G-06: Cultural-context review board DLQ (prr-034)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @content @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cultural-dlq.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

LLM content flagged for cultural-context review → DLQ → admin reviews → fixes or rejects → outcome event-sourced.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `CulturalContextReview` row |
| Admin UI | Queue listing |
| Bus | NATS DLQ topic subscriber count |

## Regression this catches

Flagged content leaks past DLQ to students; action non-idempotent; queue grows unbounded.

## Done when

- [ ] Spec lands
