# TASK-E2E-G-07: LLM cost dashboard (prr-112)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @llm @tenant @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/llm-cost-dashboard.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Admin opens `/apps/system/llm-cost` → per-feature / per-cohort cost breakdown → filter by time range → CSV export.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Chart renders; CSV downloads |
| DB | `LlmCostMetric` rollup |
| Tenant | SUPER_ADMIN → all; ADMIN → own institute only |

## Regression this catches

Cost leak across tenants; CSV missing rows; rollup falls back to Null (shows zeros).

## Done when

- [ ] Spec lands
