# TASK-PRR-186: Manual triage — 4 partial-match orphans + 16 partial-match handoffs

**Priority**: P2 — post-launch polish (lens consensus: 1, coordinator-flagged)
**Effort**: S — 1-2 days
**Lens consensus**: coordinator (derived from tight-match audit 2026-04-20)
**Source docs**: `pre-release-review/reviews/audit/tight-match-orphans.md` (partial-match section), `pre-release-review/reviews/audit/tight-match-handoffs.md` (partial-match section)
**Assignee hint**: human-architect (explicitly NOT an agent — matching heuristics already tried, signal is ambiguous enough to require human judgment)
**Tags**: source=pre-release-review-2026-04-20, type=triage, assignee=human, do-not-agent
**Status**: Not Started
**Source**: Synthesized from the tight-match audit 2026-04-20. The first audit's fuzzy match flagged 141 orphans + 118 handoffs; tight-match re-matching resolved most into existing tasks or confirmed-orphans, but left 20 items with matching scores 3-4 (neither confident merge nor confident orphan). Coordinator decision 2026-04-20: these require eyeball review, not more agent time.
**Tier**: post-launch

---

## Goal

Manually triage the 20 items in the tight-match audit's `partial-match` bucket — agent matching was insufficient and each needs human judgment against the canonical 185-task backlog to classify and resolve.

## Items to triage (20 total)

- **4 partial-match orphans** — from `audit/tight-match-orphans.md`. These scored 3-4 under composite-signature matching (code-path + source-doc + concept). A match exists but is not confident enough to auto-merge.
- **16 partial-match handoffs** — from `audit/tight-match-handoffs.md`. The recipient persona's YAML has related content but not obviously the handoff topic; target coverage is ambiguous.

## Triage decision model (apply per item)

For each of the 20 items, classify as exactly one:

| Classification | Meaning | Follow-up action |
|---|---|---|
| `merge-into:prr-NNN` | Genuine duplicate of an existing task | Annotate in target task's queue_body as an absorbed finding; note in triage log |
| `promote-as-new` | Genuine orphan not covered elsewhere | Author new prr-187+ task with full template + senior-architect protocol |
| `fold-into:prr-019` or `fold-into:prr-040` | Narrow scanner/copy item (banned term, euphemism, aria-live hint) | Add vocabulary to the ship-gate scanner task's scope; do not create standalone task |
| `false-orphan` | Already covered under different bounded-context vocabulary | Document the covering task/retire/conflict in triage log |
| `defer-to-epic` | Will be absorbed by a pending epic (e.g. ADR-0012 StudentActor split, ADR-026 3-tier routing, Parent Aggregate) | Link to the epic; don't promote yet |

## Files

- `pre-release-review/reviews/audit/tight-match-orphans.md` — read the partial-match rows (4 entries)
- `pre-release-review/reviews/audit/tight-match-handoffs.md` — read the partial-match rows (16 entries)
- `pre-release-review/reviews/tasks.jsonl` — canonical backlog for lookup when classifying `merge-into` / `false-orphan`
- `pre-release-review/reviews/retired.md` — for `subsumed-by-retire` lookups
- `pre-release-review/reviews/conflicts.md` — for `defer-to-conflict` lookups
- `pre-release-review/reviews/triage-partial-matches.md` **(new — write outcomes here)**
- `tasks/pre-release-review/TASK-PRR-NNN-*.md` — any new task files for `promote-as-new` classifications

## Definition of Done

- `triage-partial-matches.md` exists with one entry per item (20 total). Each entry contains: the original item ID (O-NNN or H-NNN), the classification, and a one-line justification citing the specific task, retire, or reasoning behind the call.
- Any `promote-as-new` items have a new prr-187+ entry appended to `tasks.jsonl` AND a task file materialized in `tasks/pre-release-review/` following the existing template (including the senior-architect protocol section).
- Any `merge-into` items are cited in the target task's queue_body (or a task-level comment) so the absorbed finding has provenance.
- Any `fold-into` items expand the scope/vocabulary of prr-019 or prr-040 — edit those task files' queue_body and the corresponding line in tasks.jsonl.
- Any `false-orphan` items are documented in the triage log with the exact prr-NNN / R-NN / C-NN that covers them.
- `SYNTHESIS.md` bottom count updated if new tasks were promoted (185 → NNN).
- `tasks/pre-release-review/README.md` section counts updated if P1 or P2 totals changed.
- If zero items require new tasks, triage log still exists with 20 entries explaining each classification.

## Why a human

Handoff semantics ("persona A asks persona B to look at X") do not map cleanly to task semantics ("build X"). A handoff may be:

- Already addressed by persona B under different wording (false-orphan)
- A cross-cutting concern requiring ADR-level resolution (defer-to-epic)
- A narrow policy item that's scanner vocabulary, not engineering (fold-into)
- A genuinely missed feature (promote-as-new)

Distinguishing these requires reading both the raw persona YAML and the current canonical backlog with full context — a budgetable 1-2 day human pass, not another agent hop. Further agent matching would produce the same partial-match bucket under different thresholds.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<triage summary: N merge-into, N promote-as-new, N fold-into, N false-orphan, N defer-to-epic; new task IDs if any; branch>"

---

## Non-negotiable references

None explicitly bound; all baseline non-negotiables from CLAUDE.md still apply to any `promote-as-new` task authored during triage.

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

**Reference**: full protocol and its rationale live in [`/tasks/pre-release-review/README.md`](../../../tasks/pre-release-review/README.md#implementation-protocol-senior-architect) (this section is duplicated there for skimming convenience).

---

## Related

- [Full synthesis](../../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../../pre-release-review/reviews/retired.md)
- [Conflicts needing decision](../../../pre-release-review/reviews/conflicts.md)
- [Tight-match orphans](../../../pre-release-review/reviews/audit/tight-match-orphans.md)
- [Tight-match handoffs](../../../pre-release-review/reviews/audit/tight-match-handoffs.md)
- [Canonical task JSON](../../../pre-release-review/reviews/tasks.jsonl) (id: prr-186)
