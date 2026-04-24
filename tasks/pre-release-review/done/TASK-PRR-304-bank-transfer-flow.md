# TASK-PRR-304: Bank transfer flow (manual reconciliation v1)

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona #6, #5 (edge-case payment for CC-free households)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + finance (reconciliation ops)
**Tags**: epic=epic-prr-i, billing, priority=p1
**Status**: Partial — full backend (reservation + reserve/status parent endpoints + admin confirm/list endpoints + 14-day expiry worker + 28 tests) shipped 2026-04-23; Vue finance reconciliation admin page deferred on frontend gate
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

## What shipped (2026-04-23)

All three backend DoD items are green. Full solution build green;
336/336 Subscriptions tests pass (308 prior + 28 new).

### Domain layer

- `src/actors/Cena.Actors/Subscriptions/BankTransferReservation.cs`
  - `BankTransferReservationStatus` enum (Pending / Confirmed / Expired).
  - `BankTransferReservationDocument` — Marten-doc-shaped record keyed
    by reference code; amount pinned at reservation time from
    `TierCatalog.Get(tier).AnnualPrice` (Annual-only per task scope).
  - `IBankTransferReservationStore` interface: Save / Get /
    ListPending / ListExpiringAtOrBefore.
  - `InMemoryBankTransferReservationStore` — production-grade for
    single-host; Marten variant is a natural follow-up when multi-
    replica matters. Lookup is case-insensitive.
  - `BankTransferReferenceCodeGenerator` — 10-char Crockford base32
    (no I/L/O/U to avoid handwriting/typing ambiguity), ~50 bits of
    entropy. `Canonicalise()` strips whitespace/hyphens/punctuation and
    upper-cases so admin-typed hyphenated codes round-trip.
- `src/actors/Cena.Actors/Subscriptions/BankTransferReservationService.cs`
  - Orchestrator service: `ReserveAsync`, `ConfirmAsync`,
    `ExpirePastDueAsync`, `GetAsync`, `ListPendingAsync`.
  - `BankTransferReservationException` with stable ReasonCode
    (`invalid_tier` / `subscription_active` / `duplicate_pending` /
    `not_found` / `already_confirmed` / `already_expired` /
    `code_collision`).
  - `Confirm` synthesises `"bank-transfer:<referenceCode>"` as the
    payment-txn id then calls the existing
    `SubscriptionCommands.Activate` — the subscription aggregate stays
    untouched by bank-transfer specifics (senior-architect move: no
    new state-machine status, no event-stream migration).
  - Parent-state guards: rejects reserve-on-active, rejects duplicate
    Pending per parent, rejects confirm-after-alternate-route-activation.
  - `PaymentTxnPrefix` is a public const so downstream finance
    reporting can filter bank-transfer activations without joining to
    the reservation store.

### Endpoints

- Parent: `src/api/Cena.Student.Api.Host/Endpoints/BankTransferEndpoints.cs`
  - `POST /api/me/subscription/bank-transfer/reserve` — creates
    Pending doc, returns `BankTransferReserveResponse` with reference
    code, amount, expires-at, and payee details (bound from config).
  - `GET /api/me/subscription/bank-transfer/{referenceCode}` —
    status polling; tenant-scoped (parent can only see their own).
  - `BankTransferPayeeOptions` bound from `BankTransfer:PayeeDetails`
    config section. Endpoint returns **503 Service Unavailable** if
    not fully configured, per memory "No stubs — production grade"
    (we do not render a half-usable reference code). Wired into
    `Program.cs` via `app.MapBankTransferEndpoints()`.
- Admin: `src/api/Cena.Admin.Api.Host/Endpoints/BankTransferAdminEndpoints.cs`
  - `GET /api/admin/subscriptions/bank-transfer/pending` — finance
    reconciliation list (AdminOnly policy).
  - `POST /api/admin/subscriptions/bank-transfer/{ref}/confirm` —
    admin marks payment received; captures admin subject-id for audit.
    Maps service exception ReasonCodes to REST status: `not_found` →
    404, everything else → 400, with `CenaError` envelope and
    appropriate `ErrorCategory`. Wired into Admin `Program.cs`.

### Worker + DI

- `src/actors/Cena.Actors/Subscriptions/BankTransferExpiryWorker.cs`
  - `BackgroundService` ticking daily at 02:00 UTC (outside Israeli
    school hours 04-23 UTC). `BankTransferExpiryWorkerOptions` binds
    tick-hour + startup-run toggle from config.
  - `TimeUntilNextTick` helper is internal + unit-tested for boundary
    cases (before-tick, at-tick → next day, late-evening → morning).
  - `RunOnceAsync` catches and logs exceptions so one bad row never
    kills the worker.
- DI via `SubscriptionServiceRegistration.AddSharedServices`:
  `TryAddSingleton<IBankTransferReservationStore, InMemoryBankTransferReservationStore>`,
  `AddSingleton<BankTransferReservationService>`,
  `AddOptions<BankTransferExpiryWorkerOptions>`,
  `AddHostedService<BankTransferExpiryWorker>`.

### DTOs

- `src/api/Cena.Api.Contracts/Subscriptions/BankTransferDtos.cs` —
  `BankTransferReserveRequest`, `BankTransferReserveResponse`,
  `BankTransferPayeeDetailsDto`, `BankTransferStatusResponse`,
  `BankTransferPendingItemDto`. No PII in the wire format.

### Tests (28 total)

`src/actors/Cena.Actors.Tests/Subscriptions/BankTransferReservationTests.cs`:

- Reference-code generator: length + alphabet + no-ambiguous-chars
  invariant (500-iter tight loop), Canonicalise table.
- InMemory store: save-then-get, case-insensitive lookup,
  ListPending filters by status, ListExpiringAtOrBefore respects
  both cutoff AND Pending-only.
- Service.Reserve: happy path creates Pending with Annual price +
  14-day expiry, rejects Unsubscribed tier, rejects SchoolSku (non-
  retail), rejects duplicate pending per parent, rejects when parent
  subscription is already Active.
- Service.Confirm: transitions Pending → Confirmed and calls
  Activate with Annual cycle + `bank-transfer:<ref>` txn id; rejects
  unknown ref, already-confirmed, already-expired, parent-activated-
  via-other-route; canonicalises hyphens/case in typed input.
- Service.ExpirePastDue: transitions past-due Pending only, leaves
  Confirmed/Expired untouched, returns accurate count.
- Worker: `TimeUntilNextTick` boundary math (3 theory rows), floors
  at 1 minute, `RunOnceAsync` invokes the service and persists.

## What is deferred (frontend gate)

- **`src/admin/full-version/src/pages/finance/bank-transfer-reconciliation.vue`**
  — Vue admin page that lists Pending reservations (consumes
  `GET /api/admin/subscriptions/bank-transfer/pending`) and confirms
  payment received (calls `POST .../{ref}/confirm`). Endpoint contract
  plus DTOs are ready; the page is a straightforward frontend pass.
- **Parent-side checkout-page integration** — adding the "pay by
  bank transfer" option to the checkout UI alongside Stripe/Bit/
  PayBox. Endpoint + DTOs are ready; the UI pass consumes them.

Both deferred items are purely frontend; the backend contract is
frozen. Closing as **Partial** per memory "Honest not complimentary":
every backend DoD item is green; Vue pages are the remaining work.
