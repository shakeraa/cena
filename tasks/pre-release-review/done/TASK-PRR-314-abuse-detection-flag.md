# TASK-PRR-314: Abuse detection — flag users >200 uploads/mo for review

**Priority**: P1
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO (account-sharing risk)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-i, abuse-detection, priority=p1
**Status**: Ready (post-launch if capacity tight)
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Flag users with >200 diagnostic uploads/month for account-sharing / abuse investigation. No auto-action (review-queue only).

## Scope

- Daily batch job scans diagnostic upload counts.
- Users exceeding threshold added to `admin_review_queue`.
- Admin dashboard surfaces flagged users with upload timing distribution, device fingerprint variety.
- No user-facing action until human review.
- Legitimate heavy users (e.g., exam-week cram) dismissed with note; actual abuse leads to warning, then cancel.

## Files

- `src/backend/Cena.StudentApi/Workers/AbuseDetectionWorker.cs`
- `src/admin/full-version/src/pages/moderation/account-review.vue`
- Tests.

## Definition of Done

- Users >200/mo surface in review queue.
- Admin can dismiss or escalate.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary" — notes reflect actual pattern.
- Privacy: review queue access logged.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-312](TASK-PRR-312-per-tier-photo-diagnostic-caps.md), [PRR-403](TASK-PRR-403-abuse-detection-uploads.md)
