# TASK-PRR-325: Tutor-handoff PDF export (Premium differentiator)

**Priority**: P0 — launch-blocker (Premium differentiator per persona #7)
**Effort**: M (1 week)
**Lens consensus**: persona #7 tutor (flips competitor→channel), #2 high-SES (trust artifact)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend (PDF generation) + UX
**Tags**: epic=epic-prr-i, parent-ux, premium-differentiator, priority=p0
**Status**: Ready
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Premium parents can generate a shareable PDF for their student's external tutor: "This month your student practiced X, struggled with Y, mastered Z." Turns the human-tutor segment into a Cena distribution channel.

## Scope

- PDF template with Cena branding, parent-selectable date range (default 30d).
- Contents: topics practiced, accuracy distribution, notable misconceptions (aggregate, no session-specific), mastery deltas, recommended focus areas.
- Parent chooses what to share (opt-in granularity).
- Export format: PDF (downloadable + email-to-tutor option).
- HE/AR/EN.
- Tutor-facing only; not visible to student (avoids surveillance-feeling).

## Files

- `src/backend/Cena.StudentApi/Controllers/TutorReportController.cs`
- PDF template HTML → PDF.
- `src/parent/src/pages/tutor-report.vue`
- Tests.

## Definition of Done

- PDF generates with real data.
- Locale-correct.
- Parent-opt-in granularity respected.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — aggregate only, no session-specific misconception names.
- Memory "Labels match data".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md)
