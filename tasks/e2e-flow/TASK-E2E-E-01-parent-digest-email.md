# TASK-E2E-E-01: Parent digest email → delivered → dashboard

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@parent @digest @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/parent-digest-email.spec.ts`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus probe — ✅ shipped) · PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Child completes a week → Saturday 08:00 local (IClock-seamed) → digest built → SMTP sends → parent clicks magic link → `/parent/dashboard` with age-band visibility filter.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Dashboard renders; no over-14 fields for 13+ child (prr-052) |
| SMTP | Email captured via test-mode SMTP sink |
| DB | `DigestDeliveredV1` event present |
| Bus | `ParentDigestDeliveredV1` emitted |

## Regression this catches

Dashboard shows mastery for 13+ child; magic link signs in wrong parent; digest sent despite opt-out.

## Done when

- [ ] Spec lands
- [ ] Age-band filter verified for 9 / 13 / 14 thresholds
