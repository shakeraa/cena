# TASK-PRR-402: Hard-cap threshold → contact-support UX (legitimate heavy use)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO (hard cap has human-review path)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + content
**Tags**: epic=epic-prr-j, tier-enforcement, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

At 300-upload hard cap, block further uploads for the month but surface a "contact support" CTA for legitimate exam-week-cram edge cases. Not punitive.

## Scope

- Modal + account-screen banner when hard cap hit.
- One-click ticket: pre-populated with context (date range, tier, cap reached).
- Support can grant a one-time extension.

## Files

- `src/student/full-version/src/components/diagnostic/HardCapContactSupport.vue`
- Support ticket creation endpoint.

## Definition of Done

- Hard cap surfaces contact-support.
- Ticket creation works.
- Support can grant extension.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "Ship-gate banned terms".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-400](TASK-PRR-400-per-tier-upload-counter.md)
