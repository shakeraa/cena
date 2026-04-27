# TASK-E2E-A-04: Parent ↔ child binding (prr-009)

**Status**: Spec landed at `tests/e2e-flow/workflows/parent-child-binding.spec.ts`. 3 tests listed: full bind flow (BLOCKED_ON `parent-bind-endpoint` per spec annotation), parent-dashboard endpoint shape, unauthenticated rejection.
**Priority**: P0
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @parent @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/parent-child-binding.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`) · **Backend prereq**: `/parent/bind?token=...` endpoint + bind-invite email + `ParentChildBoundV1` event — not yet queued; needs a TASK-E2E-A-04-BE-01 (bind endpoint) and -BE-02 (event)

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
