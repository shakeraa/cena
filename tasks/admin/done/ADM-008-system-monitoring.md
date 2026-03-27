# ADM-008: System Monitoring & Settings

**Priority:** P2 — super admin only
**Blocked by:** ADM-001 (auth), ADM-003 (super admin role)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, ApexCharts

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Super admins need visibility into platform health: actor system status, service health, error rates, and system configuration. This is the ops view of the Cena platform.

## Subtasks

### ADM-008.1: System Health Dashboard

**Files to create:**
- `src/admin/full-version/src/pages/apps/system/health.vue`
- `src/admin/full-version/src/views/apps/system/` — system components

**Acceptance:**
- [ ] Service status cards: API, Actor System, Database, Redis, S3 — green/yellow/red
- [ ] Error rate chart: errors per minute over last 24 hours
- [ ] Active actors count and resource usage
- [ ] Queue depths: SQS queues, pending jobs
- [ ] Uptime metrics per service

### ADM-008.2: Platform Settings

**Files to create:**
- `src/admin/full-version/src/pages/apps/system/settings.vue`

**Acceptance:**
- [ ] Organization settings: name, logo, timezone, default language
- [ ] Feature flags: toggle features on/off per org
- [ ] Focus engine config: degradation thresholds, microbreak intervals
- [ ] Mastery engine config: mastery thresholds, decay rates
- [ ] Email/notification templates management
- [ ] Audit log: all setting changes with who/when/what

### ADM-008.3: Audit Log

**Files to create:**
- `src/admin/full-version/src/pages/apps/system/audit-log.vue`

**Acceptance:**
- [ ] Searchable, filterable log of all admin actions
- [ ] Columns: timestamp, user, action, target, details
- [ ] Filters: date range, user, action type
- [ ] Export to CSV

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/admin/system/health` | Service health statuses |
| GET | `/api/admin/system/metrics` | Error rates, queue depths |
| GET | `/api/admin/system/actors` | Actor system status |
| GET | `/api/admin/settings` | Platform settings |
| PUT | `/api/admin/settings` | Update settings |
| GET | `/api/admin/audit-log` | Paginated audit log |

## Test

- [ ] Health dashboard shows real service statuses
- [ ] Settings changes persist and take effect
- [ ] Audit log captures all admin actions
- [ ] Only super admin can access system pages (CASL enforced)
- [ ] Feature flag toggle immediately affects platform behavior
