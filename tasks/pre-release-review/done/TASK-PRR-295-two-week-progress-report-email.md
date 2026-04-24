# TASK-PRR-295: Automated 2-week progress report email (HE/AR/EN)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week)
**Lens consensus**: persona #1 cost-conscious parent (observable early win pre-pays invoice 2)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev (email pipeline) + content (template copy)
**Tags**: epic=epic-prr-i, commercial, priority=p0, retention, trust
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

14 days after subscription start, send the parent an automated progress report showing concrete improvement: accuracy delta on practiced topics, time-on-task, readiness-score delta. Pre-pays mental value for the 30-day-mark second invoice.

## Scope

- Scheduled worker fires 14 days post-subscription-start (or at purchase of annual plan).
- Email content sourced from existing progress-report template (consider reusing [READINESS-001-bagrut-readiness-report.md](../../docs/design/READINESS-001-bagrut-readiness-report.md) data).
- HE/AR/EN variants; locale from parent account preference.
- Data included: student name(s), topics practiced, accuracy trend, time-on-task, readiness-score delta, next-week recommendation.
- Honest framing per memory "Honest not complimentary" — if no progress, say so with actionable next step, don't fake.
- Unsubscribe honored (CAN-SPAM + Israeli direct-marketing law).

## Files

- `src/backend/Cena.StudentApi/Workers/TwoWeekReportWorker.cs` (new)
- `src/backend/Cena.Domain/Reports/TwoWeekProgressReport.cs`
- Email templates `emails/two-week-progress-report.{he,ar,en}.html`
- Tests: locale selection, data accuracy, unsubscribe respects.

## Definition of Done

- Worker fires exactly at +14 days post-start.
- Email delivers in correct locale.
- Unsubscribe link works; state persisted.
- Honest framing passes content review (no fake-positive messaging).
- Full build green.

## Non-negotiable references

- Memory "Honest not complimentary" — report reflects real data.
- Memory "Labels match data" — claimed deltas match actual.
- Israel direct-marketing law — opt-out honored.
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no urgency copy.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + sample emails HE/AR/EN>"`

## Related

- [PRR-323](TASK-PRR-323-weekly-parent-digest.md) — ongoing weekly digest
- [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)
