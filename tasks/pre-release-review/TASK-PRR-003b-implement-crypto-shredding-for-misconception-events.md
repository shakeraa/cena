# TASK-PRR-003b: Implement crypto-shredding for misconception events (pending ADR-003a)

**Priority**: P0 — ship-blocker (lens consensus: 2; blocked-by prr-003a)
**Effort**: M-L — 1-3 weeks (depends on chosen ADR option)
**Lens consensus**: persona-privacy, persona-redteam
**Source docs**: `axis9_data_privacy_trust_mechanics.md:L176`, see `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs:334-357` for the read-filter gap the comment self-documents
**Assignee hint**: kimi-coder (if crypto-shred chosen) OR claude-subagent-events-rewrite (if stream-rewrite chosen)
**Tags**: source=pre-release-review-2026-04-20, lens=privacy, blocked-by=prr-003a, user-decision=2026-04-20-split-with-crypto-shred-preference
**Status**: Blocked on prr-003a (ADR)
**Source**: Split from prr-003 during user walkthrough 2026-04-20. The implementation half of the original task. Cannot start until prr-003a lands the chosen erasure model.
**Tier**: mvp
**Split-sibling**: prr-003a (ADR)

---

## Goal

Implement whatever erasure model prr-003a's ADR selects. User direction is crypto-shredding (Option A) — this task is scoped to that assumption, with a cutover note if the ADR chooses otherwise.

Replace the read-filter erasure stub (`RetentionWorker.cs:334-357` — reports `PurgedCount = 0` by design) with a real irreversible erasure path for `MisconceptionDetected_V1` and any event type carrying fields classified as PII by the ADR's field-classification map.

## Assumed implementation (Option A — crypto-shredding)

If the ADR picks a different option, rescope this task before claiming.

### Components

1. **Per-subject key store** — separate persistence (not Marten), keys derived from a root KMS/HSM key. Subject-id → AES-GCM 256 key mapping with erasure-delete semantics.
2. **Encrypted-field accessor** — write-side: encrypt classified fields before event append; read-side: `TryDecrypt` returning `[erased]` sentinel when key is missing.
3. **`EncryptedFieldAccessor` seam** — single chokepoint all event serialization and read paths go through. Architecture test enforces no direct field access bypasses this seam.
4. **Erasure + retention workers update**:
   - `ErasureWorker` (user-initiated GDPR Art 17 / PPL §14): delete subject's key → PII across all streams becomes irretrievable in one atomic operation.
   - `RetentionWorker` (time-based ADR-0003 30-day): delete subject's session-key for expired windows.
   - Both now report true counts (not hardcoded zero).
5. **Audit trail**: log the erasure action with subject-id hash, timestamp, authorization source (user request / retention timer / parental request). Never log PII content.

## Files

- `src/shared/Cena.Infrastructure/Compliance/KeyStore/` — new subnamespace
  - `ISubjectKeyStore.cs` — interface with `GetOrCreate`, `Delete`, `Exists` semantics
  - `SubjectKeyStore.cs` — implementation (backed by PostgreSQL separate table, NOT Marten events)
  - `SubjectKeyDerivation.cs` — root-key → per-subject-key derivation via HKDF
- `src/shared/Cena.Infrastructure/Compliance/EncryptedFieldAccessor.cs` — encrypt/decrypt seam
- `src/actors/Cena.Actors/Events/MisconceptionEvents.cs` — change `StudentAnswer` type from `string` to `EncryptedField<string>` (or equivalent)
- `src/shared/Cena.Infrastructure/Compliance/RetentionWorker.cs` — replace the documented-stub at lines 334-357; call `SubjectKeyStore.Delete` for expired windows; report true `PurgedCount`
- `src/shared/Cena.Infrastructure/Compliance/ErasureWorker.cs` — user-initiated erasure calls `SubjectKeyStore.Delete` across all subject's session keys
- Marten event serializer registration — hook the `EncryptedFieldAccessor` into write/read pipeline
- `tests/integration/CryptoShredding.IntegrationTests.cs` — end-to-end: write event with PII → issue erasure → attempt read → assert `[erased]` sentinel
- `tests/arch/NoDirectPiiFieldAccessTest.cs` — architecture test: no code reads `MisconceptionDetected_V1.StudentAnswer` except through `EncryptedFieldAccessor`
- `tests/integration/RetentionWorker.PurgeCount.Tests.cs` — assert `PurgedCount > 0` when expired events exist (not the current hardcoded zero)
- `docs/runbooks/post-restore-re-erasure.md` — runbook for the backup-restore edge case (also produced under 003a, dep)

## Definition of Done

1. All fields classified as PII in the ADR's map are encrypted at write time and routed through `EncryptedFieldAccessor` at read time.
2. Per-subject key store operational; key deletion is irreversible (PostgreSQL row delete with no undo path; backup policy explicit in the restore runbook).
3. `ErasureWorker` and `RetentionWorker` both report true counts. The "reports zero by design" comment at `RetentionWorker.cs:334-357` is gone. `[SIEM] SessionMisconceptionPurge` log includes actual purge count, not a claim of `cutoff` with zero counts.
4. **Integration test** (crypto-shred round-trip): write event with known PII → `ErasureWorker.Erase(subjectId)` → read same event → decrypted field returns `[erased]` sentinel → `MisconceptionDetected_V1` still queryable (event exists) but `StudentAnswer` is irretrievable.
5. **Integration test** (retention-driven erasure): write event at T-31 days → run `RetentionWorker` → attempt read → `[erased]` sentinel. Assert `summary.PurgedCount > 0`.
6. **Architecture test** green: `NoDirectPiiFieldAccessTest` scans type graph; only `EncryptedFieldAccessor` touches PII fields. No logging, serialization, LLM prompt assembly, analytics, or export path bypasses it.
7. Audit-trail entries written for every erasure action (subject-id hash, timestamp, authorization source); PII content never in logs.
8. Backup/restore runbook tested in staging: restore a pre-erasure snapshot → re-run erasure script → verify PII cannot be read.
9. Full `Cena.Actors.sln` builds cleanly.
10. All existing tests pass (some will need updates to use `EncryptedFieldAccessor` for field access in assertions).

### Migration cutover

- Existing pre-ADR `MisconceptionDetected_V1` events carrying unencrypted PII: per the ADR's migration plan (003a), either (a) backfill encryption in a one-time job, or (b) flag the pre-ADR horizon and let retention naturally expire them. The task executes whichever the ADR chose.

## Blocks (cannot claim until prr-003a lands)

Same as prr-003a's "Blocks" list — this task IS one of the blocked items.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)
- #8: Event-sourced DDD, files <500 LOC, no stubs in production

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-003)
