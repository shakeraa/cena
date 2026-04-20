# EPIC-PRR-B: ADR-026 3-tier LLM routing governance

**Priority**: P0
**Effort**: XL (epic-level: 3-8 weeks aggregate across 11 sub-tasks)
**Lens consensus**: persona-cogsci, persona-enterprise, persona-ethics, persona-finops, persona-redteam, persona-sre
**Source docs**: AXIS_4_Parent_Engagement_Cena_Research.md:L~, axis1_pedagogy_mechanics_cena.md:L~, axis2_motivation_self_regulation_findings.md:L81, feature-discovery-2026-04-20.md:L25, feature-discovery-2026-04-20.md:L~
**Assignee hint**: human-architect (epic-level planning) + named subagents per sub-task
**Tags**: source=pre-release-review-2026-04-20, type=epic, epic=epic-prr-b
**Status**: Not Started
**Source**: Synthesized from the 10-persona pre-release review 2026-04-20; bundles the absorbed tasks listed below because they share a single architectural substrate and must ship in lock-step.

---

## Epic goal
Promote the current `contracts/llm/routing-config.yaml` convention to a real ADR, and wire a CI scanner that breaks the build on silent-default-to-Sonnet, missing tier assignments, or missing cost/cache annotations. Roll in all cost-governance, caching, per-feature/per-institute cost tagging, turn-budget enforcement, and vendor-outage runbook sub-tasks so that a single routing/governance decision applies to every LLM-consuming feature at 10k-student scale.

## Architectural substrate
ADR-026 + the scanner + cost-telemetry substrate. Without the ADR there is no enforcement surface; without the scanner the ADR is an unenforced norm. The sub-tasks that build dashboards, enforce caps, and define runbooks all depend on the routing-config schema being a first-class architectural primitive, not a stray YAML.

## Absorbed tasks (11)
| ID | Title | Priority | Role in epic |
|---|---|---|---|
| prr-004 | Promote contracts/llm/routing-config.yaml governance to ADR-026 + CI scanner | P0 | foundation |
| prr-012 | Cap Socratic self-explanation to 3 LLM calls/session + reuse SAI-003 cache | P0 | feature |
| prr-046 | Cost-projection dashboard + per-feature cost tag | P1 | feature |
| prr-047 | LLM prompt cache enforcement + hit-rate SLO | P1 | feature |
| prr-048 | Daily-minute caps per student + soft-limit nudge | P1 | feature |
| prr-084 | Cost alert: LLM spend per institute breach | P2 | feature |
| prr-095 | Runbook: LLM vendor outage failover | P2 | doc |
| prr-105 | Tutor turn-budget enforcement from ADR-0002 | P2 | foundation |
| prr-112 | Admin UI: cost per feature per cohort | P2 | feature |
| prr-143 | Observability: trace id on every LLM call | P2 | feature |
| prr-145 | ADR: Hint generation model-tier selection | P2 | foundation |

The absorbed task files remain in place as the executable unit-of-work; this epic file provides the coordination frame, dependency order, and DoD for the whole bundle.

## Suggested execution order
1. prr-004 — Author ADR-026, wire routing-config CI scanner, break on silent-default-to-Sonnet
2. prr-145 — ADR: Hint generation model-tier selection (clarifies L1/L2/L3 tier choice)
3. prr-012, prr-105 — Cap Socratic self-explanation + enforce tutor turn-budget from ADR-0002
4. prr-047 — LLM prompt cache enforcement + hit-rate SLO (the primary cost lever)
5. prr-046, prr-084, prr-112 — Cost projection dashboard + per-institute alerts + per-feature/per-cohort dashboard
6. prr-048 — Daily-minute caps per student + soft-limit nudge
7. prr-143 — Trace-id on every LLM call (observability backbone)
8. prr-095 — Runbook: LLM vendor outage failover

## Epic-level DoD (in addition to per-task DoDs)
- Single ADR covers all absorbed scope (or the ADR pack is internally consistent).
- Integration test demonstrating all sub-task contributions work together in one end-to-end scenario.
- SYNTHESIS.md epic section reflects completion.
- No absorbed task is marked done until all peers in the epic are merged.

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
- Absorbed sub-tasks: prr-004, prr-012, prr-046, prr-047, prr-048, prr-084, prr-095, prr-105, prr-112, prr-143, prr-145
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (epic id: epic-prr-b)
