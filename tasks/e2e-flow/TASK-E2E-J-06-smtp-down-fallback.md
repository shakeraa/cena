# TASK-E2E-J-06: SMTP down → parent digest falls back

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-J](EPIC-E2E-J-resilience-failure-modes.md)
**Tag**: `@resilience @parent @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/smtp-down-fallback.spec.ts`

## Journey

SMTP unreachable → channel dispatcher falls back to WhatsApp if opted in, else holds + retries → SMTP recovers → held digests deliver.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | `DigestHoldingQueue` during outage |
| Post-recovery | Zero held rows |
| Bus | `DigestDeliveryDelayedV1` during outage |

## Regression this catches

Digest silently dropped; fallback picks wrong channel; holding queue grows unbounded.

## Done when

- [ ] Spec lands
