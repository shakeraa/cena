# ADR-0042 — ConsentAggregate as a bounded-context primitive

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), Architecture
- **Task**: prr-155
- **Related**: [ADR-0012](0012-student-actor-decomposition.md), [ADR-0038](0038-event-sourced-right-to-be-forgotten.md), [ADR-0041](0041-parent-auth-role-age-bands.md), [ADR-0001](0001-multi-institute-enrollment.md)

---

## Context

Pre-prr-155 consent state was scattered across:

- `Cena.Infrastructure.Compliance.GdprConsentManager` — Marten-document-backed writer owning the per-student consent rows (SEC-005 origin).
- Ad-hoc `IsMinor<16` gates sprinkled in `MeGdprEndpoints.cs:529` and downstream consumers.
- No event-sourced history — every change overwrote the document in place, so "what consent did this student have on exam day?" was a query the system could not answer without reading audit-log side-tables.

That layout has three problems:

1. **ADR-0012 decomposition pressure.** The StudentActor god-aggregate is being broken up into per-bounded-context aggregates; consent is a natural seam (its lifecycle, authorisation rules, and audit requirements are orthogonal to pedagogy).
2. **ADR-0041 age-band matrix needs a stream.** Four bands × five roles = a non-trivial authorisation matrix that must be enforced at the write path, not re-derived at every read site. A document-only store cannot naturally carry "who authorised this, under what role" across time.
3. **ADR-0038 erasure contract.** Consent events carry PII (subject IDs, actor IDs, purpose strings). Crypto-shredding requires that every PII field round-trip through `EncryptedFieldAccessor`. Refactoring every caller of `GdprConsentManager` to handle encryption would spread the contract; a new aggregate with encryption baked into its command handlers keeps it in one place.

## Decision

Introduce a new **`ConsentAggregate`** under `src/actors/Cena.Actors/Consent/` as the authoritative consent primitive. `GdprConsentManager` is refactored to a **thin read-facade + shadow-write adapter** that preserves the existing public API contract (every existing consumer of `IGdprConsentManager` keeps working without changes).

### Bounded-context shape

- **Stream key**: `consent-{subjectId}` — the subject is either a student OR a parent (independent streams).
- **Events** (V1):
  - `ConsentGranted_V1 (subjectId*, purpose, scope, grantedByRole, grantedByActorId*, grantedAt, expiresAt)`
  - `ConsentRevoked_V1 (subjectId*, purpose, revokedByRole, revokedByActorId*, revokedAt, reason)`
  - `ConsentPurposeAdded_V1 (consentId, newPurpose, addedByRole, addedAt)`
  - `ConsentReviewedByParent_V1 (studentSubjectId*, parentActorId*, purposesReviewed, outcome, reviewedAt)`
  - `*` marks fields encrypted via `EncryptedFieldAccessor` per ADR-0038.
- **State**: folded map of `ConsentPurpose → ConsentGrantInfo`, plus the most-recent parent-review record.
- **Commands**: `GrantConsent`, `RevokeConsent`, `AddPurpose`, `RecordParentReview` — each gate on `AgeBandAuthorizationRules` before producing events.

### Age-band authorisation at the command layer

`AgeBandAuthorizationRules.CanActorGrant(purpose, actorRole, subjectBand)` is a pure function encoding the ADR-0041 matrix. It refuses:

- Under13 self-grants (COPPA VPC violation)
- Teen13to15 student self-grants of durable-data purposes (parent consent required)
- Teen16to17 parent grants on behalf of the student (PPA minor-dignity)
- Adult parent grants without a fresh adult-consent flow
- All Teacher grants (teacher is not a consent authority)
- All System grants (system is a compliance-ops role, not a grant authority)

Revoke follows a strictly-more-permissive matrix: Teen13to15 students may revoke (even durable) purposes they cannot grant, System + Admin may revoke for compliance operations in every band, and Parent may revoke on legal-minor grounds for Teen16to17.

The command layer throws `ConsentAuthorizationException` on refusal; there is no silent-deny path.

### Facade contract

