# ADM-007: Mastery & Learning Progress

**Priority:** P1 — core learning analytics
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena's mastery engine (MST tasks) tracks student knowledge state across math/physics concepts using BKT and learning frontiers. The admin dashboard surfaces mastery progress for teachers and admins.

## Subtasks

### ADM-007.1: Mastery Overview Dashboard

**Files to create:**
- `src/admin/full-version/src/pages/apps/mastery/dashboard.vue`
- `src/admin/full-version/src/views/apps/mastery/` — mastery components

**Widgets:**
- [ ] **Mastery Distribution** — histogram of students by mastery level
- [ ] **Subject Breakdown** — radar chart of avg mastery per subject
- [ ] **Learning Velocity** — avg concepts mastered per week, trend
- [ ] **At-Risk Students** — students below mastery threshold, declining trend

### ADM-007.2: Student Mastery Detail

**Files to create:**
- `src/admin/full-version/src/pages/apps/mastery/student/[id].vue`

**Acceptance:**
- [ ] Knowledge map: concept graph with mastery levels color-coded
- [ ] Learning frontier: which concepts the student is ready for next
- [ ] Mastery history: concept-by-concept progress over time
- [ ] Scaffolding recommendations from mastery engine
- [ ] Review priority queue: concepts most at risk of decay

### ADM-007.3: Class Progress View (Teacher)

**Files to create:**
- `src/admin/full-version/src/pages/apps/mastery/class/[id].vue`

**Acceptance:**
- [ ] Class mastery grid: students × concepts, colored by mastery level
- [ ] Concept difficulty analysis: which concepts students struggle with most
- [ ] Pacing recommendations: is the class ready to advance?
- [ ] Comparison to curriculum targets

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/mastery/overview` | Platform/class mastery summary |
| GET | `/api/admin/mastery/students/{id}` | Student mastery detail |
| GET | `/api/admin/mastery/students/{id}/knowledge-map` | Concept graph with levels |
| GET | `/api/admin/mastery/classes/{id}` | Class mastery grid |
| GET | `/api/admin/mastery/at-risk` | Students below threshold |

## Test

- [ ] Mastery dashboard renders with real BKT data
- [ ] Knowledge map visualizes concept graph correctly
- [ ] Teacher sees only own class data
- [ ] At-risk students list matches mastery engine thresholds
- [ ] Arabic RTL: concept names, labels render correctly
