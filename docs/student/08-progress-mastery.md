# 08 — Progress & Mastery

## Overview

Progress is where a student sees themselves over time: what they've learned, what's next, and how their effort translates into outcomes. This area benefits hugely from a web screen — more data can be rendered simultaneously than on mobile.

## Mobile Parity

- [mastery_list_widget.dart](../../src/mobile/lib/features/progress/mastery_list_widget.dart)
- [learning_time_card.dart](../../src/mobile/lib/features/progress/learning_time_card.dart)

Mobile shows a compact list. Web expands this into a multi-tab dashboard.

## Pages

### `/progress` — Overview Dashboard

Default tab. Summary KPIs + recent activity.

Layout:

```
┌──────────────────────────────────────────────────────────────┐
│  KPI Row: Level · XP · Streak · Avg accuracy · Time this week │
├─────────────────────────┬────────────────────────────────────┤
│  Weekly XP line chart   │  Concepts mastered this week       │
│  (ApexCharts)           │  (chips)                           │
├─────────────────────────┼────────────────────────────────────┤
│  Subject mastery radar  │  Session streak calendar heatmap   │
│  (ApexCharts)           │  (last 12 weeks)                   │
├─────────────────────────┴────────────────────────────────────┤
│  Recent sessions (5 rows) → "See all"                        │
└──────────────────────────────────────────────────────────────┘
```

### `/progress/sessions` — Session History

Full-bleed table with filters:

- Date range picker
- Subject filter
- Mode filter (standard, review, deep study, boss battle)
- Outcome filter (completed, abandoned, failed)
- Search (concept, question text)

Columns: date, subject, mode, duration, questions, accuracy, XP earned, flow %, actions (replay, review, delete).

Pagination + infinite scroll + CSV export.

Backed by `GET /api/sessions`.

### `/progress/sessions/:sessionId` — Session Detail

Alias for `/session/:sessionId/replay`. Reuses the replay UI.

### `/progress/mastery` — Mastery Breakdown

Primary mastery surface. Two-pane:

**Left pane** — concept tree:
- Subject → Topic → Concept hierarchy
- Mastery color per node (novice / learning / proficient / mastered / expert)
- Expand / collapse

**Right pane** — concept detail:
- Mastery level + confidence interval
- BKT parameters (learn rate, slip, guess) — shown in an "advanced" toggle
- History chart (line) — mastery over time
- Last 10 attempts table
- Related concepts
- "Practice this concept" CTA → starts a scoped session

### `/progress/time` — Learning Time Analytics

- **Daily time** bar chart (last 30 days)
- **Time by subject** donut chart
- **Time by hour-of-day** heatmap (when is the student most productive?)
- **Flow time %** trend — compares to accuracy
- **Break compliance** — did the student take recommended breaks in deep study?
- **Screen time report** (web-only) — time on pages vs time in session

## Components

| Component | Purpose |
|-----------|---------|
| `<KpiCard>` | Large KPI with label, value, trend arrow |
| `<WeeklyXpChart>` | ApexCharts area chart |
| `<SubjectMasteryRadar>` | ApexCharts radar chart |
| `<StreakHeatmap>` | GitHub-style contribution calendar |
| `<RecentSessionsList>` | Compact list of last 5 sessions |
| `<SessionHistoryTable>` | Paginated, filterable data table |
| `<ConceptTree>` | Nested expandable tree with mastery colors |
| `<ConceptDetailPanel>` | Right-pane concept detail view |
| `<MasteryHistoryChart>` | Per-concept line chart |
| `<LearningTimeChart>` | Bar chart for daily time |
| `<TimeHeatmap>` | Hour-of-day heatmap |
| `<FlowVsAccuracyChart>` | Dual-axis line chart |

## Mastery Model

