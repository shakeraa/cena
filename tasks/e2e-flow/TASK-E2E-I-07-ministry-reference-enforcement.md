# TASK-E2E-I-07: Ministry-reference enforcement (ADR-0043)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/ministry-reference-enforcement.spec.ts`
**Prereqs**: none beyond shared fixtures (`tenant`, `authUser`, `stripeScope` — wired in `fixtures/tenant.ts`)

## Journey

Admin tries to mark a raw Ministry-reference question as student-facing → backend rejects (ADR-0043 enforcement). Parametric-recreated version from that reference IS shippable.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Backend | Service-layer refusal on raw Ministry text |
| Admin UI | Error message reflects policy |
| Recreation path | Accepted |

## Regression this catches

Ministry raw text slips to students (ship blocker + Ministry compliance breach); recreation pipeline bypassed.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
