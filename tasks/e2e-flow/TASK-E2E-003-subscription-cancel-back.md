# TASK-E2E-003: Subscription abandonment — user dismisses checkout → back to /pricing clean

**Priority**: P1
**Status**: Spec'd
**Spec**: `src/student/full-version/tests/e2e-flow/workflows/subscription-cancel-back.spec.ts`

---

## Journey

1. Fresh user.
2. Visit `/pricing` → click Premium annual → checkout session is created.
3. Before any webhook fires, user closes the Stripe modal / navigates back.
4. Expect clean return to `/pricing` — cycle toggle preserved, no flash-of-error state.
5. User immediately picks a different tier → new checkout session created (not the abandoned one reused).

## Boundary assertions

| Boundary | Assertion |
|---|---|
| **DOM** | `/pricing` renders, no error banner, cycle toggle = whatever was set pre-click |
| **DB** | First `CheckoutSession` row = `Abandoned` (not `Pending` forever); second click creates a fresh row |
| **Bus** | `CheckoutSessionAbandoned_V1` emitted (if that event exists in the subscription aggregate; otherwise confirm no spurious events) |
| **Stripe** | First session is expired / voided after N minutes via cron — not the concern of this test |

## What this catches

- Idempotency-key reuse bug (two clicks → same session → Stripe complains)
- "Ghost pending" subscriptions — rows that never get GC'd
- Stale UI state after a failed navigate (cycle toggle resets, tier cards flicker)

## Done when

- Spec green
- Idempotency-key collision path confirmed (click tier → abandon → click tier → new key)
