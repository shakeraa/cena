# TASK-PRR-402: Hard-cap threshold → contact-support UX (legitimate heavy use)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #10 CFO (hard cap has human-review path)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + content
**Tags**: epic=epic-prr-j, tier-enforcement, priority=p0
**Status**: Partial — backend support-ticket service + 3 endpoints (student open / admin resolve / admin reject) + 26 tests shipped; Vue modal and account-screen banner deferred on frontend gate
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

## Audit (2026-04-23)

Per-session audit: PRR-402 backend surface already shipped. All three
DoD backend items are green.

### Already shipped

- [HardCapSupportService.cs](../../src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/HardCapSupportService.cs)
  — `CreateTicketAsync`, `GrantExtensionAsync`, `DeclineAsync`, list-
  open. Premium-only; one open ticket per (student, month) to prevent
  queue flooding; extension count validated in 1..100 abuse window.
- [HardCapSupportTicketDocument.cs](../../src/actors/Cena.Actors/Diagnosis/PhotoDiagnostic/HardCapSupportTicketDocument.cs)
  — Marten doc with upload-count-at-request + status + monthly window.
- [HardCapSupportTicketEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/HardCapSupportTicketEndpoints.cs)
  — three routes:
  - `POST /api/me/hard-cap-support-tickets` (authenticated student)
    — derives student id from JWT claim (never body), refuses to
    open unless `PhotoDiagnosticQuotaGate` reports HardCapReached
    for the caller now, so the Open queue stays scoped.
  - `POST /api/admin/hard-cap-support-tickets/{id}/resolve` (AdminOnly)
    — grants extension count.
  - `POST /api/admin/hard-cap-support-tickets/{id}/reject` (AdminOnly).
- Tests: `HardCapSupportServiceTests.cs` — 26 tests covering
  Premium-only gating, duplicate-open prevention, extension bounds,
  grant + decline flows. All pass.
- Shipgate discipline: response payloads carry error codes plus ticket
  ids only — no scarcity copy, no countdowns (GD-004 scanner).

### What is deferred (frontend gate)

- **`src/student/full-version/src/components/diagnostic/HardCapContactSupport.vue`**
  — modal and account-screen banner that surfaces when the quota
  gate reports `HardCapReached`. Consumes the student endpoint;
  banner copy comes from i18n bundles (warm exam-week framing per
  ADR-0048, no time-pressure language).
- **Admin triage UI** — Vue admin page that lists Open tickets and
  calls `/resolve` or `/reject`. Endpoint contract frozen.

Closing as **Partial** per memory "Honest not complimentary": every
backend DoD item is green; the remaining work is two Vue pages that
consume frozen endpoint contracts.
