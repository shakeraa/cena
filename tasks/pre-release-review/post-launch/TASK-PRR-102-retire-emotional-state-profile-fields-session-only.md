# TASK-PRR-102: Retire 'emotional state' profile fields (session-only)

**Priority**: P2 — post-launch improvement (lens consensus: 2)
**Effort**: S — 1-2 days
**Lens consensus**: persona-ethics, persona-privacy
**Source docs**: `axis2_motivation_self_regulation_findings.md:L~`
**Assignee hint**: claude-subagent-adr-authoring
**Tags**: source=pre-release-review-2026-04-20, lens=ethics
**Status**: Not Started
**Source**: Synthesized from 10-persona pre-release review (2026-04-20) — see `/pre-release-review/reviews/SYNTHESIS.md`
**Tier**: post-launch
**Epic**: EPIC-PRR-A — ADR-0012 StudentActor decomposition

---

## Goal

R-37 retirement follow-through: no persistent `emotional_state` / `mood` / `affect` field on any student-profile persistence surface. Affective signals are ADR-0037 session-scoped. Scanner + arch test prevent regression; no new feature work.

### User decision 2026-04-20 — adopt as R-37 follow-through, no new design

Minimal delta — the retirement was already decided. This task is the enforcement:

1. **Arch test** (post Sprint 2 StudentProfile extraction): no `emotional_state*`, `mood*`, `affect*`, `feelings*`, `emotional*` field on any type in the StudentProfile aggregate tree or any DTO reachable from it. Session-scoped affective signals remain OK inside `LearningSession` aggregate scope only.
2. **Scanner rule** (EPIC-PRR-D cluster D3): reject any locale/doc/code comment proposing "track student emotion" / "emotional profile" / "mood persistence" / "emotional state over time" framing. Adds to the banned-copy rule pack.
3. **Sweep existing code**: grep `StudentState.cs`, `StudentProfileSnapshot.cs`, `MeStore.ts` for any current affective-state fields; retire each with a migration note.

## Files

- `tests/architecture/NoEmotionalStateOnProfileTest.cs` (new, Sprint 2 timing)
- `scripts/shipgate/banned-copy.yml` (EPIC-PRR-D cluster D3 — add emotional-profile terms)
- Any currently-shipped affective-state fields on profile — to be swept and retired

## Definition of Done

1. Arch test green (no emotional-state fields on profile)
2. Scanner rule active; fixture covers the banned terms
3. Repo sweep report: zero remaining fields, or fields retired with migration notes
4. Full `Cena.Actors.sln` builds cleanly

## Reporting
complete via: node .agentdb/kimi-queue.js complete <id> --worker claude-subagent-adr-authoring --result "<branch>"

---

## Non-negotiable references
- #3: No dark-pattern engagement (streaks, loss-aversion, variable-ratio banned)
- ADR-0037

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
- [Canonical task JSON](../../../pre-release-review/reviews/tasks.jsonl) (id: prr-102)
