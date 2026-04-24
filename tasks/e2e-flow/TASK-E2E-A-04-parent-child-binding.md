# TASK-E2E-A-04: Parent ↔ child binding (prr-009)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @parent @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/parent-child-binding.spec.ts`

## Journey

Existing student → parent receives bind-invite email → parent clicks link → `/parent/bind?token=...` → confirms kinship → parent-side dashboard shows child.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Parent dashboard shows the child's name/id |
| DB | `ParentChildBinding` row with correct relationship + tenant |
| Bus | `ParentChildBoundV1` event emitted |
| Token | Single-use — second click returns 409 |

## Regression this catches

Parent sees another family's child (binding leak); unsigned-token replay; cross-tenant invite honored; kinship mismatch accepted.

## Done when

- [ ] Spec lands
- [ ] Cross-tenant invite path tested (rejected)
- [ ] Tagged `@parent @p0`
