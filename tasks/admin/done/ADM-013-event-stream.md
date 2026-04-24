# ADM-013: Event Stream Monitor & Dead Letter Queue

**Priority:** P2 — system observability
**Blocked by:** ADM-001 (auth), ADM-003 (super admin role)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, SignalR

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

Cena uses NATS JetStream for inter-context messaging and Marten for event sourcing. Super admins need a real-time view of domain events flowing through the system, plus management of failed messages in the dead letter queue.

Reuse Vuexy's `apps/chat/` pattern for the real-time log viewer and `apps/email/` pattern for the DLQ inbox.

## Subtasks

### ADM-013.1: Live Event Stream

**Files to create:**

- `src/admin/full-version/src/pages/apps/system/events.vue`
- `src/admin/full-version/src/views/apps/system/events/` — event components

**Acceptance:**

- [ ] Real-time event feed via SignalR `AdminHub`
- [ ] Events displayed as chat-like messages with timestamp, type, and payload preview
- [ ] Filter by event type: ConceptAttempted, ConceptMastered, StagnationDetected, MethodologySwitched, SessionStarted, SessionEnded, etc.
- [ ] Pause/resume stream
- [ ] Click event to expand full payload (JSON viewer)
- [ ] Event rate counter (events/second)

### ADM-013.2: Dead Letter Queue

**Files to create:**

- `src/admin/full-version/src/pages/apps/system/dead-letters.vue`

**Acceptance:**

- [ ] Table of failed messages: timestamp, source, event type, error message, retry count
- [ ] Click to inspect full payload and stack trace
- [ ] Actions: retry (single), retry (bulk selected), discard, move to investigation
- [ ] Filter by source, event type, date range
- [ ] DLQ depth alert when count exceeds threshold

## .NET Backend Endpoints Required

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| GET | `/api/admin/events/recent` | Recent events (paginated) |
| WS | `/hubs/admin/events` | SignalR hub for real-time event stream |
| GET | `/api/admin/events/dead-letters` | Failed messages list |
| GET | `/api/admin/events/dead-letters/{id}` | Full DLQ item detail |
| POST | `/api/admin/events/dead-letters/{id}/retry` | Retry failed message |
| POST | `/api/admin/events/dead-letters/{id}/discard` | Discard failed message |
| POST | `/api/admin/events/dead-letters/bulk-retry` | Bulk retry |

## Test

- [ ] Live event stream displays events in real-time via SignalR
- [ ] Filtering reduces stream to selected event types only
- [ ] Pause stops new events from appearing, resume catches up
- [ ] DLQ retry moves message back to processing
- [ ] Only SUPER_ADMIN can access (CASL enforced)
