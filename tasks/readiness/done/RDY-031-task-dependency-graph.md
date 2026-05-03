# RDY-031: Readiness Task Dependency Graph

- **Priority**: Medium — coordination risk without it
- **Complexity**: Low — documentation + validation script
- **Source**: Cross-review — Nadia (Pedagogy)
- **Tier**: 3
- **Effort**: 1 day

## Problem

Cross-review identified 7+ undocumented dependencies between readiness tasks. Without a dependency graph, parallel execution risks wasted work (building on incomplete prerequisites) or missed integration points.

Known dependencies:
1. RDY-027 (Glossary) → RDY-004 (Arabic Translations) — translate from canonical Hebrew
2. RDY-003 (Prerequisites) → RDY-007 (DIF) — DIF needs prerequisite structure for content balancing
3. RDY-003 (Prerequisites) → RDY-018 (Sympson-Hetter) — exposure control needs topological ordering
4. RDY-002 (RTL) → RDY-015 (A11y Sweep) — a11y testing requires RTL enabled
5. RDY-015 (A11y Sweep) → RDY-030 (A11y Automation) — automation locks in sweep fixes
6. RDY-013 (Worked Examples) → RDY-014 (Misconception Detection) — misconception remediation may show worked examples
7. RDY-023 (Diagnostic) → RDY-024 (BKT Calibration) — diagnostic provides initial data for calibration

## Scope

### 1. Dependency map

- Create a machine-readable dependency graph: `config/readiness-dependencies.json`
- Format: `{ task_id, depends_on: [task_ids], blocks: [task_ids] }`
- Include all 31 tasks

### 2. Validation script

- Script that reads dependency graph and current task statuses
- Warns if a task is "in progress" but its dependencies are not "completed"
- Generates a topological sort for optimal execution order
- Outputs Mermaid diagram for visual review

### 3. Critical path analysis

- Identify the longest dependency chain (critical path to production)
- Document which tasks are parallelizable vs. sequential
- Update READINESS-TASK-INDEX.md with dependency column

## Files to Create

- New: `config/readiness-dependencies.json`
- New: `scripts/readiness-dependency-check.ts`
- New: `docs/tasks/readiness/READINESS-DEPENDENCY-GRAPH.md` — Mermaid diagram

## Acceptance Criteria

- [ ] All 31 task dependencies documented in JSON
- [ ] Validation script warns on dependency violations
- [ ] Critical path identified and documented
- [ ] Mermaid diagram generated and included in docs
- [ ] Task index updated with dependency column
