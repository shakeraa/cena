# TASK-PRR-386: Soft-cap-reached UX (Premium diagnostic)

**Priority**: P0 — shipgate-gated
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO, shipgate enforcement
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + content
**Tags**: epic=epic-prr-j, ux, shipgate, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Friendly positive-framing UX when Premium soft cap (100 diagnostic uploads/mo) is reached. Offers tutor-session upsell or continue-anyway. NOT a hard block. Shipgate scanner must pass.

## Scope

- Modal at 100th upload of the month.
- Copy: "You've been busy! You're in the top 1% of Premium users this month. Want to talk it through with a tutor?" or similar (HE/AR/EN).
- Actions: "Book tutor" / "Continue anyway" / "Close".
- Hard cap at 300 routes to support CTA.

## Files

- `src/student/full-version/src/components/diagnostic/DiagnosticSoftCapModal.vue`
- i18n.
- Tests incl. shipgate scanner.

## Definition of Done

- Modal appears at soft cap.
- Copy shipgate-passes.
- Continue-anyway works.
- Hard cap at 300 surfaces contact-support.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- [`docs/engineering/shipgate.md`](../../docs/engineering/shipgate.md).
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-400](TASK-PRR-400-per-tier-upload-counter.md), [PRR-401](TASK-PRR-401-soft-cap-upsell-trigger.md), [PRR-313](TASK-PRR-313-graceful-soft-cap-upsell.md)
