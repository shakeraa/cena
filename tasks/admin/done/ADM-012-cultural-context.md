# ADM-012: Cultural Context Insights

**Priority:** P2 — equity and inclusion monitoring
**Blocked by:** ADM-001 (auth), ADM-003 (permissions)
**Estimated effort:** 2 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena serves Israeli students in Hebrew and Arabic. The `CulturalContextService` detects linguistic context (HebrewDominant, ArabicDominant, Bilingual, Unknown) and the `SocialResilienceSignal` tracks cultural resilience. This dashboard monitors equity across cultural groups.

## Subtasks

### ADM-012.1: Cultural Distribution Dashboard

**Files to create:**

- `src/admin/full-version/src/pages/apps/cultural/dashboard.vue`
- `src/admin/full-version/src/views/apps/cultural/` — cultural components

**Widgets:**

- [ ] Student distribution by cultural context — donut chart
- [ ] Resilience score comparison by cultural context — box plot
- [ ] Methodology effectiveness by cultural context — heatmap (method × culture → success rate)
- [ ] Focus patterns by cultural context — grouped bar chart

### ADM-012.2: Equity Alerts

**Acceptance:**

- [ ] Alert when any cultural group's mastery average diverges >10% from platform average
- [ ] Alert when content quality scores differ significantly by language
- [ ] Recommendations for content balancing (e.g., "Arabic physics questions are 30% fewer than Hebrew")

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/cultural/distribution` | Student counts by cultural context |
| GET | `/api/admin/cultural/resilience-comparison` | Resilience scores by group |
| GET | `/api/admin/cultural/methodology-by-context` | Method effectiveness per culture |
| GET | `/api/admin/cultural/equity-alerts` | Active equity alerts |

## Test

- [ ] Charts show real cultural distribution data
- [ ] Equity alerts fire when thresholds exceeded
- [ ] All labels render correctly in both RTL and LTR
