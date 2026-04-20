# EPIC-PRR-C: Parent Aggregate + age-band consent + IDOR enforcement

**Priority**: P0
**Effort**: XL (epic-level: 3-8 weeks aggregate across 10 sub-tasks)
**Lens consensus**: persona-enterprise, persona-ethics, persona-finops, persona-privacy, persona-redteam, persona-sre
**Source docs**: AXIS_4_Parent_Engagement_Cena_Research.md:L109, AXIS_4_Parent_Engagement_Cena_Research.md:L157, AXIS_4_Parent_Engagement_Cena_Research.md:L64, AXIS_4_Parent_Engagement_Cena_Research.md:L69, AXIS_4_Parent_Engagement_Cena_Research.md:L86, AXIS_4_Parent_Engagement_Cena_Research.md:L~, axis9_data_privacy_trust_mechanics.md:L~
**Assignee hint**: human-architect (epic-level planning) + named subagents per sub-task
**Tags**: source=pre-release-review-2026-04-20, type=epic, epic=epic-prr-c
**Status**: Not Started
**Source**: Synthesized from the 10-persona pre-release review 2026-04-20; bundles the absorbed tasks listed below because they share a single architectural substrate and must ship in lock-step.

---

## Epic goal
Build the missing Parent aggregate: parent auth role, parent→child claims binding with IDOR enforcement helper, age-band split (<13 / 13-15 / 16-17) that gates what the parent can see and what the student can withdraw, outbound SMS/WhatsApp sanitizer with quiet hours and opt-out, and the admin UIs + DSAR exports for parental consent. All AXIS-4 parent-engagement work is blocked on this substrate.

## Architectural substrate
ADR for parent auth role + Parent aggregate + ConsentAggregate events. This epic is the single reason AXIS-4 cannot ship today. The absorbed sub-tasks cover auth, binding, consent purposes, outbound nudge governance, admin management UIs, parent-facing privacy policy, and per-student consent audit exports — one coherent trust-and-messaging contract with parents.

## Absorbed tasks (10)
| ID | Title | Priority | Role in epic |
|---|---|---|---|
| prr-009 | Parent→child claims binding + IDOR enforcement helper | P0 | feature |
| prr-014 | ADR: Parent auth role + age-band + multi-institute visibility | P1 | foundation |
| prr-018 | Outbound SMS sanitizer + rate-limit + quiet-hours policy (parent nudges) | P1 | doc |
| prr-051 | Parent digest: opt-in purposes + unsubscribe-all link | P1 | feature |
| prr-052 | Parent-dashboard student-visible consent at 13+/16+ | P1 | feature |
| prr-096 | Admin UI: parental-consent management | P2 | feature |
| prr-106 | Accommodation audit exports for parents (on request) | P2 | test |
| prr-108 | WhatsApp Twilio adapter opt-out enforcement | P2 | feature |
| prr-123 | Privacy policy: parent + student dual-version | P2 | doc |
| prr-130 | Admin: consent audit export per student | P2 | test |

The absorbed task files remain in place as the executable unit-of-work; this epic file provides the coordination frame, dependency order, and DoD for the whole bundle.

## Suggested execution order
1. prr-014 — ADR: Parent auth role + age-band + multi-institute visibility
2. prr-009 — Parent→child claims binding + IDOR enforcement helper
3. prr-018 — Outbound SMS sanitizer + rate-limit + quiet-hours (messaging policy)
4. prr-108 — WhatsApp Twilio adapter opt-out enforcement
5. prr-052, prr-051 — Student-visible consent at 13+/16+; parent digest opt-in purposes + unsubscribe-all
6. prr-096, prr-130 — Admin UI for parental-consent management + per-student consent audit export
7. prr-106 — Accommodation audit exports for parents on request
8. prr-123 — Parent + student dual-version privacy policy

## Epic-level DoD (in addition to per-task DoDs)
- Single ADR covers all absorbed scope (or the ADR pack is internally consistent).
- Integration test demonstrating all sub-task contributions work together in one end-to-end scenario.
- SYNTHESIS.md epic section reflects completion.
- No absorbed task is marked done until all peers in the epic are merged.

## Epic triage decisions 2026-04-20 (user)

**Adopted**: scope + general execution order.

**Tightenings**:

1. **Explicit external blockers (ADD to Blocks section)**:
   - **prr-011** (httpOnly session cookie + BFF) — parent session cookie reuses this pattern; cannot ship until prr-011 lands.
   - **prr-007** (theta isolation seam) — parent-visible surfaces consume readiness buckets via ThetaMasteryMapper; need the seam first.
   - **prr-003a** (erasure ADR) — parental-erasure flow depends on chosen erasure model.
2. **C-04 conflict is explicit prerequisite** — parent visibility at 13-16 is a decision gate; epic cannot start until C-04 resolves.
3. **prr-018 role reclassified**: `doc` → `feature+infra`. Outbound SMS sanitizer is code, not documentation.
4. **Coordinate ConsentAggregate with EPIC-PRR-A prr-155** — same aggregate-design substrate; avoid double-design.

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
- Absorbed sub-tasks: prr-009, prr-014, prr-018, prr-051, prr-052, prr-096, prr-106, prr-108, prr-123, prr-130
- [Full synthesis](../../pre-release-review/reviews/SYNTHESIS.md)
- [Retired proposals](../../pre-release-review/reviews/retired.md)
- [Conflicts](../../pre-release-review/reviews/conflicts.md)
- [Canonical task JSON](../../pre-release-review/reviews/tasks.jsonl) (epic id: epic-prr-c)
