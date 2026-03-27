# ADM-004: Admin Dashboard Home

**Priority:** P1 — first screen after login
**Blocked by:** ADM-001 (auth), ADM-002 (users for stats)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin dashboard home is the first screen after login. It provides a high-level overview of platform health, user activity, content status, and learning metrics. Adapt Vuexy's `dashboards/analytics.vue` and widget cards.

## Subtasks

### ADM-004.1: Platform Overview Cards

**Files to create:**
- `src/admin/full-version/src/pages/dashboards/admin.vue` — main admin dashboard
- `src/admin/full-version/src/views/admin/dashboard/` — dashboard components

**Widgets (top row):**
- [ ] **Active Users** — currently online count with sparkline (SignalR real-time)
- [ ] **Total Students** — count with % change vs last week
- [ ] **Content Items** — total questions/lessons with pending review count
- [ ] **Avg Focus Score** — platform-wide average with trend arrow

### ADM-004.2: Activity Charts

**Charts (middle section):**
- [ ] **User Activity** — line chart: DAU/WAU/MAU over last 30 days
- [ ] **Content Pipeline** — stacked bar: created/reviewed/approved/rejected per day
- [ ] **Focus Distribution** — histogram: student focus scores distribution
- [ ] **Mastery Progress** — area chart: avg mastery level over time by subject

### ADM-004.3: Quick Actions & Alerts

**Bottom section:**
- [ ] **Pending Reviews** — count + link to content moderation queue
- [ ] **System Alerts** — actor health warnings, failed jobs, error spikes
- [ ] **Recent Activity** — timeline of last 20 admin actions (who did what)
- [ ] **Quick Actions** — buttons: add user, create question, view reports, system settings

### ADM-004.4: Role-Based Dashboard Variants

- [ ] **Super Admin/Admin**: full dashboard with all widgets
- [ ] **Moderator**: content pipeline + pending reviews (no user stats)
- [ ] **Teacher**: class-specific metrics only (no platform-wide data)
- [ ] CASL-based conditional rendering per widget

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/dashboard/overview` | Summary stats (users, content, focus) |
| GET | `/api/admin/dashboard/activity` | DAU/WAU/MAU time series |
| GET | `/api/admin/dashboard/content-pipeline` | Content status counts over time |
| GET | `/api/admin/dashboard/focus-distribution` | Focus score histogram data |
| GET | `/api/admin/dashboard/mastery-progress` | Mastery trend data |
| GET | `/api/admin/dashboard/alerts` | Active system alerts |
| GET | `/api/admin/dashboard/recent-activity` | Recent admin action log |

## Test

- [ ] Dashboard loads within 2 seconds
- [ ] All charts render with real data from backend
- [ ] SignalR updates active user count in real-time
- [ ] Moderator sees only content-related widgets
- [ ] Teacher sees only class-level metrics
- [ ] Charts handle empty/sparse data gracefully (no blank areas)
- [ ] Arabic RTL: charts, numbers, labels all render correctly
