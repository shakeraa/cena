# SES-002: Session Lifecycle REST API — History, Resume, Analytics

**Priority:** P1 — clients need REST endpoints for non-real-time session operations
**Blocked by:** SES-001 (SignalR hub)
**Estimated effort:** 2 days

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

SignalR handles real-time session commands (start, answer, hint), but clients also need REST endpoints for session history, resuming interrupted sessions, and per-student analytics. These endpoints query Marten read models and actor state — they don't go through NATS.

## Subtasks

### SES-002.1: Session History Endpoints

**Files:**
- `src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs` — minimal API endpoints
- `src/api/Cena.Api.Host/Services/SessionQueryService.cs` — Marten projection queries

**Endpoints:**
```
GET  /api/sessions                    — list student's sessions (paginated, filterable by subject/date)
GET  /api/sessions/{sessionId}        — session detail (questions, scores, duration, methodology used)
GET  /api/sessions/{sessionId}/replay — question-by-question replay with timing data
GET  /api/sessions/active             — check if student has an active session (for resume)
```

**Acceptance:**
- [ ] All endpoints require Firebase JWT, extract `studentId` from claims
- [ ] Tenant-scoped: students can only see their own sessions
- [ ] Session list: `page`, `pageSize`, `subject`, `from`, `to` query params
- [ ] Session detail includes: questions attempted, correct count, fatigue score, methodology, duration, mastery deltas
- [ ] Replay returns ordered list of `QuestionAttemptDto` (question text, student answer, correct answer, time taken, hint used, explanation shown)
- [ ] Active session check returns `{ hasActive: bool, sessionId?: string, subject?: string, startedAt?: DateTimeOffset }`

### SES-002.2: Session Resume Flow

**Files:**
- `src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs` — add resume endpoint
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — handle `ResumeSession` message

**Endpoints:**
```
POST /api/sessions/{sessionId}/resume — resume an interrupted session
```

**Acceptance:**
- [ ] Validates session belongs to requesting student
- [ ] Validates session is in `Interrupted` or `Paused` state (not `Completed`)
- [ ] Sends `ResumeSession` command to actor via cluster
- [ ] Actor restores state from last checkpoint (Marten snapshot)
- [ ] Returns session state + next question to present
- [ ] If session expired (>24h since interruption): returns 410 Gone with summary

### SES-002.3: Student Analytics Endpoints

**Files:**
- `src/api/Cena.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs`

**Endpoints:**
```
GET  /api/analytics/summary           — overall stats (total sessions, avg score, streaks)
GET  /api/analytics/mastery           — per-concept mastery levels
GET  /api/analytics/progress?days=30  — daily session counts, scores over time
```

**Acceptance:**
- [ ] Summary: `totalSessions`, `totalQuestionsAttempted`, `overallAccuracy`, `currentStreak`, `longestStreak`, `totalXp`, `level`
- [ ] Mastery: array of `{ conceptId, conceptName, subject, masteryLevel (0-1), lastPracticed, status }`
- [ ] Progress: array of `{ date, sessionCount, questionsAttempted, accuracy }` for each day in range
- [ ] All queries use Marten async projections (no actor calls for reads)

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] All endpoints return correct data for seeded test students
- [ ] Pagination, filtering, and tenant scoping verified
- [ ] Resume flow tested with interrupted session
