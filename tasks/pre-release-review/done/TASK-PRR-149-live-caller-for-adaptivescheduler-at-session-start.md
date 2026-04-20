# TASK-PRR-149: Live caller for AdaptiveScheduler at session start

**Priority**: P1 — strongly-recommended pre-launch (lens consensus: 3)
**Effort**: M — 1-2 weeks
**Lens consensus**: persona-educator, persona-enterprise, persona-finops
**Source docs**: `axis1_pedagogy_mechanics_cena.md:L40`, `retired.md R-01` (user decision 2026-04-20)
**Assignee hint**: kimi-coder
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise, cluster=scheduler-wiring
**Status**: Done — 2026-04-20
**Source**: R-01 walk-through — scheduler substrate exists but has no production caller
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal
`AdaptiveScheduler.PrioritizeTopics` is called only from tests. Wire it into `LearningSessionActor` so every new session requests a priority plan, stores the resulting `CompressedPlan` session-scoped (ADR-0003), and exposes it via the session API for the student UI's trajectory view.

## Files
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` (call PrioritizeTopics on session start)
- `src/actors/Cena.Actors/Sessions/SessionPlanSnapshot.cs` (new VO, session-scoped, NOT persisted to StudentState)
- API endpoint `/api/session/{id}/plan` returning the current snapshot
- SignalR event `SessionPlanUpdated` when plan refreshes mid-session
- Tests: plan generated on session start; plan respects DeadlineUtc from prr-148; plan expires with session (architecture test asserting no PlanSnapshot leaks to StudentState or Marten streams)

## Definition of Done
- New session creates a plan via AdaptiveScheduler using SchedulerInputs from student config.
- Plan is session-scoped — dies with the session, not persisted to profile.
- Student UI can fetch `/api/session/{id}/plan` and SignalR updates flow.
- Architecture test passes.
- Cost gate: per-session plan generation must not issue an LLM call (scheduler is heuristic, not LLM-driven). Assert via cost-audit test.
- Full Cena.Actors.sln builds cleanly.

## Reporting
complete via: node .agentdb/kimi-queue.js complete <id> --worker kimi-coder --result "<branch>"

---

## Non-negotiable references
- ADR-0003 (misconception/session-scoped data policy — SessionPlanSnapshot must share the same lifecycle rules)
- ADR-026 (LLM routing / cost controls — scheduler plan generation stays off the LLM critical path)

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
- [Retired proposals](../../pre-release-review/reviews/retired.md) (R-01)
- [Conflicts needing decision](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-149)
- Sibling wiring tasks: prr-148 (student-input UI — upstream), prr-150 (mentor override), prr-151 (Group-A audit)
