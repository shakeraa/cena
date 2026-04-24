# TASK-STU-W-09: Progress & Mastery Dashboards

**Priority**: HIGH — retention and self-reflection surface
**Effort**: 3-4 days
**Phase**: 3
**Depends on**: [STU-W-04](TASK-STU-W-04-auth-onboarding.md)
**Backend tasks**: [STB-09](../student-backend/TASK-STB-09-analytics-export.md)
**Status**: Not Started

---

## Goal

Turn raw event-sourced data into a multi-tab progress experience: overview KPIs, full session history with filters, mastery breakdown with per-concept history, and learning-time analytics. This is the area where a web screen dramatically outshines mobile.

## Spec

Full specification in [docs/student/08-progress-mastery.md](../../docs/student/08-progress-mastery.md). All 19 `STU-PROG-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- `/progress` overview page with KPI row + 4 chart widgets + recent sessions list
- `/progress/sessions` full data table with filters (date, subject, mode, outcome, search)
- `/progress/sessions/:sessionId` → reuses the `/session/:id/replay` view from STU-W-06
- `/progress/mastery` two-pane: concept tree (left) + concept detail (right)
- `/progress/time` learning time analytics page
- Components:
  - `<KpiCard>` (from STU-W-01)
  - `<WeeklyXpChart>` — ApexCharts area
  - `<SubjectMasteryRadar>` — ApexCharts radar
  - `<StreakHeatmap>` — GitHub-style contribution grid
  - `<RecentSessionsList>` — compact card list
  - `<SessionHistoryTable>` — data table with pagination + CSV export
  - `<ConceptTree>` — expandable nested tree with mastery colors
  - `<ConceptDetailPanel>` — right pane with mastery level, BKT params (advanced toggle), history chart, last 10 attempts, related concepts
  - `<MasteryHistoryChart>` — per-concept line chart
  - `<LearningTimeChart>` — daily time bar
  - `<TimeOfDayHeatmap>` — hour-of-day heatmap
  - `<FlowVsAccuracyChart>` — dual-axis line
- PDF export action on `/progress` overview, driven by `POST /api/analytics/export/pdf` (STB-09)
- Side-by-side concept comparison: pick two concepts, render both histories
- Goal tracking: set a mastery target for a concept by a date; render a burn-down
- iCal export of planned sessions via `.ics` download
- Parent/tutor share token generation (creates a time-bound view-only link)
- Empty states for new students, no sessions, no mastery data
- All charts are lazy-loaded from a shared `useApexCharts()` composable
- All charts are keyboard-navigable and announce values to screen readers

Out of scope:

- Teacher gradebook view — out of scope for student app
- Ambition meter (metacognitive prompt) — stretch, defer
- Parent dashboard page itself — out of scope

## Definition of Done

- [ ] All 19 `STU-PROG-*` acceptance criteria in [08-progress-mastery.md](../../docs/student/08-progress-mastery.md) pass
- [ ] Every chart renders within 200 ms after data arrives
- [ ] Session history table supports filter + sort + pagination + CSV export without a full-page refresh
- [ ] Concept tree handles 500+ concepts without layout lag (virtualize if needed)
- [ ] Concept detail panel updates within 100 ms of selecting a concept
- [ ] PDF export succeeds on a 30-day dataset and includes the student's display name + date range
- [ ] Share token generation returns a working URL that loads a read-only view
- [ ] All charts pass a screen-reader check (values announced on focus)
- [ ] Playwright covers: overview render, session history filter + export, mastery tree selection, time analytics page, side-by-side comparison, goal tracking
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **ApexCharts + RTL** — some ApexCharts axes do not render correctly in RTL. Wrap charts with `dir="ltr"` but flip labels manually where needed.
- **Data volume** — students with months of history can produce 10k+ data points. Paginate + aggregate server-side; never render more than 200 points per chart.
- **Mastery BKT params** are confusing to students; gate behind an "advanced" toggle and add a tooltip that explains each parameter in plain language.
- **PDF export server load** — PDFs can take 3-5 seconds to generate. Use an async job pattern: POST returns a job ID, client polls or listens for a `ReportReady` hub event.
- **Share token abuse** — must be revokable and have an expiry. Surface all active tokens in `/settings/privacy` so students can audit who has access.
- **Concept comparison UX** — picking two concepts on a small screen is hard. Require `md` breakpoint or above; hide the feature on mobile-web.