- **BKT** (Bayesian Knowledge Tracing) for per-concept mastery estimate.
- Mastery level thresholds:
  - Novice: 0–0.25
  - Learning: 0.25–0.5
  - Proficient: 0.5–0.75
  - Mastered: 0.75–0.9
  - Expert: 0.9+
- Mastery updates on every `AnswerEvaluated` event.
- Decays slowly over time if not reinforced (SRS model).

Matches backend `mastery-engine-implementation.md`.

## Data Sources

- `GET /api/analytics/summary` — KPIs
- `GET /api/analytics/mastery` — concept mastery map
- `GET /api/analytics/progress?period=daily|weekly|monthly` — time series
- `GET /api/sessions?page=&subject=&mode=&from=&to=` — history
- `GET /api/analytics/time-breakdown?period=30d` — time analytics
- `GET /api/analytics/flow-vs-accuracy?period=30d` — correlation

## Web-Specific Enhancements

- **Side-by-side concept comparison** — select two concepts to compare mastery histories.
- **Export report** — PDF export of the progress dashboard with student name, date range, and signature line (useful for parents / tutors).
- **Goal tracking** — set a target mastery for a concept by a date; progress shown as a burn-down.
- **Calendar sync** — export session schedule to Google / iCal (web-only).
- **Parent / tutor share link** — generate a view-only token that grants read access to progress dashboards for 30 days.
- **Teacher gradebook view** — when enrolled in a class, surface the teacher's expectations inline.
- **Ambition meter** — metacognitive prompt: "How ambitious were you this week?" vs actual data, for self-calibration.

## Acceptance Criteria

- [ ] `STU-PROG-001` — `/progress` overview loads all 4 chart widgets + KPI row + recent sessions.
- [ ] `STU-PROG-002` — KPI cards show level, XP, streak, avg accuracy, time this week with trend arrows.
- [ ] `STU-PROG-003` — Weekly XP chart fetches from `/api/analytics/progress?period=weekly`.
- [ ] `STU-PROG-004` — Subject mastery radar renders with correct axis labels.
- [ ] `STU-PROG-005` — Streak heatmap renders last 12 weeks with tooltips showing date + XP.
- [ ] `STU-PROG-006` — Session history table supports filter, sort, pagination, CSV export.
- [ ] `STU-PROG-007` — Session detail opens the replay UI.
- [ ] `STU-PROG-008` — Concept tree shows subject → topic → concept hierarchy.
- [ ] `STU-PROG-009` — Concept detail shows mastery level, history, last 10 attempts, related concepts.
- [ ] `STU-PROG-010` — "Practice this concept" CTA launches a scoped session with that concept filter.
- [ ] `STU-PROG-011` — Learning time page renders daily bar, subject donut, hour heatmap, flow-vs-accuracy.
- [ ] `STU-PROG-012` — Break compliance chart computed from deep-study sessions.
- [ ] `STU-PROG-013` — Side-by-side concept comparison view available.
- [ ] `STU-PROG-014` — PDF export generates a styled progress report.
- [ ] `STU-PROG-015` — Goal tracking: set a mastery target and see a burn-down chart.
- [ ] `STU-PROG-016` — Calendar sync exports session schedule as `.ics`.
- [ ] `STU-PROG-017` — Parent/tutor share link generates a scoped view-only token.
- [ ] `STU-PROG-018` — All charts are keyboard-navigable and announce values to screen readers.
- [ ] `STU-PROG-019` — Empty state shown for new students with no data yet.

## Backend Dependencies

- `GET /api/analytics/summary` — exists
- `GET /api/analytics/mastery` — exists
- `GET /api/analytics/progress` — exists
- `GET /api/sessions` — exists (with filters)
- `GET /api/analytics/time-breakdown` — new
- `GET /api/analytics/flow-vs-accuracy` — new
- `GET /api/analytics/concepts/{id}/history` — new
- `POST /api/analytics/export/pdf` — new
- `POST /api/me/share-tokens` — new (parent/tutor read-only tokens)
