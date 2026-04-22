# TASK-PRR-304: Bank transfer flow (manual reconciliation v1)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona #6, #5 (edge-case payment for CC-free households)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + finance (reconciliation ops)
**Tags**: epic=epic-prr-i, billing, priority=p1
**Status**: Ready — can ship post-launch if critical path tight
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch-or-launch+1
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Offer bank transfer as checkout option for segments without CC/Bit/PayBox. V1 = manual reconciliation; automation deferred.

## Scope

- Checkout "pay by bank transfer" option shows Cena bank details + unique reference code.
- Subscription enters `pending_payment` state (not yet active).
- Admin tool for finance to mark payment received → transition to `active`.
- Auto-expire after 14 days if unreceived.
- Annual prepay only initially (not monthly — reconciliation cost too high for ₪79).

## Files

- `src/backend/Cena.StudentApi/Controllers/BankTransferController.cs`
- `src/admin/full-version/src/pages/finance/bank-transfer-reconciliation.vue`
- Tests.

## Definition of Done

- Checkout via bank transfer creates pending_payment subscription.
- Admin mark-received → active.
- 14-day auto-expire works.

## Non-negotiable references

- Memory "No stubs — production grade".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
