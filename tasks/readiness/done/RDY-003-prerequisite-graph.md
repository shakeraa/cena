# RDY-003: Populate Prerequisite Graph

- **Priority**: SHIP-BLOCKER — adaptive learning is hollow without prerequisites
- **Complexity**: Senior engineer + curriculum expert — data + validation
- **Source**: Expert panel audit — Amjad (Curriculum), Nadia (Pedagogy), Yael (IRT)
- **Tier**: 0 (blocks adaptive learning claim)
- **Effort**: 2-3 weeks (revised from 1-2 weeks per Rami's review)

> **Rami's challenge**: This task is BLOCKED on Amjad's availability. The prerequisite graph requires curriculum expertise — an engineer can't author it alone. Clarify: is this 1 week (Amjad specs graph, engineer populates) or 2-3 weeks (collaborative)? Also missing: physics prerequisites, multi-path prerequisites (co-requisites), and how many concepts need edges (task doesn't count them).
>
> **Immediate action**: Confirm Amjad's calendar — can he start week 1?

## Problem

All `Prerequisites[]` arrays on 1,000 seed questions are empty. The schema exists (`KnowledgeDtos.cs`), the BKT+ prerequisite gate exists (0.60 threshold), and the graph connectivity validator exists — but no prerequisite data has been populated.

**Downstream impact** (per panel):
- **Nadia**: Scaffolding levels depend on prerequisite mastery. Without prerequisites, all students start at the same level regardless of prior knowledge.
- **Yael**: CAT content balancing assumes topological ordering. Without prerequisites, it's random selection with a difficulty filter.
- **Lior**: ConceptTile UI shows a blank dependency map — implying no dependencies exist.

## Scope

### 1. Define foundational concepts per subject

For each subject (Math 5-unit, Math 4-unit, Physics), identify entry-point concepts that have no prerequisites. These are the roots of the dependency graph.

### 2. Build prerequisite edges for Math 5-unit (priority)

Using the official Bagrut 5-unit syllabus and the existing ConceptId namespace (`math_5u_*`), define prerequisite edges:
- Algebra fundamentals → Equations → Inequalities
- Functions → Derivatives → Integrals
- Geometry → Trigonometry → Analytic Geometry
- Probability → Statistics

Target: Every concept has at least one path from a foundational node. No circular dependencies.

### 3. Populate via seed data or migration

Either update `QuestionBankSeedData.cs` or create a migration script that:
- Sets `Prerequisites` and `Dependencies` arrays on each `QuestionDocument`
- Validates graph connectivity (every concept reachable from a root)
- Validates no cycles

### 4. Enable prerequisite gating in quality gate

Add a validation rule: if a question is tagged Bloom Level 3+ (Apply and above), it MUST have at least one prerequisite. Questions with `Prerequisites: []` and `BloomsLevel >= 3` should be flagged.

## Files to Modify

- `src/api/Cena.Admin.Api/QuestionBankSeedData.cs` — populate prerequisite arrays
- New: `scripts/prerequisite-graph.json` — canonical prerequisite edges (version-controlled)
- `src/shared/Cena.Infrastructure/Content/QualityGateService.cs` — add prerequisite validation rule
- `src/student/full-version/src/components/session/ConceptTile.vue` — verify rendering with real data

## Acceptance Criteria

- [ ] Every Math 5-unit concept has `Prerequisites` populated (no empty arrays)
- [ ] Graph is acyclic (validation script passes)
- [ ] Every concept is reachable from at least one foundational node
- [ ] BKT+ prerequisite gate fires for students attempting concepts without prerequisites met
- [ ] Quality gate flags Bloom 3+ questions with empty prerequisites
- [ ] ConceptTile renders dependency edges in UI
- [ ] `scripts/prerequisite-graph.json` is version-controlled and human-reviewable
