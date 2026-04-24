# TASK-PRR-323: Weekly parent email digest (HE/AR/EN)

**Priority**: P0 — Premium retention driver
**Effort**: M (1 week)
**Lens consensus**: persona #2 high-SES (time-saving trust signal)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend (email worker) + content (template copy)
**Tags**: epic=epic-prr-i, parent-ux, retention, priority=p0
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Every week, Premium parents receive a concise digest of their student(s) activity, progress, and next-week recommendation. Ongoing counterpart to the one-shot [PRR-295](TASK-PRR-295-two-week-progress-report-email.md).

## Scope

- Scheduled worker — Sunday mornings (weekly anchor for Israeli school week).
- Email locale from parent preference.
- Content: topics practiced this week, accuracy trends, concerning patterns (honest), recommended focus, time-on-task.
- No fake-positive framing (memory "Honest not complimentary").
- Unsubscribe honored per Israeli direct-marketing law.
- Basic tier opted out by default; available only to Plus + Premium.

## Files

- `src/backend/Cena.StudentApi/Workers/WeeklyParentDigestWorker.cs`
- Templates `emails/weekly-digest.{he,ar,en}.html`

## Definition of Done

- Digest delivered every Sunday to opted-in Premium + Plus parents.
- Honest framing even on low-progress weeks.
- Unsubscribe flow works.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary".
- Israel direct-marketing law.
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no countdown/urgency.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-295](TASK-PRR-295-two-week-progress-report-email.md), [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md)
