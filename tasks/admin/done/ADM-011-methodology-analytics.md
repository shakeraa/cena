# ADM-011: Methodology & Stagnation Analytics

**Priority:** P2 — pedagogy insights
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena uses 8+ teaching methodologies (worked examples, scaffolded hints, Socratic dialogue, etc.) selected by the MCM (Methodology-Concept Mapping) graph. The admin dashboard surfaces which methodologies work best, where students stagnate, and which concepts resist all approaches.

Maps to: `McmGraphActor`, `StagnationDetectorActor`, methodology switching logic.

## Subtasks

### ADM-011.1: Methodology Effectiveness Dashboard

**Files to create:**

- `src/admin/full-version/src/pages/apps/pedagogy/methodology.vue`
- `src/admin/full-version/src/views/apps/pedagogy/` — pedagogy components

**Charts:**

- [ ] Methodology effectiveness comparison — grouped bar: avg time-to-mastery by methodology per error type
- [ ] Methodology switch triggers — stacked bar: stagnation vs student-requested vs MCM recommendation
- [ ] Stagnation events per week — trend line chart
- [ ] Escalation rate — concepts where all methodologies exhausted

### ADM-011.2: Stagnation Monitor

**Files to create:**

- `src/admin/full-version/src/views/apps/pedagogy/StagnationMonitor.vue`

**Acceptance:**

- [ ] Table of currently stagnating students: student, concept cluster, composite score, attempts, days stuck
- [ ] Concepts flagged "mentor-resistant" (all methodologies tried, no progress)
- [ ] Click student to navigate to student detail focus tab

### ADM-011.3: MCM Graph Viewer

**Files to create:**

- `src/admin/full-version/src/pages/apps/pedagogy/mcm-graph.vue`

**Acceptance:**

- [ ] Visual representation of (ErrorType, ConceptCategory) → Methodology mappings
- [ ] Confidence scores displayed on edges
- [ ] Read-only for moderators, editable for admins
- [ ] Edit confidence scores — changes sent to `McmGraphActor`
- [ ] Show data-driven confidence updates from real student interactions

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/pedagogy/methodology-effectiveness` | Effectiveness metrics by methodology |
| GET | `/api/admin/pedagogy/stagnation-trend` | Stagnation events over time |
| GET | `/api/admin/pedagogy/switch-triggers` | Methodology switch breakdown |
| GET | `/api/admin/pedagogy/mentor-resistant` | Concepts that resist all approaches |
| GET | `/api/admin/pedagogy/mcm-graph` | Full MCM graph data |
| PUT | `/api/admin/pedagogy/mcm-graph/edge` | Update MCM edge confidence |

## Test

- [ ] Charts render with real methodology data
- [ ] Stagnation monitor shows currently stuck students
- [ ] MCM graph edits persist and update actor state
- [ ] Moderator sees read-only MCM graph (CASL enforced)
