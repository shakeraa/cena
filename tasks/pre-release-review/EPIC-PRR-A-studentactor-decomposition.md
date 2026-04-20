# EPIC-PRR-A: ADR-0012 StudentActor decomposition

**Priority**: P0
**Effort**: XL (epic-level: 3-8 weeks aggregate across 13 sub-tasks)
**Lens consensus**: persona-cogsci, persona-educator, persona-enterprise, persona-ethics, persona-finops, persona-ministry, persona-privacy, persona-redteam
**Source docs**: AXIS_10_Operational_Integration_Features.md:L111, axis1_pedagogy_mechanics_cena.md:L40, axis1_pedagogy_mechanics_cena.md:L44, axis1_pedagogy_mechanics_cena.md:L441, axis1_pedagogy_mechanics_cena.md:L46, axis2_motivation_self_regulation_findings.md:L43, axis2_motivation_self_regulation_findings.md:L~, axis3_accessibility_accommodations_findings.md:L~, axis9_data_privacy_trust_mechanics.md:L111, axis9_data_privacy_trust_mechanics.md:L70 ...
**Assignee hint**: human-architect (epic-level planning) + named subagents per sub-task
**Tags**: source=pre-release-review-2026-04-20, type=epic, epic=epic-prr-a
**Status**: Not Started
**Source**: Synthesized from the 10-persona pre-release review 2026-04-20; bundles the absorbed tasks listed below because they share a single architectural substrate and must ship in lock-step.

---

## Epic goal
Break the StudentActor god-aggregate (>2,969 LOC across partial classes) into LearningSession, SelfRegulation, Accommodations, and StudentPlanConfig aggregates so that pedagogy, SRL, accommodations, and scheduler features can ship without compounding the architectural debt. Every absorbed sub-task either depends on the split or moves state onto one of the successor aggregates.

## Architectural substrate
This epic establishes ADR-0012 + the bounded contexts that replace StudentActor. Without it, every subsequent pedagogy/SRL/accommodations feature that adds state violates the 500-LOC rule and makes the later decomposition strictly harder. Sub-tasks that (a) author the ADR, (b) extract the first aggregate, (c) wire live callers into the new seams, and (d) attach new projections to the erasure cascade must ship as one coordinated bundle.

## Absorbed tasks (13)
| ID | Title | Priority | Role in epic |
|---|---|---|---|
| prr-002 | ADR-0012 StudentActor split — gate pedagogy + SRL features | P0 | foundation |
| prr-013 | Redesign 'At-Risk Student Alert' — honest + supportive + legal | P0 | feature |
| prr-038 | ADR: Right-to-be-forgotten in event-sourced Cena | P1 | foundation |
| prr-041 | ADR: BKT fixed-parameter policy + worked-example fading hysteresis | P1 | foundation |
| prr-044 | ADR: Accommodation profile scope (student vs enrollment) | P1 | foundation |
| prr-065 | Strategy-discrimination scores in AdaptiveScheduler (session-scoped) | P2 | feature |
| prr-102 | Retire 'emotional state' profile fields (session-only) | P2 | feature |
| prr-148 | Student-input UI for AdaptiveScheduler (deadline + weekly time budget) | P1 | feature |
| prr-149 | Live caller for AdaptiveScheduler at session start | P1 | feature |
| prr-150 | Mentor/tutor override aggregate for schedule | P2 | feature |
| prr-151 | Live-caller audit across Group A substrates (R-03/R-08/R-09/R-13/R-15/R-22) | P1 | test |
| prr-155 | Design ConsentAggregate + events | P1 | feature |
| prr-157 | Fix TZ infra before calendar features | P1 | feature |

The absorbed task files remain in place as the executable unit-of-work; this epic file provides the coordination frame, dependency order, and DoD for the whole bundle.

## Suggested execution order
1. prr-002 — Author ADR-0012, lock split schedule, add 500-LOC architecture test
2. prr-044, prr-041, prr-038 — Supporting ADRs (accommodation scope, BKT fixed params, event-sourced RTBF) that constrain the split
3. prr-155 — Design ConsentAggregate + events (shares aggregate-design substrate)
4. prr-148, prr-149 — Wire SchedulerInputs/StudentPlanConfig into new aggregate + live caller
5. prr-150, prr-151 — Mentor override aggregate and live-caller audit across peer substrates
6. prr-013, prr-102, prr-065 — Retire/redesign at-risk + emotional-state + strategy-discrimination under ADR-0003 session-only scope
7. prr-157 — Fix TZ infra before calendar features (pre-req for scheduler aggregate)

