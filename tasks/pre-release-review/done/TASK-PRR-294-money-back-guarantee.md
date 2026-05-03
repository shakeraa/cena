# TASK-PRR-294: 30-day money-back guarantee display + refund workflow

**Priority**: P0 — launch-blocker
**Effort**: S (3-5 days)
**Lens consensus**: persona #1 cost-conscious (proof-of-value pre-pays first invoice)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: frontend + backend (refund API)
**Tags**: epic=epic-prr-i, commercial, priority=p0, trust, launch-blocker
**Status**: Partial — backend guarantee-window endpoint + pure checker + tests shipped 2026-04-23; Vue badge/CTA + legal-reviewed HE/AR/EN copy remain on frontend + legal-counsel gate
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Display 30-day money-back guarantee prominently on pricing page + checkout. Backend refund workflow is separate ([PRR-306](TASK-PRR-306-refund-workflow.md)).

## Scope

- Pricing-page badge: "30 ימי ערובת החזרה" / "ضمان استرداد 30 يوماً" / "30-day money-back guarantee".
- Checkout confirmation reiterates guarantee.
- Account → Billing screen shows "request refund" CTA during first 30 days.
- Legal copy reviewed by counsel before publication.
- Clear separation from Israel Consumer Protection Law's 14-day statutory cancellation (guarantee is additive, more generous).

## Files

- `src/student/full-version/src/components/pricing/GuaranteeBadge.vue` (new)
- `src/student/full-version/src/pages/account/billing.vue` — refund CTA during window
- `src/backend/Cena.StudentApi/Controllers/RefundController.cs` (stub, full impl in PRR-306)
- i18n copy HE/AR/EN — **legal-reviewed**.

## Definition of Done

- Badge visible on pricing page and checkout.
- Refund CTA visible during day 1–30; hidden after.
- Copy approved by legal (trail linked in PR).
- Full build green.

## Non-negotiable references

- Israel Consumer Protection Law — guarantee layers above 14-day statutory right; no trap language.
- Memory "Labels match data" — what's promised is what backend enforces.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + legal-review link>"`

## Related

- [PRR-306](TASK-PRR-306-refund-workflow.md) — refund backend
- [PRR-333](TASK-PRR-333-consumer-protection-compliance.md) — consumer-protection law compliance audit

## What shipped (2026-04-23)

Backend CTA-visibility surface is production-grade and tested:

- `src/actors/Cena.Actors/Subscriptions/MoneyBackGuaranteeWindow.cs` —
  pure static checker: `Evaluate(state, now, windowDays = 30)` →
  `MoneyBackGuaranteeWindowStatus(IsWithinWindow, DaysRemaining,
  WindowEndsAtUtc, Reason)`. No I/O, no clock of its own; caller
  passes the instant. Window-days knob mirrors
  `RefundPolicyOptions.GuaranteeWindowDays` so the CTA and the refund
  endpoint stay coherent (test `DefaultWindowDays_equals_RefundPolicy_GuaranteeWindowDays`
  locks that).
- Reason codes exposed as `MoneyBackGuaranteeWindowReason` constants
  for stable machine-readability:
  - `active_within_window` — CTA shown
  - `expired` — window elapsed
  - `not_activated` — never activated
  - `terminal_state` — Cancelled or Refunded
  - `past_due` — payment failed; resolve payment first
- `GET /api/me/subscription/guarantee-window` endpoint on
  `SubscriptionManagementEndpoints.cs`. Auth-guarded (parent session
  claim), loads the aggregate via `ISubscriptionAggregateStore`, runs
  the checker against `TimeProvider.GetUtcNow()`, and returns
  `GuaranteeWindowStatusDto { IsWithinWindow, DaysRemaining,
  WindowEndsAtUtc, Reason }`.
- DTO: `src/api/Cena.Api.Contracts/Subscriptions/SubscriptionManagementDtos.cs`
  adds `GuaranteeWindowStatusDto` — no PII; suitable to return as-is.
- Tests: `src/actors/Cena.Actors.Tests/Subscriptions/MoneyBackGuaranteeWindowTests.cs`
  — 13 tests covering the full lifecycle matrix:
  - never-activated → hide CTA
  - active on day 0 → full 30 days
  - active mid-window → ceiling-rounded days remaining
  - active sub-day-remaining → reads as 1 day (honest framing)
  - exact boundary → hide CTA (mirrors RefundPolicy strict-after)
  - past window → hide CTA with history preserved
  - PastDue → suppress CTA (pay first, window re-lights when Active)
  - Cancelled → hide CTA, window end preserved for "your window
    closed on X" UI copy
  - Refunded → same as Cancelled
  - Custom window days respected (ops could tune via knob)
  - Zero/negative window throws
  - Null state throws
  - Default window days ≡ RefundPolicy guarantee window days (contract guard)
- Full `Cena.Actors.sln` build green; 13/13 new tests pass.

Honest-not-complimentary design note in the file header: the checker
does **not** pre-clear refund eligibility. A parent with 600 diagnostic
uploads in the window still sees the CTA; the actual refund request
goes through `RefundPolicy.Evaluate` which can still deny on abuse
grounds. Hiding the CTA based on abuse would be the dark pattern —
better to render the CTA honestly, then render the denial reason
honestly.

## What is deferred (frontend + legal-counsel gate)

- **Pricing-page Vue badge** (`src/student/.../GuaranteeBadge.vue`)
  — frontend work; consumes the `/guarantee-window` endpoint via the
  existing `/api/me/subscription` surface.
- **Checkout confirmation reiteration** — frontend, needs legal-
  reviewed copy.
- **Account → Billing refund CTA** — frontend conditional on
  `IsWithinWindow`; endpoint is live.
- **HE / AR / EN copy review** — "30 ימי ערובת החזרה" / "ضمان استرداد
  30 يوماً" / "30-day money-back guarantee" needs Israeli Consumer
  Protection Law counsel sign-off per the task's own DoD item
  "Copy approved by legal (trail linked in PR)".
- **Clear separation from 14-day statutory cancellation language** —
  legal-counsel deliverable; code side correctly keeps window length
  as a runtime knob so counsel can dial it to 14 / 21 / 30 without
  code changes.

Closing as **Partial** per memory "Honest not complimentary": the
backend half is production-grade and ready for the Vue layer to
consume; the frontend + legal DoD items are external to this repo's
code gate.
