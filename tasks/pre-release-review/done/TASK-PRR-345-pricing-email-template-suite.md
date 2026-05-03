# TASK-PRR-345: Pricing email template suite (welcome / renewal / past-due / cancellation / refund confirm) (TAIL)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week content + 3-5 days eng)
**Lens consensus**: tail
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: content + backend
**Tags**: epic=epic-prr-i, transactional-email, priority=p0, tail
**Status**: Partial — lifecycle-email worker hardened + RenewalUpcoming added + DI-registered + 15 tests shipped 2026-04-23; HE/AR/EN template content + real SMTP/SES/Mailgun dispatcher + legal-reviewed copy deferred on content + counsel gate
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

All transactional subscription emails in HE/AR/EN: welcome, trial-ending (if trial), renewal-upcoming, past-due, cancellation-confirm, refund-confirm.

## Scope

- Templates for each lifecycle moment.
- Honest framing (memory "Honest not complimentary" — no guilt-trip on cancellation emails).
- Correct invoice link embedded where relevant.
- Unsubscribe honored (statutory).
- Legal-reviewed cancellation + refund language.

## Files

- `emails/subscription-{welcome,renewal,past-due,cancellation,refund}.{he,ar,en}.html`
- Worker wiring to lifecycle events.
- Tests: locale, rendering, link correctness.

## Definition of Done

- All templates rendered, legal-approved.
- Wired to subscription lifecycle events.
- Locale correct.

## Non-negotiable references

- Israel direct-marketing law — unsubscribe.
- Memory "Honest not complimentary".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md), [PRR-295](TASK-PRR-295-two-week-progress-report-email.md), [PRR-323](TASK-PRR-323-weekly-parent-digest.md)

## What shipped (2026-04-23)

Audit surfaced the existing `SubscriptionLifecycleEmailWorker` was
**dead code** — file present but not DI-registered anywhere, no tests,
and `RenewalUpcoming` declared in the Kinds enum but not implemented
in the dispatch switch (4 of 5 kinds live). This session fixed all
three gaps so the worker is genuinely production-grade:

### Hardened worker

[SubscriptionLifecycleEmailWorker.cs](../../src/actors/Cena.Actors/Subscriptions/SubscriptionLifecycleEmailWorker.cs):

- Fifth kind implemented. `RenewalUpcoming` fires when `now` lands in
  `[RenewsAt − leadDays, RenewsAt − leadDays + windowHours]`. Default
  lead = 4 days, window = 25 hours (1-hour overlap slack so a missed
  tick still catches the event).
- Per-cycle idempotency for RenewalUpcoming. Marker id shape:
  `{parentId}:renewal_upcoming:{renewsAt:o}` — a parent gets exactly
  one email per renewal cycle, even across overlapping windows and
  missed ticks. Other four kinds remain per-parent-lifetime
  (`{parentId}:{kind}`).
- Terminal-event suppression. A stream with
  `SubscriptionCancelled_V1` or `SubscriptionRefunded_V1` suppresses
  downstream RenewalUpcoming — a cancelled parent does not receive a
  "your subscription renews in 4 days" email. Cross-cutting rule
  locked in the pure classifier.
- Classification logic extracted as `ClassifyDispatches(events,
  alreadySent, now, options) → IReadOnlyList<LifecycleDispatchPlanItem>`
  — pure static function, no I/O, testable without Marten. The
  `RunOnceAsync` loads events from Marten, maps them to
  `LifecycleEventInput` DTOs, calls the pure classifier, iterates the
  plan and dispatches via `TryDispatchAsync` (catches dispatcher
  exceptions per row so one bad email never kills the batch).
