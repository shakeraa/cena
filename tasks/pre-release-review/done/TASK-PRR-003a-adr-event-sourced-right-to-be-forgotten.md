# TASK-PRR-003a: ADR — Event-sourced right-to-be-forgotten policy

**Priority**: P0 — ship-blocker (lens consensus: 2; decision gate for prr-003b and several downstream tasks)
**Effort**: S — 3-5 days
**Lens consensus**: persona-privacy, persona-redteam, persona-enterprise (cross-lens handoff)
**Source docs**: `axis9_data_privacy_trust_mechanics.md:L176`, `retired.md R-09`, `retired.md R-16`, SYNTHESIS.md outstanding ADR gap #13, conflicts.md C-03
**Assignee hint**: human-architect (decision) + persona-privacy review before acceptance
**Tags**: source=pre-release-review-2026-04-20, lens=privacy, type=adr-authoring, user-decision=2026-04-20-split-with-crypto-shred-preference, decision-gate=true
**Status**: Done — 2026-04-20
**Source**: Split from prr-003 during user walkthrough 2026-04-20. Originally a single "hard-delete" task; walkthrough surfaced that Marten's append-only event store doesn't support selective event deletion, so erasure strategy is a design decision with four incompatible options — must be resolved by ADR before any implementation starts.
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition (same erasure policy shapes the decomposed aggregates; coordinate with epic's ADR)
**Split-sibling**: prr-003b (implementation)

---

## Goal

Author `docs/adr/NNNN-event-sourced-right-to-be-forgotten.md` resolving how GDPR Art 17 / Israeli PPL §14 erasure is implemented atop the Marten event store. Choose between:

| Option | Summary | Trade-off |
|---|---|---|
| **A. Crypto-shredding** | Encrypt PII fields with per-subject keys; destroy key on erasure. Events remain; data is irretrievable. | Fits Marten; requires key lifecycle infra; backup/restore edge cases |
| **B. Event-stream rewrite** | Delete affected streams, replay with PII redacted. | Semantically purest; heavy migration; risky at scale |
| **C. Out-of-band PII store** | Move free-text fields to deletable store; events hold IDs. | Keeps events pristine; adds join per read; two systems of record |
| **D. Aggregate rebuild + new stream** | Snapshot current state, delete old stream, write redacted rebuild. | Clean semantics; expensive; touches every stream per subject |

## User direction 2026-04-20 — prefer crypto-shredding (Option A)

The walkthrough produced a direction, not a mandate. Document the preference honestly in the ADR:

- **Prefer A unless a stakeholder rules it out.** Reasons: Marten-compatible (doesn't fight the architecture); audit trail preserved (regulators see the *fact* of the event, just not the PII content — strengthens defensibility for fraud investigation); one-key-one-erasure-all-streams (vs iterating streams in B/D); performance-neutral at read; reversible within retention window if an erasure is mistakenly issued.
- **Document the risks the user named**: per-field classification decisions (not every field is PII); key lifecycle policy; backup/restore edge cases (restoring a pre-erasure snapshot silently reverts the erasure — needs post-restore re-erasure runbook); Israeli PPL legal review before committing.
- **Explicit blockers to A**: if legal counsel / persona-privacy determines PPL interpretation requires physical deletion, fall back to D. Do not fall back to current read-filter — that's the status quo and it's the violation.

## Scope of this ADR

1. **Decision**: chosen option (A/B/C/D/hybrid) + rationale + rejected alternatives
2. **Field classification policy**: which fields across which event types are PII — not every string field needs encryption. Minimum: `MisconceptionDetected_V1.StudentAnswer`, `StudentId` (if not already hashed)
3. **Key lifecycle** (if A): per-subject key derivation, rotation, revocation, key-store tech choice (KMS/HSM/self-hosted)
4. **Erasure triggers**: user request (GDPR Art 17), retention expiry (ADR-0003 30-day), parental request (GDPR Art 8 / COPPA)
5. **Audit-trail requirement**: the *erasure action itself* must be logged (subject-id hash, timestamp, authorization source) without ever logging the PII content
6. **Backup interaction**: what happens when a backup predating an erasure is restored
7. **Read-path contract**: what a decryption failure / missing key returns (`[erased]` sentinel, empty string, null, throw) and how downstream code must handle it
8. **Migration plan**: how existing unencrypted events get covered — backfill encryption? leave unencrypted but flag as pre-ADR? cut-over date?

## Files

- `docs/adr/NNNN-event-sourced-right-to-be-forgotten.md` — the ADR itself (new)
- Update `docs/adr/0003-misconception-session-scope.md` — cross-reference the new ADR; note that "session-scoped, 30-day" is how ADR-0003 manifests under the chosen erasure policy
- Update `pre-release-review/reviews/conflicts.md` C-03 — mark resolved once ADR accepted
- Update SYNTHESIS.md ADR gap #13 — strike from the gaps list once ADR accepted

## Definition of Done

1. ADR file exists, format follows existing `docs/adr/00NN-*.md` pattern (Context / Decision / Consequences / Alternatives)
2. Named-stakeholder sign-off recorded in the ADR: human-architect + privacy lens (Noa-equivalent) + legal/PPL review note
3. Field-classification map committed (either inline in the ADR or as `docs/compliance/pii-field-classification.md`) — every event type across the codebase has a row listing which fields are PII
4. Backup/restore runbook written (even if brief) — `docs/runbooks/post-restore-re-erasure.md`
5. Read-path contract documented with the sentinel/exception model
6. Migration plan accepted (cut-over date OR explicit "pre-ADR events remain unencrypted" with risk acknowledgment)
7. C-03 in `pre-release-review/reviews/conflicts.md` marked resolved with a link to the accepted ADR
8. ADR gap #13 in SYNTHESIS.md struck off

## Why human, not agent

The ADR decision has legal implications under PPL and GDPR. The "prefer A" direction is engineering-reasonable, but the ADR must be signed off by a human with authority over the privacy-compliance trade-off — not produced by an agent. An agent can draft structure and trade-offs (good accelerator) but the commit must carry a human's name.

## Blocks (cannot start until 003a lands)

- **prr-003b** — implementation of the chosen approach
- **prr-015** — retention-worker registration for new PII stores (shape depends on chosen erasure model)
- **prr-158** — erasure cascade for new per-student projections
- **EPIC-PRR-C** — Parent Aggregate parental-erasure flow
- Any future feature adding a new event type that carries PII (the ADR's field-classification map is the gate)

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<ADR path + branch>"

---

## Non-negotiable references

- #2: Misconception data session-scoped, 30-day max (ADR-0003) — the new ADR implements the erasure half of this
- #8: Event-sourced DDD, files <500 LOC, no stubs in production — current RetentionWorker comment that `PurgedCount = 0` is a stub-in-production form; the ADR's purpose is to make the hard-delete path real

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

- [Split-sibling: prr-003b implementation](./TASK-PRR-003b-implement-crypto-shredding-for-misconception-events.md) — blocked on this ADR
- [Conflicts: C-03 (hard-delete vs crypto-shred)](../../pre-release-review/reviews/conflicts.md)
- [SYNTHESIS.md ADR gap #13](../../pre-release-review/reviews/SYNTHESIS.md)
- [ADR-0003 misconception session scope](../../docs/adr/0003-misconception-session-scope.md)
- [Retired R-09 misconception tagging](../../pre-release-review/reviews/retired.md)
- [Retired R-16 retention worker](../../pre-release-review/reviews/retired.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-003a)
