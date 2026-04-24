# ADM-006: Focus & Attention Analytics

**Priority:** P1 — key Cena differentiator
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 4 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena's focus engine (FOC tasks) tracks student attention, mind wandering, microbreak effectiveness, and focus degradation patterns. The admin dashboard surfaces these analytics for teachers and admins to monitor student wellbeing and learning effectiveness.

Maps to backend services: `FocusDegradationService`, `MindWanderingDetector`, `MicrobreakScheduler`, `FocusExperimentCollector`.

## Subtasks

### ADM-006.1: Focus Overview Dashboard

**Files to create:**
- `src/admin/full-version/src/pages/apps/focus/dashboard.vue`
- `src/admin/full-version/src/views/apps/focus/` — focus components

**Widgets:**
- [ ] **Avg Focus Score** — gauge chart, current platform/class average
- [ ] **Mind Wandering Rate** — percentage of sessions with detected mind wandering
- [ ] **Microbreak Compliance** — % of suggested breaks taken
- [ ] **Focus Degradation Curve** — line chart showing avg focus over session duration

### ADM-006.2: Student Focus Detail

**Files to create:**
- `src/admin/full-version/src/pages/apps/focus/student/[id].vue`

**Acceptance:**
- [ ] Individual student focus timeline (last 7/30 days)
- [ ] Session-by-session focus scores
- [ ] Mind wandering events with timestamps and context
- [ ] Microbreak history: when suggested, when taken, duration
- [ ] Optimal study time recommendation based on chronotype data
- [ ] Comparison to class/grade average (anonymized)

### ADM-006.3: Class-Level Focus View (Teacher)

**Files to create:**
- `src/admin/full-version/src/pages/apps/focus/class/[id].vue`

**Acceptance:**
- [ ] Class heatmap: students (rows) × time slots (columns), colored by focus score
- [ ] Aggregated focus trends for the class
- [ ] Students needing attention: consistently low focus, declining trend
- [ ] Subject correlation: which subjects have higher/lower focus
- [ ] Privacy: teachers see aggregated data, not raw sensor signals

### ADM-006.4: Focus Experiment Results

**Files to create:**
- `src/admin/full-version/src/pages/apps/focus/experiments.vue`

**Acceptance:**
- [ ] A/B experiment results from `FocusExperimentCollector`
- [ ] Metrics: focus score delta, completion rate delta, time-on-task delta
- [ ] Statistical significance indicators
- [ ] Experiment configuration viewer

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/focus/overview` | Platform/class focus summary |
| GET | `/api/admin/focus/students/{id}` | Individual student focus data |
| GET | `/api/admin/focus/classes/{id}` | Class-level aggregated focus |
| GET | `/api/admin/focus/degradation-curve` | Avg focus over session duration |
| GET | `/api/admin/focus/experiments` | Experiment results |
| GET | `/api/admin/focus/alerts` | Students needing attention |

## Test

- [ ] Focus dashboard loads with real data
- [ ] Charts handle missing data (new student, no sessions yet)
- [ ] Teacher can only see own class data (CASL enforced)
- [ ] Admin sees platform-wide aggregates
- [ ] Focus degradation curve matches FocusDegradationService output
- [ ] Arabic RTL: all charts, labels, numbers render correctly
- [ ] Privacy: no raw sensor data exposed in teacher view
