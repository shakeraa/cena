# TASK-E2E-C-01: First-ever sign-in → onboarding diagnostic → plan

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @onboarding @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/onboarding-diagnostic.spec.ts`

## Journey

Fresh student post-registration (chains from A-01) → `/onboarding` → exam-target picker (multi-target per ADR-0050) → short diagnostic quiz (MST-013) → MIRT estimator runs → plan generated → `/home` shows initial plan.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Wizard steps render; diagnostic Q's rendered with CAS-verified content; `/home` shows plan post-completion |
| DB | `StudentPlan` row with exam targets; MIRT theta per concept cluster within valid bounds |
| Bus | `OnboardingCompletedV1` + `PlanGeneratedV1` emitted |

## Regression this catches

Blank home page after onboarding (plan gen crashed silently); wrong exam targets saved; MIRT theta out-of-bounds.

## Done when

- [ ] Spec lands
- [ ] Multi-target selection (2+ targets) covered
- [ ] Tagged `@onboarding @p0`