## Epic-level DoD (in addition to per-task DoDs)
- Single ADR covers all absorbed scope (or the ADR pack is internally consistent).
- Integration test demonstrating all sub-task contributions work together in one end-to-end scenario.
- SYNTHESIS.md epic section reflects completion.
- No absorbed task is marked done until all peers in the epic are merged.

## Epic triage decisions 2026-04-20 (user)

**Adopted**: scope + general execution order.

**Tightenings**:

1. **prr-155 ConsentAggregate is cross-epic with EPIC-PRR-C** — same DDD-aggregate design substrate. Coordinate ConsentAggregate design with EPIC-PRR-C before either commits to events. Owner: this epic; reviewer: EPIC-PRR-C.
2. **Move prr-157 TZ fix to step 2** alongside ADRs — `FindSystemTimeZoneById("Israel")` is a latent prod-crash on Linux (actor-system-review L1), not a late-stage polish.
3. **Epic kill switch**: if Sprint 1 of ADR-0012 reveals the split fights the 500-LOC rule, pause new absorbed-feature work until re-plan. Do not keep adding features to a god-aggregate-in-refactor.
4. **Aggregate-citation gate**: every absorbed P1/P2 PR must name the successor aggregate it writes state to, referenced against the accepted ADR. Hand-wave "StudentActor or its successor" insufficient post-split.

**Tags**: user-decision=2026-04-20-epic-triaged

---

## Implementation Protocol — Senior Architect

Implementation of this epic must be driven by a senior-architect mindset, not a checklist. Before writing any code, the implementer (human or agent) must answer both sets of questions in writing — either in a task-comment, the PR description, or a `docs/decisions/` note:

### Ask why
- **Why does this epic exist?** Read the source-doc lines cited in the absorbed sub-tasks and the persona reviews in `/pre-release-review/reviews/persona-*/` that raised them. If you cannot restate the motivation in one sentence, do not start coding.
- **Why this priority?** Read the lens-consensus list. Understand which persona lens raised it and what evidence they cited.
- **Why these files?** Trace the data flow end-to-end. Verify the files listed are the right seams. A bad seam invalidates the whole epic.
- **Why are the non-negotiables above relevant?** Show understanding of how each constrains the solution, not just that they exist.

### Ask how
- **How does this interact with existing aggregates and bounded contexts?** Name them.
- **How does it respect tenant isolation (ADR-0001), event sourcing, the CAS oracle (ADR-0002), and session-scoped misconception data (ADR-0003)?**
- **How will it fail?** What is the runbook at 03:00 on a Bagrut exam morning? If you cannot describe the failure mode, the design is incomplete.
- **How will it be verified end-to-end, with real data?** Not mocks. Query the DB, hit the APIs, compare field names and tenant scoping.
- **How does it honor the <500 LOC per file rule, the no-stubs-in-prod rule, and the full `Cena.Actors.sln` build gate?**

### Before committing
- Full `Cena.Actors.sln` must build cleanly (branch-only builds miss cross-project errors — learned 2026-04-13).
- Tests cover golden path **and** edge cases surfaced in the persona reviews.
- No cosmetic patches over root causes. No "Phase 1 stub → Phase 1b real" pattern (banned 2026-04-11).
- No dark-pattern copy (ship-gate scanner must pass).
- If the epic as-scoped is wrong in light of what you find, **push back** and propose the correction — do not silently expand scope, shrink scope, or ship a stub.

### If blocked
- Fail loudly. Do not silently reduce scope. Do not skip a non-negotiable.

### Definition of done is higher than the checklist above
- Labels match data (UI label = API key = DB column intent).
- Root cause fixed, not masked.
- Observability added (metrics, structured logs with tenant/session IDs, runbook entry).
- Related personas cross-lens handoffs addressed or explicitly deferred with a new task ID.


---

## Related
- Absorbed sub-tasks: prr-002, prr-013, prr-038, prr-041, prr-044, prr-065, prr-102, prr-148, prr-149, prr-150, prr-151, prr-155, prr-157
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (epic id: epic-prr-a)
