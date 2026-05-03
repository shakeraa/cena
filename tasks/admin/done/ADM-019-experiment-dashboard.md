# ADM-019: A/B Experiment Dashboard

**Priority:** P1 — visibility for SAI-006 experiment framework
**Blocked by:** None (SAI-006 ExperimentService is complete)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Admin API (.NET 9)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

SAI-006 delivered an A/B experiment framework (ExperimentService) that assigns students to treatment/control cohorts and tracks explanation impact on mastery outcomes. The focus dashboard has a basic experiments widget, but there is no dedicated experiment management page showing cohort breakdowns, effect sizes, or statistical significance. This task adds a full experiment dashboard.

## Backend: New Admin API Endpoints

### ADM-019.1: ExperimentAdminService + Endpoints

**Files to create:**

- `src/api/Cena.Admin.Api/ExperimentAdminDtos.cs`
- `src/api/Cena.Admin.Api/ExperimentAdminService.cs`

**Files to modify:**

- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — add `MapExperimentAdminEndpoints`

**Endpoints:**

```
GET  /api/admin/experiments
     Returns: List of all experiments with status, cohort counts, date range

GET  /api/admin/experiments/{experimentId}
     Returns: Full experiment detail with cohort breakdown

GET  /api/admin/experiments/{experimentId}/cohorts
     Returns: Treatment vs control metrics — mastery delta, confusion resolution rate, avg turns

GET  /api/admin/experiments/{experimentId}/funnel
     Returns: Conversion funnel stages — assigned → engaged → confused → resolved → mastered
```

**Data source:** Query `ExperimentService` state + Marten event projections for cohort outcomes. Join with mastery events to calculate effect sizes.

**Acceptance:**

- [ ] Experiments list shows all registered experiments from ExperimentService
- [ ] Cohort endpoint calculates real treatment vs control deltas from mastery events
- [ ] Funnel counts real students at each stage (not percentages alone — raw counts + rates)
- [ ] Effect size calculation: Cohen's d for continuous metrics, risk ratio for binary outcomes
- [ ] All endpoints require `ModeratorOrAbove` auth policy

### ADM-019.2: Experiment List Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/experiments/index.vue`

**Acceptance:**

- [ ] Data table: Experiment Name, Status (Running/Completed/Paused), Treatment Count, Control Count, Start Date
- [ ] Status chips with color coding
- [ ] Click row navigates to experiment detail
- [ ] No create/edit — experiments are configured in code (ExperimentService), admin is read-only

### ADM-019.3: Experiment Detail Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/experiments/[id].vue`
- `src/admin/full-version/src/views/apps/experiments/CohortComparisonChart.vue`
- `src/admin/full-version/src/views/apps/experiments/ConversionFunnel.vue`

**Acceptance:**

- [ ] Header: experiment name, status, date range, total students
- [ ] Cohort comparison: side-by-side bar chart — treatment vs control on key metrics
- [ ] Key metrics: mastery improvement rate, confusion resolution rate, avg tutoring turns, time to mastery
- [ ] Conversion funnel: horizontal funnel visualization showing drop-off at each stage
- [ ] Statistical significance indicator: green check if p < 0.05, yellow warning if p < 0.10, gray if insufficient data
- [ ] Effect size badge: small/medium/large based on Cohen's d thresholds

### ADM-019.4: Navigation

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts`

**Acceptance:**

- [ ] Add "Experiments" under "AI Tutoring" heading
- [ ] Icon: `tabler-flask`
- [ ] CASL subject: `Pedagogy`, action: `read`
