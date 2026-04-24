# TASK-PRR-383: Privacy one-card + consent flow (first-ever upload)

**Priority**: P0 — legal-gate
**Effort**: M (1 week eng + legal review)
**Lens consensus**: persona #2 parent (privacy-first question), #5 compliance
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + legal + backend
**Tags**: epic=epic-prr-j, privacy, compliance, priority=p0, legal-gate
**Status**: Not Started — **legal gate**
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Before first photo upload, show a one-card privacy disclosure (plain HE/AR/EN): photo deletion SLA (5 min), derived data retention (30 days), no-training, no-sharing, user-delete-anytime. Under-16 accounts require parental consent.

## Scope

- One-card modal with plain-language bullets; no legalese.
- Parental consent for under-16 accounts via linked-parent-email flow (couples to EPIC-PRR-C).
- Consent event logged; re-shown on policy material change.
- Decline → student can use other diagnostic modes (typed-steps fallback).

## Files

- `src/student/full-version/src/components/diagnostic/FirstUploadConsent.vue`
- `src/backend/Cena.StudentApi/Controllers/DiagnosticConsentController.cs`
- Legal-reviewed copy HE/AR/EN.

## Definition of Done

- Consent shown before first upload only.
- Under-16 requires parent approval; student blocked otherwise.
- Decline does not crash feature (typed-steps fallback available).
- Legal sign-off recorded.

## Non-negotiable references

- Israeli Privacy Law.
- PPL Amendment 13.
- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).
- [EPIC-PRR-C](EPIC-PRR-C-parent-aggregate-consent.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-384](TASK-PRR-384-typed-steps-alternative.md), [PRR-414](TASK-PRR-414-parental-consent-under-16.md)
