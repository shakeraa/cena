# TASK-PRR-150: Mentor/tutor override aggregate for schedule

**Priority**: P2 — post-launch improvement (lens consensus: 3)
**Effort**: L — 2-4 weeks
**Lens consensus**: persona-educator, persona-enterprise, persona-ministry
**Source docs**: `axis1_pedagogy_mechanics_cena.md:L40`, `retired.md R-01`, `cena_axis5_teacher_workflow_features.md`
**Assignee hint**: human-architect (design) + claude-subagent-teacher-override (implementation)
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise, cluster=scheduler-wiring
**Status**: Not Started
**Source**: R-01 walk-through — teacher/mentor cannot currently influence a student's schedule content
**Tier**: post-launch
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal
No mentor-override mechanism exists for the schedule today. `MethodologyAssignment` overrides *teaching style*, not *schedule content*. Design and implement a new event-sourced aggregate allowing a teacher/mentor to (a) pin a specific topic for a student's next N sessions, (b) adjust that student's weekly budget, (c) override the motivation profile for specific session types. Fully auditable (teacher action log). Respects tenant scoping.

## Files
- `docs/adr/NNNN-teacher-schedule-override.md` — new ADR
- `src/actors/Cena.Actors/Teacher/ScheduleOverride/` — new bounded context
- `src/actors/Cena.Actors/Teacher/ScheduleOverride/TeacherOverrideAggregate.cs`
- `src/actors/Cena.Actors/Teacher/ScheduleOverride/Events/` — PinTopicRequested_V1, BudgetAdjusted_V1, MotivationProfileOverridden_V1
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — consult override aggregate when building SchedulerInputs
- Admin UI in `src/admin/full-version/src/views/apps/teacher/StudentScheduleOverride.vue`
- Tests: tenant-scope enforcement (teacher at institute A cannot override student at institute B), audit trail completeness, precedence rules (override > student input > scheduler default)

## Definition of Done
- ADR accepted.
- Aggregate ships with full event sourcing, tenant-scoped, audited.
- Teacher UI allows pin/unpin/budget-adjust with confirmation.
- Scheduler respects overrides with documented precedence.
- Arch test: no cross-tenant read path.
- AdminActionAuditMiddleware captures every override action.
- Full Cena.Actors.sln builds cleanly.

## Reporting
complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-teacher-override --result "<branch>"

---

## Non-negotiable references
- ADR-0001 (tenant isolation — override aggregate is tenant-scoped; cross-institute reads forbidden)
- ADR-0003 (session-scope discipline — override state is long-lived teacher intent, not session-scoped student state; keep the boundary clean)

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
- [Retired proposals](../../../pre-release-review/reviews/retired.md) (R-01)
- [Conflicts needing decision](../../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../../pre-release-review/reviews/tasks.jsonl) (id: prr-150)
- Sibling wiring tasks: prr-148 (student-input UI), prr-149 (live caller — override consumed here), prr-151 (Group-A audit)
