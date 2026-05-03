# TASK-E2E-001: Subscription happy path — login → pricing → Plus annual → Stripe success → /subscription/confirm

**Priority**: P0 (flagship spec — validates the pattern)
**Status**: Spike shipped (workflow skeleton); assertions at all 4 boundaries scheduled next iteration.
**Epic**: [EPIC-E2E-B](EPIC-E2E-B-subscription-billing.md) — workflow B-01
**Spec**: `src/student/full-version/tests/e2e-flow/workflows/subscription-happy-path.spec.ts`
**Tag**: `@billing @p0`
**Prereqs**: [TASK-E2E-INFRA-01](TASK-E2E-INFRA-01-bus-probe.md) (bus boundary, ✅ shipped) · PRR-436 admin test probe (DB boundary, pending)

---

## Journey

1. **Fresh user** — Playwright fixture signs up a brand-new user via Firebase Auth emulator; gets a session cookie for `student-api`.
2. **Visit `/pricing`** — page renders, three tier cards visible, toggle shows "Monthly / Annual".
3. **Select Plus annual** — click tier card → POST `/api/me/subscription/checkout-session` fires → backend returns Stripe Checkout URL.
4. **Simulate Stripe success** — instead of driving Stripe's hosted checkout, fixture POSTs a test-mode `checkout.session.completed` webhook via `stripe-cli trigger` bound to the session's metadata (customer=t_e2e_*).
5. **Navigate to `/subscription/confirm`** — SPA polls `/api/me/subscription` until state=Active.
6. **Land on confirm-active view** — `data-testid="subscription-confirm-active"` visible.

## Boundary assertions (all four)

| Boundary | Assertion | How |
|---|---|---|
| **DOM** | `subscription-confirm-active` visible within 10s | `expect(page.getByTestId(...)).toBeVisible()` |
| **DB** | `SubscriptionAggregate` state = `Active`, tier = `Plus`, cycle = `Annual` | `probes/db-probe.ts` reads via Marten (behind `/api/admin/test/probe` guarded by `CENA_TEST_PROBE_TOKEN`) |
| **Bus** | `SubscriptionActivated_V1` event published on `cena.events.subscription.{tenantId}.activated` | `probes/bus-probe.ts` subscribes a JetStream consumer at test start, asserts event seen |
| **Stripe** | `checkout.session.completed` linked to our `checkoutSessionId` | Stripe API lookup by metadata filter |

## Files touched

**Spec**: `workflows/subscription-happy-path.spec.ts`
**Fixtures used**: `tenant`, `auth`, `stripe`
**Probes used**: `db-probe`, `bus-probe`

## Dependencies

- Firebase emulator must be healthy (`docker ps cena-firebase-emulator`)
- Stripe CLI logged in to test mode
- `student-api` + `actor-host` + `nats` must be running

## Known gap — admin test-probe endpoint

The DB-probe assertion needs `/api/admin/test/probe?type=subscription&tenantId=X` endpoint. Not wired yet; spike uses HTTP-level assertion instead (status code + response shape from `/api/me/subscription`). **PRR-436 will wire the admin test probe.**

## Done when

- [x] Spec file runs green on clean dev stack
- [ ] All 4 boundary assertions in place (currently just DOM + HTTP)
- [ ] Tenant cleanup step confirmed at teardown
- [ ] Runs in < 45 seconds
