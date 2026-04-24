# TASK-E2E-B-08: Bank transfer path (PRR-304, Israeli market)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md)
**Tag**: `@billing @il @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/bank-transfer.spec.ts`

## Journey

Parent picks "bank transfer" on `/pricing` → POST `/api/me/bank-transfer` → reference number displayed + emailed → admin records payment → subscription activated manually.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Reference number shown post-submit |
| DB | `BankTransferRequest` row created |
| Email | Confirmation email captured via test SMTP sink |
| Admin UI | Pending row visible at `/apps/system/bank-transfers` |
| Bus | `BankTransferActivatedV1` after admin records payment |

## Regression this catches

Bank-transfer requests silently dropped; admin UI doesn't show them; activation applies wrong tier.

## Done when

- [ ] Spec lands
- [ ] Tenant scoping asserted on admin UI
