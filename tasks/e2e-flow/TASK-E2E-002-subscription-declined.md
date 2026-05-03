# TASK-E2E-002: Subscription rejection — login → pricing → tier → declined card → /subscription/cancel

**Priority**: P1
**Status**: Spec'd (skeleton to be implemented alongside TASK-E2E-001's boundary-assertion pass)
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md) — workflow B-02
**Spec**: `src/student/full-version/tests/e2e-flow/workflows/subscription-declined.spec.ts`
**Tag**: `@billing @p1`
**Prereqs**: TASK-E2E-001 spike (shared fixtures + checkout flow)

---

## Journey

1. Fresh user via Firebase emulator.
2. Visit `/pricing` → select Basic monthly.
3. Checkout session created — instead of success webhook, fire a `checkout.session.expired` or `payment_intent.payment_failed` via `stripe-cli trigger`.
4. Expect SPA to land on `/subscription/cancel`.
5. "Try again" CTA links back to `/pricing` (assert href + click → pricing renders).

## Boundary assertions

| Boundary | Assertion |
|---|---|
| **DOM** | `subscription-cancel-page` visible; retry CTA present + correct href |
| **DB** | `SubscriptionAggregate` state = `Pending` or absent; no spurious `Active` row |
| **Bus** | NO `SubscriptionActivated_V1` fired (inverse assertion — confirm absence within a 5s window) |
| **Stripe** | `payment_intent.payment_failed` captured with decline_code = `card_declined` |

## Critical regression surfaces this catches

- Backend wrongly activating a sub on failed payment (auth-token replay vulnerability)
- SPA not handling the cancel redirect (blank page / stale state)
- Duplicate webhook delivery race creating two subscription rows

## Done when

- Spec green alongside TASK-E2E-001
- Decline-reason UX text asserted (i18n key renders, not raw key)
