# TASK-E2E-E-05: Accommodations profile (RDY-066)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@parent @a11y @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/accommodations-profile.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Parent → `/parent/accommodations` → configure (extra time, font size, hide-reveal) → sign consent-doc → save → next session respects the profile.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Profile UI + next session UI honors settings |
| DB | `AccommodationProfileAssignedV1` with consent-doc hash |
| LearningSession | Actor reads profile on session-start |

## Regression this catches

Accommodation not applied; wrong-child's profile applied; consent-doc hash silently ignored.

## Done when

- [ ] Spec lands
