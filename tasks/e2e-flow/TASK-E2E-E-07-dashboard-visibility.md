# TASK-E2E-E-07: Dashboard-visibility age-band filter (prr-052)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@gdpr @parent @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/dashboard-visibility.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Parent of a 14-year-old logs in → dashboard shows ONLY fields allowed for 14+ (no mastery breakdown, no misconception patterns) → parent of 9-year-old sees wider set.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Rendered fields = backend `/api/parent/dashboard-visibility` response |
| DB | Filter applied consistently server-side |

## Regression this catches

Filter leaks fields; backend differs from frontend (consistency drift); field set not updated when child ages into a new band.

## Done when

- [ ] Spec lands
- [ ] Age transition (13 → 14) verified with IClock fast-forward
