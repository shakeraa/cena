# TASK-PRR-375: Taxonomy governance — review, versioning, update workflow

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona #9 support (disputed templates need review)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + content-eng
**Tags**: epic=epic-prr-j, governance, priority=p1
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Workflow for taxonomy review, update, deprecation; integration with dispute-rate feedback.

## Scope

- Review board (2+ SMEs sign off on new/changed templates).
- Versioning + rollback.
- Dispute-rate monitoring: templates >5% dispute flagged for review.
- Update propagation: new template live in ≤24h of approval.
- Audit trail.

## Files

- `src/admin/full-version/src/pages/taxonomy/governance.vue`
- Backend version-management.

## Definition of Done

- SMEs can review + approve via admin dashboard.
- Version rollback works.
- Dispute-flag surfaces automatically.

## Non-negotiable references

- Memory "Honest not complimentary" — disputed templates acknowledged, not hidden.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-370](TASK-PRR-370-taxonomy-structure-definition.md), [PRR-392](TASK-PRR-392-disputed-diagnosis-taxonomy-feedback.md)
