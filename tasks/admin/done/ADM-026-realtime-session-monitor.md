# ADM-026: Real-Time Session & Tutoring Monitor

**Priority:** P1 — teachers need live visibility into active student sessions
**Blocked by:** ADM-017 (tutoring dashboard, exists), SES-001 (SignalR hub)
**Estimated effort:** 2 days

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

ADM-017 provides a REST-based tutoring session dashboard (list, detail, budget, analytics). But it's poll-based — teachers cannot see sessions updating in real time. This task adds a live session monitor page where teachers see active sessions, live question attempts, mastery changes, and tutoring conversations as they happen. Uses SSE from the Admin API (which subscribes to NATS events internally).

## Subtasks

### ADM-026.1: Admin SSE Event Stream

**Files:**
- `src/api/Cena.Admin.Api/LiveMonitorEndpoints.cs`
- `src/api/Cena.Admin.Api/LiveMonitorService.cs` — NATS subscriber → SSE writer

**Endpoints:**
```
GET  /api/admin/live/sessions           — SSE stream of session events for teacher's students
GET  /api/admin/live/sessions/{studentId} — SSE stream filtered to one student
```

**Acceptance:**
- [ ] SSE stream emits JSON events for: `session.started`, `session.ended`, `question.attempted`, `mastery.updated`, `tutoring.started`, `tutoring.message`, `tutoring.ended`, `stagnation.detected`, `methodology.switched`
- [ ] Teacher sees only their students (filtered by class membership from claims)
- [ ] Service subscribes to `cena.events.student.>` on NATS, filters by teacher's student list
- [ ] Connection timeout: 30 minutes, client reconnects with `Last-Event-ID`
- [ ] Each event has `id` (monotonic), `event` (type), `data` (JSON payload)

### ADM-026.2: Live Session Monitor Page

**Files:**
- `src/admin/full-version/src/pages/sessions/live.vue` — live monitor page
- `src/admin/full-version/src/views/sessions/LiveSessionCard.vue` — per-student session card
- `src/admin/full-version/src/views/sessions/LiveActivityFeed.vue` — scrolling event feed

**Acceptance:**
- [ ] Page at `/sessions/live` in admin navigation
- [ ] Grid of `LiveSessionCard` — one per active student session
- [ ] Card shows: student name, subject, methodology, question count, accuracy %, fatigue indicator, duration
- [ ] Card updates in real time as events arrive (no polling)
- [ ] Fatigue indicator: green (<0.4), yellow (0.4–0.7), red (>0.7)
- [ ] Click card → navigates to session detail (ADM-017)
- [ ] `LiveActivityFeed`: chronological scrolling list of events across all students
- [ ] Feed entries: "{Student} answered {concept} — {correct/incorrect}", "{Student} started tutoring on {concept}", "{Student} mastered {concept}"
- [ ] Sound notification toggle for stagnation/high-fatigue events
- [ ] Counter badge: "X active sessions"

### ADM-026.3: SSE Client Composable

**Files:**
- `src/admin/full-version/src/composables/useLiveStream.ts` — SSE client with auto-reconnect

**Acceptance:**
- [ ] `useLiveStream(url)` returns `{ events: Ref<LiveEvent[]>, connected: Ref<boolean>, error: Ref<string|null> }`
- [ ] Auto-reconnects with exponential backoff on disconnect
- [ ] Passes `Last-Event-ID` header on reconnect
- [ ] Cleans up EventSource on component unmount
- [ ] Buffers last 200 events in memory (older events dropped)

## Definition of Done
- [ ] Live monitor page shows active sessions updating in real time
- [ ] Events appear within 500ms of NATS publish
- [ ] SSE auto-reconnects on network interruption
- [ ] Teacher sees only their students
- [ ] `vue-tsc --noEmit` passes
- [ ] `dotnet build` + `dotnet test` pass
