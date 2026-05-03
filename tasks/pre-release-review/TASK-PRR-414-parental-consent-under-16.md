# TASK-PRR-414: Parental consent for under-16 accounts (photo feature)

**Priority**: P0 — legal gate
**Effort**: M (1 week eng + legal)
**Lens consensus**: persona #5 compliance
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + frontend + legal; couples to [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md)
**Tags**: epic=epic-prr-j, compliance, legal-gate, priority=p0
**Status**: Not Started — **legal gate**
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Under-16 students must have verified parental consent before photo-upload feature is enabled for their account. Couples to EPIC-PRR-C parental-consent infrastructure.

## Scope

- Age gate on account.
- Under-16 → photo-upload disabled until parent-email consent.
- Parent-consent flow via linked email with revocable token.
- Revocation available any time.
- Audit log.

## Files

- Integration with EPIC-PRR-C consent service.
- `src/student/full-version/src/components/diagnostic/UnderAgeConsentGate.vue`.
- Tests.

## Definition of Done

- Under-16 accounts blocked from upload until consent.
- Parent revocation works.
- Legal signoff recorded.

## Non-negotiable references

- Israeli Privacy Law minor consent.
- PPL Amendment 13.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-383](TASK-PRR-383-privacy-card-consent-flow.md), [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md)
