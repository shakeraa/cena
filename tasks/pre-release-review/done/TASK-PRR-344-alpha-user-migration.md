# TASK-PRR-344: Alpha-user migration to paid tiers (TAIL)

**Priority**: P0 — launch-blocker
**Effort**: M (1 week)
**Lens consensus**: tail — not in original review, critical for live migration
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend-dev + support
**Tags**: epic=epic-prr-i, migration, priority=p0, launch-blocker, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Migrate existing alpha / beta / pre-paywall users to the new paid tier model with goodwill grandfathering where appropriate.

## Scope

- Audit existing user accounts: who has had access without paying, for how long, what feature-set?
- Grandfather policy: alpha users receive first 60 days Premium free + 50% off first 3 months.
- Migration email communicating changes, effective date, action required.
- Automated conversion: grace-period state → active subscription on conversion OR read-only state if ignored.
- Data preservation: all prior session history + mastery state preserved.

## Files

- `src/backend/Cena.StudentApi/Workers/AlphaUserMigrationWorker.cs`
- Migration emails HE/AR/EN.
- Admin dashboard view of migration status.

## Definition of Done

- All alpha users transitioned (migrated / grace-period / read-only).
- No data loss.
- Email delivered with opt-in confirmation link.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary" — email is honest about the change.
- Memory "No stubs — production grade".
- Israel Consumer Protection Law — 30-day notice for material service changes.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-300](TASK-PRR-300-subscription-billing-engine.md)
