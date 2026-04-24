# TASK-E2E-E-04: Consent flow (ADR-0042 ConsentAggregate)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@gdpr @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/consent-flow.spec.ts`

## Journey

New parent registers → sees consent dialog (observability, AI tutoring, marketing — separate toggles) → flips → `ConsentAggregate` appends one event per toggle → read-model updated → admin consent-audit export shows trail.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Toggle states persist across reload |
| DB | `ConsentEventV1` event-sourced (not mutated) |
| Bus | `ConsentGrantedV1` / `ConsentRevokedV1` |
| Admin CSV export (prr-130) | Contains full audit trail |

## Regression this catches

Toggle revoked but state says granted; consent applied to wrong child; audit export missing a row.

## Done when

- [ ] Spec lands
- [ ] 12-flip stress variant asserts event count
- [ ] Tagged `@gdpr @p0`
