# TASK-PRR-155: Design ConsentAggregate + events

**Priority**: P1 — strongly-recommended pre-launch (lens consensus: 1)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-enterprise
**Source docs**: `axis9_data_privacy_trust_mechanics.md:L111`
**Assignee hint**: claude-code
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise, origin=tight-match-audit, src-audit-id=O-062
**Status**: Not Started
**Source**: tight-match audit 2026-04-20 (O-062)
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

Design `ConsentAggregate` + its events as new bounded context replacing scattered consent state on `StudentActor` / `GdprConsentManager`. Lives in EPIC-PRR-A aggregate-design substrate; event schema reviewed by EPIC-PRR-C owner before commit.

### User decision 2026-04-20 — design sketch + blocked-by + cross-review gate

**Blocked-by (hard)**:

- [prr-003a](./TASK-PRR-003a-adr-event-sourced-right-to-be-forgotten.md) — erasure model must be known before event schema commit; consent events carry subject-id + purpose strings = PII under erasure-field-classification map
- prr-014 (EPIC-PRR-C parent auth role ADR) — age-band rules govern which actor roles can grant/revoke which purposes

**Event schema sketch** (minimum 4 — ADR refines):

| Event | Payload (minimum) |
|---|---|
| `ConsentGranted_V1` | subjectId, purpose, scope, grantedByRole, grantedByActorId, timestamp, expiresAt |
| `ConsentRevoked_V1` | subjectId, purpose, revokedByRole, revokedByActorId, timestamp, reason |
| `ConsentPurposeAdded_V1` | consentId, newPurpose, addedByRole, timestamp |
| `ConsentReviewedByParent_V1` | studentSubjectId, parentActorId, purposesReviewed, outcome, timestamp |

**Stream key**: `consent-{subjectId}` — subject is student OR parent (independent streams).

**Relation to existing [`GdprConsentManager`](../../src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs)**: ConsentAggregate is new primitive (source of truth); GdprConsentManager refactored to thin read-side facade projecting events into existing DTO shape. Preserves API contract with current consumers.

**Age-band interaction**: every event carries `ActorRole`; aggregate command-handler enforces age-band authorization invariants from prr-014. A 14-year-old cannot grant consent for purposes that require a parent. Invariant enforced in aggregate code, not at API edge.

**Cross-review gate**: EPIC-PRR-C owner must sign off on event schema via PR comment before merge. Non-negotiable per EPIC-PRR-C coordination contract.

## Files

- `src/actors/Cena.Actors/Consent/ConsentAggregate.cs` (new)
- `src/actors/Cena.Actors/Consent/Events/` — 4 events above
- `src/actors/Cena.Actors/Consent/ConsentCommands.cs`
- `src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs` (refactor to facade)
- `docs/adr/NNNN-consent-aggregate.md` (micro-ADR)
- `tests/unit/Consent/` + `tests/integration/Consent/`
- `tests/architecture/ConsentAggregateNoProfileCouplingTest.cs`

## Definition of Done

1. prr-003a accepted (blocker) — erasure model known
2. prr-014 accepted (blocker) — age-band rules known
3. ConsentAggregate + 4 events implemented; command handlers enforce age-band invariants
4. GdprConsentManager refactored to facade; DTO API unchanged (arch test asserts)
5. PII fields wired through `EncryptedFieldAccessor` per prr-003a erasure contract
6. **EPIC-PRR-C owner PR sign-off** on event schema recorded in merge commit
7. No consent state on StudentActor/StudentProfile post-merge (arch test)
8. Full `Cena.Actors.sln` builds cleanly; all tests pass
9. Micro-ADR committed cross-referencing prr-003a + prr-014

## Rolls up into EPIC-PRR-A Sprint 2-3

Coordinate with Parent Aggregate work in EPIC-PRR-C (same sprint window).

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-consent-aggregate --result "<branch>"

---

## Non-negotiable references
- None explicitly bound; all baseline non-negotiables from CLAUDE.md still apply.

## Implementation Protocol — Senior Architect

Implementation of this task must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this task exist?** Read the source-doc lines cited above and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised it. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole task.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What's the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping — see user memory "Verify data E2E" and "Labels match data".
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the task as-scoped is wrong in light of what you find, **push back** and propose the correction via a task comment — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly: `node .agentdb/kimi-queue.js fail <task-id> --worker <you> --reason "<specific blocker, not 'hard'>"`.
- Do not silently reduce scope. Do not skip a non-negotiable. Do not bypass a hook with `--no-verify`.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas' cross-lens handoffs addressed or explicitly deferred with a new task ID.

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-155)
- [Tight-match audit confirmed-orphans](../../pre-release-review/reviews/audit/confirmed-orphans.jsonl)
