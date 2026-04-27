# TASK-E2E-I-01: Misconception retention ≤ 30 days (ADR-0003)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/misconception-retention.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped)

## Journey

Student session emits misconception events → 30 days pass (IClock fast-forward) → cleanup runs → misconception rows pruned → admin DB scan confirms zero rows > 30 days.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Negative-property | `SELECT count(*) FROM misconception_store WHERE created_at < NOW()-INTERVAL '30 days'` returns 0 |
| Bus | `MisconceptionsPrunedV1` |

## Regression this catches

Retention extended silently; cleanup job stopped running.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
