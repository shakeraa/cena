# ADM-023: Token Budget & NATS Monitoring

**Priority:** P2 — cost management + event bus health
**Blocked by:** ADM-017 (shares budget data model)
**Estimated effort:** 1.5 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Admin API (.NET 9)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The TutorActor enforces daily token budgets per student, and the entire platform uses NATS JetStream for event sourcing (8 durable streams, 90-day retention). The admin dashboard has no visibility into token costs or NATS health. The `/api/admin/system/nats-stats` endpoint exists but returns a stub. This task adds real monitoring.

## Subtasks

### ADM-023.1: NATS Stats — Real Implementation

**Files to modify:**

- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — replace nats-stats stub with real implementation
- `src/api/Cena.Admin.Api/SystemMonitoringService.cs` — add `GetNatsStatsAsync()`

**Acceptance:**

- [ ] Connect to NATS via injected `IConnection` from `NATS.Net`
- [ ] Query JetStream API for all streams: `js.ListStreamsAsync()`
- [ ] Per stream: name, message count, byte size, consumer count, last sequence, first/last timestamp
- [ ] Per consumer: name, pending count, ack pending, delivered count, last delivered
- [ ] Return `NatsStatsDto` with stream list and aggregate metrics
- [ ] Endpoint requires `SuperAdminOnly` auth policy

### ADM-023.2: Token Budget Admin Endpoints

**Files to create:**

- `src/api/Cena.Admin.Api/TokenBudgetAdminDtos.cs`

**Files to modify:**

- `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` — add token budget endpoints under system group

**Endpoints:**

```
GET  /api/admin/system/token-budget
     Query: ?classId=&date=
     Returns: Per-student token usage for the day, budget limit, remaining, cost estimate

GET  /api/admin/system/token-budget/trend
     Query: ?days=7
     Returns: Daily aggregate token usage over N days

PUT  /api/admin/system/token-budget/limits
     Body: { dailyLimitPerStudent, monthlyLimitTotal }
     Returns: Updated limits
```

**Data source:** Token usage from `TutoringSessionDocument` (each session records tokens consumed). Budget limits from system settings (Marten).

**Acceptance:**

- [ ] Token usage summed from tutoring session events per student per day
- [ ] Cost estimate calculated from token count × model price (from AI settings)
- [ ] Trend endpoint returns daily aggregates for chart
- [ ] Limits endpoint updates system settings and broadcasts via NATS

### ADM-023.3: NATS Health Widget

**Files to create:**

- `src/admin/full-version/src/views/apps/system/health/NatsHealthCard.vue`

**Files to modify:**

- `src/admin/full-version/src/pages/apps/system/health.vue` — add NATS card

**Acceptance:**

- [ ] Card showing: total streams, total messages, total bytes, consumer lag
- [ ] Per-stream expandable rows: stream name, msg count, consumer count, last activity
- [ ] Alert if any consumer has pending > 1000 (indicating processing lag)
- [ ] Alert if any stream byte size > 1GB (indicating retention policy issue)
- [ ] Auto-refresh every 60 seconds

### ADM-023.4: Token Budget Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/system/token-budget.vue`
- `src/admin/full-version/src/views/apps/system/token-budget/BudgetTrendChart.vue`
- `src/admin/full-version/src/views/apps/system/token-budget/StudentUsageTable.vue`

**Acceptance:**

- [ ] Stat cards: Today's Total Tokens, Estimated Cost, Students Near Limit, Budget Utilization %
- [ ] Trend chart: daily token usage over last 7/14/30 days (toggle)
- [ ] Student usage table: student name, tokens today, budget limit, % used, cost estimate
- [ ] Sort by usage descending (heaviest users first)
- [ ] Budget limit editor (SuperAdmin only): update daily/monthly limits with confirmation
- [ ] Class filter: filter usage by class

### ADM-023.5: Navigation

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts`

**Acceptance:**

- [ ] Add "Token Budget" under System heading
- [ ] Icon: `tabler-coins`
- [ ] CASL subject: `System`, action: `manage`
