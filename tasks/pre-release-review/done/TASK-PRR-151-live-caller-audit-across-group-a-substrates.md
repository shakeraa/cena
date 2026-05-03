# TASK-PRR-151: Live-caller audit across Group A substrates (R-03/R-08/R-09/R-13/R-15/R-22)

**Priority**: P1 — strongly-recommended pre-launch (lens consensus: 2)
**Effort**: S — days
**Lens consensus**: persona-educator, persona-enterprise
**Source docs**: `retired.md R-01`, `R-03`, `R-08`, `R-09`, `R-13`, `R-15`, `R-22`
**Assignee hint**: claude-subagent-code-audit
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise, type=audit
**Status**: Done — 2026-04-20
**Source**: R-01 generalization — if the scheduler substrate is orphaned from its production caller, other Group A substrates may share the gap
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal
The scheduler-wiring gap surfaced by R-01 (substrate exists, no live production caller) may repeat across other Group A retires. Audit each of: R-03 ScaffoldingService, R-08 IrtCalibrationPipeline/BktService/EloScoring, R-09 MisconceptionDetectionService, R-13 ParentDigest/ParentalControls, R-15 CulturalContextService, R-22 Accommodations bounded context. For each: grep for production callers (excluding tests). Report substrate→caller status. Any substrate without a live caller gets a new wiring task (prr-152+ or append to candidates in audit/).

## Files
- `pre-release-review/reviews/audit/group-a-caller-audit.md` — report
- For each no-caller finding: append a candidate task to `pre-release-review/reviews/audit/appended-tasks.jsonl` (coordinate with the coverage-audit agent also writing there — use ID range prr-300+ to avoid collision)

## Definition of Done
- Markdown report with one section per Group A substrate.
- Each section: (a) file path, (b) test-only callers vs production callers, (c) verdict "wired" / "orphaned" / "partially-wired", (d) recommended action.
- Any "orphaned" substrate produces a candidate task in the append file.
- No code changes in this task — audit only.

## Reporting
complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-code-audit --result "<branch>"

---

## Non-negotiable references
- None — this is a read-only audit. If the audit surfaces findings whose remediation would touch ADR-protected seams, those go into the appended candidate tasks with the relevant ADR references.

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
- [Retired proposals](../../pre-release-review/reviews/retired.md) (R-01, R-03, R-08, R-09, R-13, R-15, R-22)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-151)
- Sibling wiring tasks: prr-148 (student-input UI), prr-149 (live caller), prr-150 (mentor override)
