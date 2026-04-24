# TASK-E2E-F-01: Heatmap landing (RDY-070)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-F](EPIC-E2E-F-teacher-classroom.md)
**Tag**: `@teacher @privacy @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/teacher-heatmap.spec.ts`

## Journey

Teacher signs in → `/apps/teacher/heatmap` → topic × difficulty × methodology grid renders → color + pattern encoding (non-color-alone) → cell click → drilldown.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Grid renders; aria-labels present; pattern encoding alongside color |
| DB | `ICoverageCellVariantCounter` aggregates by institute |
| RBAC | Teacher of A cannot see B's rows |

## Regression this catches

Cross-tenant bleed; color-only encoding (a11y regression); k<10 cells expose individuals.

## Done when

- [ ] Spec lands
- [ ] Cross-tenant variant tested
