# TASK-E2E-I-08: Observability-consent gate on Sentry events (FIND-privacy-016)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/sentry-consent-gate.spec.ts`

## Journey

Student without consent → Sentry plugin stays no-op → no event to Sentry; consent flipped on → plugin initializes → events flow. user.id is always `id_hash` (never raw).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Outbound HTTP (MITM) | Zero `sentry.io` calls when consent off |
| Post-consent | Non-zero calls; `user.id_hash` field only |
| Session replay | Disabled (banned per ADR-0058 §2) |

## Regression this catches

Consent bypassed; raw user id leaks; session-replay enabled.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p0`
