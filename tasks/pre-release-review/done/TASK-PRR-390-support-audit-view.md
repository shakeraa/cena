# TASK-PRR-390: Support audit view (photo + OCR + CAS + narration + template)

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #9 support
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + admin-frontend + support lead
**Tags**: epic=epic-prr-j, support, priority=p0
**Status**: Partial — admin detail endpoint `GET /api/admin/diagnostic-disputes/{id}/audit` + envelope DTO + SIEM audit trace + 6 tests shipped 2026-04-23; photo-hash capture + outcome-snapshot writer deferred on PRR-412 (photo-delete SLA) + PRR-350 (upstream MSP pipeline) downstream dependencies
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Support agent can inspect a disputed diagnostic: original photo (must be captured to hash-ledger before photo-delete SLA fires), OCR output, CAS chain, LLM narration, template selected, student's dispute reason.

## Scope

- Tension point: photo is deleted at 5 min (PRR-412); audit view needs evidence after deletion. Solution: **cryptographic hash of photo at upload + capture support-retrievable snapshot for the dispute window only** (agent-approved persisted evidence).
- For disputed diagnostics, snapshot persists 30 days (aligned with misconception session-scope).
- Agent view in admin dashboard.
- Access logged; agent role required.

## Files

- `src/admin/full-version/src/pages/support/diagnostic-audit.vue`
- Backend snapshot service.
- Privacy policy update to disclose dispute-snapshot retention.

## Definition of Done

- Agent can view photo + chain for disputed diagnostic.
- Non-disputed diagnostics have no photo retained.
- Access logged; non-agent rejected.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md).
- Israeli Privacy Law — snapshot retention must be disclosed.
- PPL Amendment 13.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-385](TASK-PRR-385-dispute-button.md), [PRR-391](TASK-PRR-391-auto-credit-confirmed-errors.md), [PRR-412](TASK-PRR-412-photo-deletion-sla.md)

## What shipped (2026-04-23)

Admin detail read — support agent's single-dispute audit surface:

- [DiagnosticAuditEndpoints.cs](../../src/api/Cena.Admin.Api.Host/Endpoints/DiagnosticAuditEndpoints.cs)
  — `GET /api/admin/diagnostic-disputes/{disputeId}/audit`:
  - `CenaAuthPolicies.AdminOnly` guard (matches
    `DisputeMetricsEndpoints` + `DiagnosticCreditEndpoints`).
  - Validates `disputeId` → 400 on empty.
  - Loads the dispute via `IDiagnosticDisputeService.GetAsync` → 404 on
    unknown id (so the endpoint is not an id-enumeration oracle for
    disputes that never existed).
  - Emits a `[SIEM] DiagnosticAuditRead` structured log capturing the
    admin subject-id prefix → disputeId → diagnosticId mapping so SOC
    can correlate audit reads end-to-end. The existing
    `AdminActionAuditMiddleware` on `/api/admin/**` already logs the
    HTTP-level read; this structured entry adds the correlation id.
  - Returns `DiagnosticAuditResponseDto` with dispute metadata
    populated (DiagnosticId, StudentSubjectIdHash, Reason,
    StudentComment, Status, SubmittedAtUtc, ReviewedAtUtc,
    ReviewerNote) **+ v1-nullable fields explicitly nulled**:
    PhotoHash, CapturedOutcomeJson, MatchedTemplateId,
    FirstWrongStepNumber.
  - `DeferredFields` response array surfaces the v1 gaps by name so
    the Vue admin page can render a "pending upstream capture" badge
    against the null fields rather than silently rendering them as
    blank values (honest UX over false completeness).
- [DiagnosticAuditResponseDto](../../src/api/Cena.Admin.Api.Host/Endpoints/DiagnosticAuditEndpoints.cs)
  — wire DTO with `JsonPropertyName` bindings; no cleartext PII.
- Wired into `Cena.Admin.Api.Host/Program.cs` via
  `MapDiagnosticAuditEndpoints(app)`.
- 6 tests in
  [DiagnosticAuditEndpointTests.cs](../../src/actors/Cena.Actors.Tests/Diagnosis/PhotoDiagnostic/DiagnosticAuditEndpointTests.cs):
  - Missing disputeId → 400.
  - Unknown disputeId → 404.
  - Known disputeId → 200 with populated metadata.
  - Deferred fields are null in v1 response (no-stubs regression guard).
  - `DeferredFields` list matches the canonical v1 sentinel
    (UI contract guard — a future dev who removes a field from the
    list but not from the response would fail this test).
  - Reviewed dispute surfaces `ReviewedAtUtc` + `ReviewerNote` +
    status transition (e.g. `Upheld`).

Full `Cena.Actors.sln` build green; 6/6 new tests pass.

### DoD coverage

- ✅ **Access logged; non-agent rejected.** `AdminOnly` policy rejects
  non-admin tokens at the authorization layer; SIEM structured log
  captures every successful read.
- ✅ **Full sln green.** Build clean, 0 errors.
- ⚠ **Agent can view photo + chain for disputed diagnostic.** Partial:
  agent can view the dispute envelope (reason, comment, timestamps,
  reviewer note, diagnostic-id pointer). Photo hash + CAS chain +
  narration + template are wired as v1 nullables with an honest
  `DeferredFields` surfacing.
- **Non-disputed diagnostics have no photo retained.** Not a code
  change this PR makes — already enforced by the 5-min
  `PhotoDeletionAuditJob` (PRR-412); the audit endpoint reads only
  disputed diagnostics (lookup-by-dispute-id, never a bulk index),
  so a non-disputed diagnostic is un-addressable through this path.

## What is deferred

- **Photo hash capture at upload time.** Requires a hook in the
  photo-intake pipeline that computes SHA-256 of the uploaded image
  and records it alongside the diagnostic id before the 5-min photo
  delete fires (PRR-412). Integration point: when the photo-
  diagnostic endpoint wires up (currently blocked on PRR-350 →
  EPIC-PRR-H §3.1 MSP intake), add a capture step right before the
  image is queued for delete. The response DTO's `PhotoHash` field
  fills in at that point with no endpoint change.
- **Full `DiagnosticOutcome` snapshot at diagnostic-completion time.**
  Requires a short-lived outcome-recent cache that the photo-
  diagnostic endpoint writes to on every successful run, which the
  dispute capture then promotes to a 30-day retention doc. Same
  upstream-wiring dependency as the photo hash.
- **30-day retention worker.** Ships when the cached outcome
  payload is real — today with no outcome to age out, an empty
  retention worker is speculative interface-padding (memory "No
  stubs — production grade" bans that).
- **Vue admin diagnostic-audit page**
  (`src/admin/full-version/src/pages/support/diagnostic-audit.vue`)
  — frontend work consuming `GET .../audit`; the response DTO's
  `DeferredFields` list is the UI contract for rendering "pending
  upstream capture" state.
- **Privacy-policy update** disclosing dispute-snapshot retention —
  legal-counsel gate; ships alongside the upstream capture since
  without captured content there is nothing to retain.
- **SUPPORT_AGENT role below ADMIN.** v1 uses AdminOnly matching
  the other admin dispute endpoints; a dedicated support-agent
  policy lands when the role catalogue expands.

Closing as **Partial** per memory "Honest not complimentary": the
admin detail endpoint is live, auth-guarded, SIEM-logged, and
contract-frozen for Vue consumption; rich artefact fields (photo /
chain / narration / template) wire into the same response DTO the
moment the upstream capture writer lands — zero endpoint churn
needed then.
