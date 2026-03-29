# BKD-004: Admin Dashboard API

**Priority:** P1 — serves ADM-004 frontend (Platform Overview)
**Blocked by:** BKD-001 (auth middleware), BKD-002 (user data)
**Estimated effort:** 2 days
**Stack:** .NET 9 Minimal API, Marten (PostgreSQL), Redis (caching)
**Frontend contract:** `tasks/admin/ADM-004-dashboard-home.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin dashboard (ADM-004) home page shows overview widgets, activity charts, alerts, and quick actions. Data is aggregated from users, content, focus, and mastery domains. Heavy queries should be cached in Redis with short TTLs.

## Endpoints

### BKD-004.1: Platform Overview

**`GET /api/admin/dashboard/overview`** — Policy: `ModeratorOrAbove`

**Response:**
```json
{
  "activeUsers": 142,
  "activeUsersChange": 12,
  "totalStudents": 3450,
  "totalStudentsChange": 5,
  "contentItems": 12800,
  "pendingReview": 47,
  "avgFocusScore": 72,
  "avgFocusScoreChange": -3
}
```

**Implementation:**
- `activeUsers`: count of users with session activity in last 15 minutes (Redis key scan or SignalR connection count)
- `totalStudents`: Marten `Query<AdminUser>().Count(u => u.Role == "STUDENT")`
- `contentItems`: Marten count of published questions
- `pendingReview`: Marten count of items in review stage
- `avgFocusScore`: aggregate from focus session data (last 7 days)
- `*Change`: compare current week vs previous week, return % delta
- Cache entire response in Redis (TTL: 60 seconds)

### BKD-004.2: User Activity Time Series

**`GET /api/admin/dashboard/activity?period=30d`** — Policy: `ModeratorOrAbove`

**Response:**
```json
{
  "period": "30d",
  "data": [
    { "date": "2026-03-01", "dau": 120, "wau": 450, "mau": 1200 },
    { "date": "2026-03-02", "dau": 135, "wau": 460, "mau": 1210 }
  ]
}
```

**Implementation:**
- Query Marten event stream for `SessionStarted` events grouped by day
- DAU: distinct users per day
- WAU: distinct users in rolling 7-day window
- MAU: distinct users in rolling 30-day window
- Cache in Redis (TTL: 5 minutes)

### BKD-004.3: System Alerts

**`GET /api/admin/dashboard/alerts`** — Policy: `ModeratorOrAbove`

**Response:**
```json
[
  { "id": "alert-1", "severity": "warning", "title": "High error rate", "message": "API error rate > 5% in last 10 minutes", "timestamp": "...", "source": "api-health" },
  { "id": "alert-2", "severity": "info", "title": "47 items pending review", "message": "Moderation queue depth above threshold", "timestamp": "...", "source": "content-pipeline" }
]
```

**Implementation:**
- Check actor system health
- Check pending moderation queue depth
- Check error rates from metrics
- Check for stale data (no new events in > 1 hour)

### BKD-004.4: Recent Admin Activity

**`GET /api/admin/dashboard/recent-activity?limit=20`** — Policy: `ModeratorOrAbove`

**Response:**
```json
[
  { "timestamp": "...", "userId": "...", "userName": "Admin User", "action": "user.suspend", "target": "student-123", "description": "Suspended user for ToS violation" }
]
```

**Implementation:**
- Query Marten event stream for admin action events
- Filter to last 20 events of admin-initiated types
- Include: user CRUD, role changes, content approvals, settings changes

## Files to Create

| File | Description |
|------|-------------|
| `src/actors/Cena.Actors/Api/Admin/AdminDashboardEndpoints.cs` | Minimal API endpoints |
| `src/actors/Cena.Actors/Api/Admin/AdminDashboardDtos.cs` | Response DTOs |
| `src/actors/Cena.Actors/Api/Admin/AdminDashboardService.cs` | Aggregation + caching logic |

## Test

- [ ] Overview returns real counts from Marten
- [ ] Activity chart returns 30 data points for last 30 days
- [ ] Alerts detect high error rates and queue depth
- [ ] Recent activity shows admin actions chronologically
- [ ] Redis caching reduces DB queries (check TTL behavior)
- [ ] Moderator sees only content-related metrics (scoped response)
- [ ] Teacher gets 403 (not ModeratorOrAbove)
