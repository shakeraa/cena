# TASK-PRR-002: ADR-0012 StudentActor split — gate pedagogy + SRL features

**Priority**: P0 — ship-blocker (lens consensus: 5)
**Effort**: L — 2-4 weeks
**Lens consensus**: persona-enterprise, persona-privacy, persona-cogsci, persona-ethics, persona-ministry
**Source docs**: `axis1_pedagogy_mechanics_cena.md:L44`, `axis2_motivation_self_regulation_findings.md:L43`
**Assignee hint**: human-architect
**Tags**: source=pre-release-review-2026-04-20, lens=enterprise
**Status**: In Progress — Sprint 1 kickoff deliverables complete 2026-04-20 (ADR Schedule Lock, arch tests green, epic cross-refs); Sprint 1 code begins 2026-04-27 per locked schedule
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: mvp
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

Lock the schedule on existing [ADR-0012](../../docs/adr/0012-aggregate-decomposition.md) and bring enforcement online. The ADR itself was accepted 2026-04-14 with implementation deferred — this task ends the deferral.

### Code-reality correction (2026-04-20 verification)

ADR-0012 **already exists** (171 lines, 3 bounded contexts defined, 6-sprint plan). Missing from it: calendar dates, 500-LOC arch test, no-new-handler arch test, first-aggregate decision. StudentActor has grown from ~2,969L at ADR acceptance to **3,532L by 2026-04-20** (6 days) — deferral cost is compounding.

### User decision 2026-04-20 — 5 Sprint-1 kickoff decisions (all adopted)

1. **Sprint 1 start**: 2026-04-27. Full schedule: 6 weeks, completion 2026-06-07.
2. **First aggregate**: LearningSession (lowest coupling, highest new-feature magnet).
3. **500-LOC test**: git-blame-aware grandfather whitelist; PRs may only lower baselines, never raise; new files ≤500L.
4. **No new StudentActor state**: arch test caps event-handler count in `Students/StudentActor*.cs` at 2026-04-20 baseline.
5. **ConsentAggregate**: designed in EPIC-PRR-A with event-schema review by EPIC-PRR-C owner.

See [ADR-0012 Schedule Lock section](../../docs/adr/0012-aggregate-decomposition.md) for full locked schedule, enforcement specification, grandfather baselines, and kill switch.

## Files

- `docs/adr/0012-aggregate-decomposition.md` — **DONE 2026-04-20** (Schedule Lock section appended)
- `tests/architecture/FileSize500LocTest.cs` (new) — grandfather-whitelist file-size gate
- `tests/architecture/FileSize500LocBaseline.yml` (new) — grandfathered baselines
- `tests/architecture/NoNewStudentActorStateTest.cs` (new) — event-handler count baseline gate
- `tests/architecture/NoNewStudentActorStateBaseline.yml` (new) — handler-count baseline
- `.github/workflows/architecture-gates.yml` — wire both arch tests into CI (may already exist for similar tests)
- Sprint 1 branch: `claude-code/epic-prr-a-sprint-1` — interface contracts + LearningSession extraction begins 2026-04-27

## Definition of Done

1. ADR-0012 Schedule Lock section merged ✅ (2026-04-20)
2. `FileSize500LocTest` + baseline YAML green on current HEAD; CI wired
3. `NoNewStudentActorStateTest` + baseline YAML green on current HEAD; CI wired
4. Both tests fail when tested against a synthetic PR that violates the rule (validation of the gate itself)
5. Sprint 1 branch opened on 2026-04-27 with first commit: interface contracts for LearningSession aggregate
6. EPIC-PRR-A task file + EPIC-PRR-C task file cross-refs updated with user-locked decisions ✅ (2026-04-20)
7. Full `Cena.Actors.sln` builds cleanly with new arch tests
8. Kill switch documented — if Sprint 1 fails the LOC rule itself, pause, do not stub

### Task status

- **Substeps 1, 6 landed 2026-04-20** (ADR Schedule Lock + epic cross-refs).
- **Substeps 2-4 next** — arch tests (delegated to focused coder subagent 2026-04-20).
- **Substep 5 awaits 2026-04-27** — Sprint 1 kickoff per locked schedule.

## Reporting

complete via: node .agentdb/kimi-queue.js complete <id> --worker human-architect --result "<branch>"

---

## Non-negotiable references
- 500-LOC rule

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
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (id: prr-002)