`IGdprConsentManager` is unchanged. `GdprConsentManager` gains an optional `IConsentAggregateWriter` constructor dependency; when registered via `AddConsentAggregate()`, every grant/revoke/change-record operation ALSO lands as an event in the `ConsentAggregate` stream. When unregistered, the manager behaves exactly as pre-prr-155 (document-only). Existing tests that don't wire the adapter see no behaviour change.

This shadow-write discipline is the same migration pattern ADR-0012 uses for the LearningSession extraction: write to both the legacy seam and the new primitive, cut over readers in a later phase.

### Rejected alternatives

- **Full replacement of GdprConsentManager.** Rewriting every consumer (`MeGdprEndpoints`, `ConsentEnforcementMiddleware`, `RequiresConsentAttribute`, `FocusAnalyticsService`, `TestGdprConsentManager` in ConsentEnforcementTests) is a large blast radius for a migration-phase task. Shadow-write defers the cutover.
- **Aggregate owned by Cena.Infrastructure.** Would place event-sourced domain logic in the cross-cutting infrastructure project, violating the bounded-context invariant ADR-0012 is trying to enforce. The aggregate belongs in Cena.Actors where the other bounded contexts live.
- **Aggregate writes as the sole source of truth.** Requires the Marten consent-event projection to be wired before any read site works, which would be a breaking change if the migration stalls. Shadow-write + legacy-document read preserves progress across partial merges.

## Consequences

### Positive

- Authorisation matrix is testable in isolation as a pure function. ADR-0041 compliance is one unit-test file away, not a scattered sweep of endpoint handlers.
- New consent events carry PII-encrypted subject and actor IDs by construction; crypto-shred tombstone erases them across the whole consent stream in one key deletion (ADR-0038 contract honoured).
- ConsentAggregate is self-contained; architecture test enforces that no non-Consent aggregate imports its internals. StudentActor cannot re-embed consent state.
- Facade preserves the existing public surface verbatim — no endpoint changes, no DTO churn, no cross-agent merge conflicts beyond the Program.cs DI line we added.

### Negative

- Two write paths (legacy document + aggregate stream) until the Sprint 2 cutover. Operators must understand both until the legacy store is retired.
- The mapping table between legacy `ProcessingPurpose` and aggregate `ConsentPurpose` is a hand-maintained translation. Additions on either side require a mapping-table update; an architecture test would tighten this, tracked as a follow-up.
- `AgeBandAuthorizationRules` encodes the matrix as a `switch` expression. Enum-growth will push it toward a data-driven representation; fine for today's 4 bands × 5 roles × ~9 purposes, would need refactor at 3× that.

### Neutral

- Aggregate store currently backed by `InMemoryConsentAggregateStore`. A Marten-backed variant is tracked under EPIC-PRR-A Sprint 2 (follow-up ticket). The interface is narrow enough that the swap is mechanical.

## Open questions / pending items

1. **Bidirectional mapping completeness.** Not every `ConsentPurpose` has a legacy `ProcessingPurpose` equivalent (`ParentDigest`, `TeacherShare`, `ExternalIntegration` are aggregate-only). Callers that need those purposes must use `ConsentAggregate` directly; the facade cannot surface them via the legacy DTO.
2. **Read-side projection.** `GetConsentsAsync` still reads the legacy Marten document. A future change will project from the aggregate stream instead, once the Sprint 2 Marten-backed store lands.
3. **Cross-review gate (EPIC-PRR-C).** The EPIC-PRR-C owner must sign off on the V1 event schema via PR comment before merge — this is a non-negotiable per the EPIC-PRR-C coordination contract.

## References

- [ADR-0012](0012-student-actor-decomposition.md) — StudentActor decomposition context; consent is one of the target bounded contexts.
- [ADR-0038](0038-event-sourced-right-to-be-forgotten.md) — crypto-shred contract for PII fields on events.
- [ADR-0041](0041-parent-auth-role-age-bands.md) — age-band + role matrix that `AgeBandAuthorizationRules` encodes.
- [ADR-0001](0001-multi-institute-enrollment.md) — multi-institute tenant boundary; future aggregate work must scope grants per institute.
- `docs/tasks/pre-release-review/TASK-PRR-155-design-consentaggregate-events.md` — task body.
