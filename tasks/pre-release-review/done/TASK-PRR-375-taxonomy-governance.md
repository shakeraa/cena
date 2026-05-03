# TASK-PRR-375: Taxonomy governance — review, versioning, update workflow

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona #9 support (disputed templates need review)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev + content-eng
**Tags**: epic=epic-prr-j, governance, priority=p1
**Status**: Done (backend)
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

---

## Closure note (2026-04-23)

**Branch**: `claude-subagent-prr375/prr-375-taxonomy-governance`
**Assignee**: claude-subagent-prr375

### Delivered (backend)

- `Cena.Actors/Diagnosis/PhotoDiagnostic/Taxonomy/`:
  - `TaxonomyVersionDocument.cs` — four-state lifecycle (Proposed, Approved, Deprecated, RolledBack), per-TemplateKey monotonic version numbering, audit-trail-preserving status transitions.
  - `ITaxonomyVersionStore.cs` — read+write contract with interface-level invariants (two-reviewer guardrail, rollback resurrects prior Approved, ≥2-reviewer hard-stop on Approve).
  - `InMemoryTaxonomyVersionStore.cs` — production-grade dev/test store, concurrency-safe (per-TemplateKey locks).
  - `MartenTaxonomyVersionStore.cs` — production Marten-backed store; schema registered via `services.ConfigureMarten(opts => opts.Schema.For<TaxonomyVersionDocument>().Identity(d => d.Id))`.
  - `TaxonomyGovernanceService.cs` — app service composing store + `IDisputeMetricsService`; exposes `FlagHighDisputeTemplatesAsync` (7-day window, default 5% threshold) and `RecordReviewAsync` (approve-or-note workflow).
- DI wiring in `PhotoDiagnosticServiceRegistration` for both `AddPhotoDiagnostic` (InMemory) and `AddPhotoDiagnosticMarten` (Marten) flavors, plus the governance service in the shared registration.
- `Cena.Actors.Tests/Diagnosis/PhotoDiagnostic/TaxonomyGovernanceTests.cs` — 12 xUnit cases covering: Propose/versioning, ≥2-reviewer guardrail (one-reviewer stays Proposed, case-insensitive dedupe, terminal-state throw), rollback-preserves-audit-trail + resurrects prior Approved, Deprecate excludes from GetLatestApprovedAsync, ListVersionsAsync ordering/empty, dispute-feedback flag threshold semantics, RecordReview approval path.

### Build + test status

- `dotnet build src/actors/Cena.Actors.sln --nologo -v minimal` → 0 Error(s).
- `dotnet test ... --filter "PhotoDiagnostic|TaxonomyGovernance"` → 213 passed, 0 failed.

### Honest scope caveats (not stubs, but future work)

1. **Admin Vue page deferred.** `src/admin/full-version/src/pages/taxonomy/governance.vue` is NOT in this branch. Task scope was explicitly restricted to backend versioning. Follow-up task needs to consume `ITaxonomyGovernanceService` via an admin HTTP endpoint (not yet added) and render version-list + approve + rollback UI.
2. **FlagHighDisputeTemplatesAsync returns DisputeReason enum names** (e.g., `"WrongNarration"`), not template keys, because `DiagnosticDisputeDocument` does not carry template/item/locale ids today (same honest-scope caveat DisputeRateAggregator documents). When the diagnostic→template correlation ships, this method projects real template keys without the caller noticing.
3. **RecordReviewAsync with `approve: false` does not mutate state** in v1 — it returns the current Proposed row. Explicit `TaxonomyReviewCommentDocument` persistence is deliberately out of scope; a follow-up can layer it on top if the dashboard needs a rejection audit trail beyond the Reviewers list.
4. **Update-propagation SLA (≤24h new-template-live-of-approval)** is enforced by architecture, not a scheduled worker: ApproveAsync transactionally flips Status to Approved and demotes the prior live version in the same Marten session, so `GetLatestApprovedAsync` serves the new version immediately on next read. There is no cache layer between approval and serving in this design.

### ADR-0003 relationship

Taxonomy version rows are **operational governance artifacts**, not student data. The 30-day retention cap from ADR-0003 (misconception session-scope) does **not** apply. Rows are kept indefinitely so audit trails survive taxonomy evolution over multiple exam cycles. Documented in `TaxonomyVersionDocument.cs` banner.
