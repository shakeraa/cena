# TASK-PRR-038: ADR: Right-to-be-forgotten — SUPERSEDED by prr-003a

**Priority**: N/A (superseded)
**Effort**: N/A
**Status**: **Superseded 2026-04-20** — merged into [prr-003a](./TASK-PRR-003a-adr-event-sourced-right-to-be-forgotten.md)
**Tags**: source=pre-release-review-2026-04-20, status=superseded, superseded-by=prr-003a
**Tier**: superseded

---

## Why this task is superseded

During the user walkthrough of prr-003 ("Hard-delete misconception events on erasure") on 2026-04-20, the task was split into:

- **prr-003a** — ADR-authoring (event-sourced right-to-be-forgotten, crypto-shred preference per user direction)
- **prr-003b** — Implementation (blocked-on-003a)

The scope of prr-003a is **identical** to prr-038 ("ADR on hard-delete vs crypto-shred vs aggregate-rebuild for event-sourced erasure"). prr-038 pre-dated the split and wasn't updated at the time.

Rather than maintain two identical ADR-authoring tasks, prr-038 is marked superseded. All its requirements (hard-delete vs crypto-shred vs aggregate-rebuild decision, migration plan, retention worker alignment) are already captured in prr-003a's tightened DoD.

## Action for implementers

Do **not** claim or work this task. Redirect all ADR-right-to-be-forgotten work to **[prr-003a](./TASK-PRR-003a-adr-event-sourced-right-to-be-forgotten.md)**.

## Historical record of what prr-038 originally covered

- Hard-delete vs crypto-shred vs aggregate-rebuild for event-sourced erasure
- Migration plan for pre-ADR events
- Retention worker alignment under chosen erasure model
- Lens consensus: persona-enterprise, persona-privacy

All absorbed into prr-003a.

## Reporting

This task is superseded — do not complete or fail. If a worker picks it up by mistake, close immediately with result: `"superseded-by prr-003a per user decision 2026-04-20"`.

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-038)
