# TASK-E2E-G-04: CAS override (RDY-036 / RDY-045)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @security @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/cas-override.spec.ts`

## Journey

SUPER_ADMIN sees a CAS-verified-failed question → uses override endpoint with justification → override logged to SIEM + Slack security notifier → question becomes shippable with override flag.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Override form with justification |
| DB | `CasOverrideEventV1` event-sourced (immutable) |
| SIEM | Audit log row |
| Security-notifier | SMS+Slack webhook fires |
| RBAC | ADMIN (non-super) → 403 |

## Regression this catches

ADMIN uses override (ship blocker); override without justification accepted; security-notifier silently failing.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