- Duplicate-input dedup. If the same event shows up twice in the
  Marten result (shouldn't happen, but defensive) the classifier
  uses `HashSet<string>.Add` to avoid planning duplicate rows.
- `SubscriptionLifecycleEmailWorkerOptions` with `RenewalUpcomingLeadDays`,
  `RenewalUpcomingWindowHours`, `TickIntervalHours`. Bound from
  configuration section `SubscriptionLifecycleEmail:*`.
- TimeProvider-driven `Task.Delay` so tests can drive the clock
  deterministically.

### DI wiring (previously missing entirely)

[SubscriptionServiceRegistration.cs](../../src/actors/Cena.Actors/Subscriptions/SubscriptionServiceRegistration.cs):

- `TryAddSingleton<ISubscriptionLifecycleEmailDispatcher,
  NullSubscriptionLifecycleEmailDispatcher>()` in shared services —
  dev-safe no-op default; hosts replace with a concrete dispatcher
  (SMTP / SES / Mailgun) by registering their own binding before
  `AddSubscriptions`/`AddSubscriptionsMarten`.
- `AddOptions<SubscriptionLifecycleEmailWorkerOptions>()` in shared.
- `AddHostedService<SubscriptionLifecycleEmailWorker>()` in
  `AddSubscriptionsMarten` (Marten-mode only — the worker needs
  `IDocumentStore` to scan the event log). Non-Marten hosts still
  exercise the pure classifier via unit tests.

### Tests (15 new, 351/351 Subscriptions pass)

[SubscriptionLifecycleEmailWorkerTests.cs](../../src/actors/Cena.Actors.Tests/Subscriptions/SubscriptionLifecycleEmailWorkerTests.cs):

- Terminal kinds: Activated → Welcome, PaymentFailed → PastDue,
  Cancelled → CancellationConfirm, Refunded → RefundConfirm.
- RenewalUpcoming window math: fires inside `[fireAt, fireAt+25h]`,
  does not fire 5 days out, does not fire 25h+ past the fire instant.
- Cancellation suppresses RenewalUpcoming even when the renewal window
  is technically hot.
- Refund suppresses RenewalUpcoming.
- `RenewalProcessed_V1` advances the cycle — cycle-2 RenewalUpcoming
  fires at cycle-2 renewal time with its own per-cycle marker id.
- Per-cycle idempotency — a recorded cycle marker prevents re-fire on
  subsequent ticks within the same cycle.
- AlreadySent markers never re-plan (pass-1 kinds).
- Duplicate input rows do not duplicate plan rows.
- Two parents are planned independently.
- `RenewalUpcomingLeadDays` option is respected.
- Null options rejected with ArgumentNullException.
- Full lifecycle correlation: a parent who activates → renews →
  fails payment → is cancelled gets Welcome + PastDue +
  CancellationConfirm but NOT RenewalUpcoming for cycle 2.

## What is deferred (content + legal + dispatcher gate)

- **HE / AR / EN email templates** — the 5 template files per locale
  (welcome / renewal / past-due / cancellation / refund) that the task
  lists. Content + counsel own these; engineering ships the seam.
- **Real dispatcher implementation** — `NullSubscriptionLifecycleEmailDispatcher`
  ships today (dev-safe no-op). A production host replaces it with an
  SMTP / Mailgun / SES implementation that reads the locale from the
  parent's profile + renders the right template. `TryAdd` default
  makes replacement a single-line change.
- **Legal-reviewed cancellation + refund copy** — the cancellation
  and refund templates specifically need Israeli Consumer Protection
  Law counsel sign-off (overlaps with PRR-294, PRR-306, PRR-333).
- **Unsubscribe link** — Israeli direct-marketing law requires a
  working unsubscribe on every marketing-adjacent send. The
  transactional nature of these emails (welcome/past-due/etc.) means
  unsubscribe does not apply to all of them, but the renewal-upcoming
  reminder likely does. Counsel-gated scope question.
- **Invoice link** — past-due + refund templates reference an
  invoice; PRR-305 (Hebrew tax invoice) ships the invoice ids the
  link will point to. Gated downstream on PRR-305.

Closing as **Partial** per memory "Honest not complimentary": the
backend delivery machinery is real and tested; the remaining work is
content + legal + a concrete dispatcher impl behind the existing seam.
