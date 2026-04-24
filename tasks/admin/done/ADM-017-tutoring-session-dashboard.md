# ADM-017: Tutoring Session Dashboard

**Priority:** P0 — critical observability for SAI-009 TutorActor
**Blocked by:** None (SAI-009 TutorActor is complete)
**Estimated effort:** 3 days
**Stack:** Vue 3 + Vuetify 3 + TypeScript, Admin API (.NET 9)

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

SAI-009 delivered a full conversational TutorActor with multi-turn Socratic dialogue, RAG context retrieval, budget gating, and safety guards. The admin dashboard has **zero visibility** into tutoring sessions. Teachers and admins cannot see active sessions, review transcripts, check token budget consumption, or monitor resolution outcomes. This task adds the admin-facing tutoring dashboard.

## Backend: New Admin API Endpoints

### ADM-017.1: TutoringAdminService + Endpoints

**Files to create:**

- `src/api/Cena.Admin.Api/TutoringAdminDtos.cs`
- `src/api/Cena.Admin.Api/TutoringAdminService.cs`
- `src/api/Cena.Admin.Api/TutoringAdminEndpoints.cs`

**Endpoints:**

```
GET  /api/admin/tutoring/sessions
     Query: ?studentId=&status=active|completed|budget_exhausted&page=1&pageSize=20
     Returns: Paged list of TutoringSessionSummaryDto

GET  /api/admin/tutoring/sessions/{sessionId}
     Returns: TutoringSessionDetailDto (full transcript, context blocks, budget usage)

GET  /api/admin/tutoring/budget-status
     Query: ?classId=
     Returns: Per-student daily token usage, budget remaining, exhaustion alerts

GET  /api/admin/tutoring/analytics
     Returns: Active sessions count, avg turns per session, resolution rate, avg budget usage
```

**Data source:** Query `TutoringSessionDocument` from Marten. Read from `tutoring_sessions` projection table. Budget data from actor state via Proto.Actor PID request.

**Acceptance:**

- [ ] `TutoringAdminService` queries Marten `TutoringSessionDocument` — no fake data
- [ ] Sessions list supports filtering by studentId, status, date range
- [ ] Session detail returns full conversation turns with timestamps
- [ ] Budget status aggregates per-student daily token usage from session documents
- [ ] Analytics endpoint returns real aggregates (count, avg, rate)
- [ ] All endpoints require `ModeratorOrAbove` auth policy
- [ ] Wire endpoints in `Program.cs` via `app.MapTutoringAdminEndpoints()`

### ADM-017.2: Session List Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/tutoring/sessions/index.vue`
- `src/admin/full-version/src/views/apps/tutoring/sessions/SessionListTable.vue`

**Acceptance:**

- [ ] Data table with columns: Student Name, Status (chip), Turns, Duration, Budget Used, Started At
- [ ] Status chips: Active (blue), Completed (green), Budget Exhausted (orange), Safety Blocked (red)
- [ ] Filter bar: student search (autocomplete), status dropdown, date range picker
- [ ] Click row navigates to session detail page
- [ ] Pagination via API (server-side)
- [ ] Auto-refresh toggle (30s interval) for active sessions
- [ ] Empty state: "No tutoring sessions found"

### ADM-017.3: Session Detail / Transcript Page

**Files to create:**

- `src/admin/full-version/src/pages/apps/tutoring/sessions/[id].vue`
- `src/admin/full-version/src/views/apps/tutoring/sessions/TranscriptTimeline.vue`
- `src/admin/full-version/src/views/apps/tutoring/sessions/BudgetMeter.vue`

**Acceptance:**

- [ ] Header: student name, session status, duration, total turns
- [ ] Transcript timeline: chat-style layout, student messages left, tutor messages right
- [ ] Each tutor message shows: methodology used (Socratic/Direct/etc), RAG sources cited
- [ ] Budget meter: visual progress bar showing tokens used vs daily limit
- [ ] Context panel (collapsible): RAG blocks retrieved for this session
- [ ] Safety events highlighted in red if TutorSafetyGuard triggered
- [ ] Back button returns to session list

### ADM-017.4: Budget Analytics Card on Main Dashboard

**Files to modify:**

- `src/admin/full-version/src/views/admin/dashboard/` — add TutoringBudgetCard

**Acceptance:**

- [ ] Card shows: active sessions count, today's total token usage, students near budget limit
- [ ] Sparkline chart: token usage over last 7 days
- [ ] Alert badge if any student exhausted daily budget
- [ ] Click card navigates to tutoring sessions page

### ADM-017.5: Navigation

**Files to modify:**

- `src/admin/full-version/src/navigation/vertical/apps-and-pages.ts`

**Acceptance:**

- [ ] Add "AI Tutoring" heading after "Pedagogy" section
- [ ] Add "Tutoring Sessions" nav item → `apps-tutoring-sessions`
- [ ] Icon: `tabler-messages` or `tabler-robot`
- [ ] CASL subject: `Tutoring`, action: `read`
