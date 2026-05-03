# 15 — Backend Integration

## Overview

The student web app communicates with the **same backend** as the Flutter mobile client. No parallel backend is created. Every new capability must either reuse an existing endpoint, or add an endpoint that mobile will eventually consume.

## Base URL & Environments

| Env | Base URL | Hub URL |
|-----|----------|---------|
| Local dev | `https://localhost:5001` | `wss://localhost:5001/hub/cena` |
| Dev (cloud) | `https://dev-api.cena.education` | `wss://dev-api.cena.education/hub/cena` |
| Staging | `https://staging-api.cena.education` | `wss://staging-api.cena.education/hub/cena` |
| Production | `https://api.cena.education` | `wss://api.cena.education/hub/cena` |

Environments configured via Vite env vars (`VITE_API_BASE`, `VITE_HUB_URL`).

---

## Auth

1. Firebase Auth (project `cena-platform`) handles sign-in.
2. Client stores the Firebase ID token in memory (not localStorage).
3. `$api` interceptor adds `Authorization: Bearer <token>` on every request.
4. Token is refreshed automatically by the Firebase SDK.
5. SignalR connection sends the token via the `access_token` query param on connect.
6. If the token expires mid-request → 401 → `$api` calls `firebase.auth.currentUser.getIdToken(true)` and retries once.
7. If refresh fails → redirect to `/login?returnTo=<current>`.

Backend validation is in `Cena.Infrastructure.Auth.Firebase` middleware; it extracts claims (`sub`, `email`, `role`, `tenant`) and populates `ctx.User`.

---

## REST Endpoints (Existing, Confirmed)

Source files:
- [src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs](../../src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs)
- [src/api/Cena.Api.Host/Endpoints/ContentEndpoints.cs](../../src/api/Cena.Api.Host/Endpoints/ContentEndpoints.cs)
- [src/api/Cena.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs](../../src/api/Cena.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs)

### Sessions (`/api/sessions`)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/` | List student's sessions (paginated, filterable) |
| GET | `/active` | Check for an unended session |
| GET | `/{id}` | Get a single session |
| GET | `/{id}/replay` | Replay log for a finished session |
| POST | `/{id}/resume` | Resume an existing session |

### Content (`/api/content`)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/questions/{id}` | Published question with translations (ETag cached) |
| GET | `/questions/{id}/explanation` | Canonical explanation |
| GET | `/subjects` | List of subjects |
| GET | `/subjects/{subject}/topics` | Topics under a subject |
| GET | `/diagrams/{id}` | Diagram payload |

### Student Analytics (`/api/analytics`)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/summary` | Level, XP, streaks, accuracy |
| GET | `/mastery` | Per-concept mastery |
| GET | `/progress` | Daily/weekly/monthly time series |

---

## REST Endpoints (New — Required for Student Web)

The following endpoints are implied by feature docs and must be scoped as backend work:

### Me / Profile
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/me` | 03, 04 |
| GET | `/api/me/profile` | 13 |
| PATCH | `/api/me/profile` | 13 |
| GET | `/api/me/settings` | 13 |
| PATCH | `/api/me/settings` | 13 |
| POST | `/api/me/onboarding` | 03 |
| PUT | `/api/me/preferences/home-layout` | 04 |
| GET | `/api/me/devices` | 13 |
| POST | `/api/me/devices/{id}/revoke` | 13 |
| POST | `/api/me/share-tokens` | 08, 13 |

### Session lifecycle
| Method | Path | Feature doc |
|--------|------|-------------|
| POST | `/api/sessions/start` | 05 |

### Content extensions
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/content/concepts` | 09 |
| GET | `/api/content/concepts/{id}` | 09 |
| POST | `/api/knowledge/path` | 09 |
| POST | `/api/me/concept-annotations` | 09 |
| POST | `/api/me/diagram-annotations` | 12 |

### Recommendations & Plan
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/me/plan/today` | 04 |
| GET | `/api/review/due` | 04 |
| GET | `/api/recommendations/sessions` | 04 |

### Gamification
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/gamification/badges` | 06 |
| GET | `/api/gamification/quests/active` | 06 |
| GET | `/api/gamification/quests/history` | 06 |
| GET | `/api/gamification/leaderboard` | 06, 11 |

### Tutor
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/tutor/threads` | 07 |
| POST | `/api/tutor/threads` | 07 |
| GET | `/api/tutor/threads/{id}/messages` | 07 |
| POST | `/api/tutor/threads/{id}/messages` | 07 |
| POST | `/api/tutor/ocr` | 07 |
| POST | `/api/tutor/tts` | 07 |
| GET | `/api/tutor/threads/{id}/export` | 07 |

### Challenges
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/challenges/daily` | 10 |
| POST | `/api/challenges/daily/start` | 10 |
| GET | `/api/challenges/daily/leaderboard` | 10 |
| GET | `/api/challenges/boss` | 10 |
| POST | `/api/challenges/boss/{id}/start` | 10 |
| GET | `/api/challenges/chains` | 10 |
| GET | `/api/challenges/chains/{id}` | 10 |

### Social
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/social/class-feed` | 11 |
| POST | `/api/social/reactions` | 11 |
| POST | `/api/social/comments` | 11 |
| GET | `/api/social/peers/solutions` | 11 |
| POST | `/api/social/peers/solutions/{id}/vote` | 11 |
| POST | `/api/social/peers/solutions/{id}/report` | 11 |
| GET | `/api/social/friends` | 11 |
| POST | `/api/social/friends/request` | 11 |
| GET | `/api/social/study-rooms` | 11 |

### Notifications
| Method | Path | Feature doc |
|--------|------|-------------|
| GET | `/api/notifications` | 13 |
| POST | `/api/notifications/{id}/read` | 13 |
| POST | `/api/notifications/mark-all-read` | 13 |
| DELETE | `/api/notifications/{id}` | 13 |

### Export
| Method | Path | Feature doc |
|--------|------|-------------|
| POST | `/api/analytics/export/pdf` | 08 |
| POST | `/api/content/diagrams/{id}/export` | 12 |

---

## SignalR Hub — `/hub/cena`

Source: `src/api/Cena.Api.Host/Hubs/CenaHub.cs` + `HubContracts.cs`.

### Connection

```ts
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const connection = new HubConnectionBuilder()
  .withUrl(`${hubUrl}`, {
    accessTokenFactory: async () => await firebase.auth.currentUser.getIdToken(),
  })
  .withAutomaticReconnect([0, 2000, 10000, 30000]) // exp backoff
  .configureLogging(LogLevel.Warning)
  .build()
```

### Group Membership

On connect, the hub auto-joins the student to:
- `student:{studentId}` — personal events
- `class:{classId}` — class-wide events (if enrolled)
- `session:{sessionId}` — active session events (after starting)

### Message Envelope

All messages follow `BusEnvelope<T>`:

```ts
interface BusEnvelope<T> {
  id: string              // UUID
  type: string            // event name
  timestamp: string       // ISO 8601
  sessionId?: string
  studentId: string
  payload: T
  correlationId?: string
}
```

### Client → Server Events (session)

- `SubmitAnswer` — `{ questionId, answer, timeSpentMs }`
- `RequestHint` — `{ questionId, hintLevel }`
- `SkipQuestion` — `{ questionId }`
- `EndSession` — `{}`
- `PauseSession` — `{}`
- `ResumeSession` — `{}`
- `SubmitConfidenceRating` — `{ questionId, rating }`
- `SubmitTeachBack` — `{ questionId, explanation }`

### Server → Client Events

Covered across feature docs:
- `QuestionDelivered`
- `AnswerEvaluated`
- `HintDelivered`
- `PhaseChanged`
- `FlowScoreUpdated`
- `CognitiveLoadHigh`
- `XpAwarded`
- `BadgeEarned`
- `QuestUpdated`
- `QuestCompleted`
- `SessionEnded`
- `NotificationDelivered`
- `TutorTokenStreamed`
- `TutorToolCalled`
- `TutorToolCompleted`
- `TutorMessageComplete`
- `LeaderboardChanged`
- `ClassFeedItemAdded`
- `StudyRoomPresenceChanged`

Any new event is **additive** to `HubContracts.cs`.

---

## Rate Limits

The backend enforces rate limits via ASP.NET Core rate-limiting middleware. Student-relevant limits:

- REST `api` policy: 120 requests/min per user
- Hub: 60 messages/min per connection (burst 10)
- Tutor message: 30 messages/hour per user
- Image OCR: 20 uploads/day per user
- Export PDF: 10/day per user
- Share tokens: 20 active tokens per user

Web client must handle `429` responses gracefully with backoff + user-facing toast.

---

## Offline / Resilience Patterns

- **$api interceptor** retries on transient errors (502/503/504) with backoff (max 3 attempts).
- **SignalR** reconnects with exponential backoff; in-flight `Client → Server` events are queued and replayed.
- **Optimistic UI** for XP, streak, mastery — rolls back on rejection.
- **Draft autosave** for long inputs to localStorage.
- **Service worker** caches shell + static assets for instant reload.

---

## Error Handling

| HTTP | Client Behavior |
|------|-----------------|
| 400 | Inline field errors + toast |
| 401 | Refresh token; on second failure, redirect to `/login` |
| 403 | "You don't have permission" modal + log |
| 404 | Empty state with retry |
| 409 | Inline conflict message + refresh data |
| 422 | Validation errors shown inline |
| 429 | Toast "slow down, try again in N seconds" |
| 500 | Sentry report + generic error modal |
| Network error | Offline banner + queued retry |

---

## Observability

- All network calls tagged with `correlationId` for request tracing.
- Frontend errors captured to Sentry.
- Frontend performance (LCP, CLS, FID) captured to Sentry performance.
- User journey events (session start, XP gained, badge unlock) sent to analytics pipeline.
- No PII in analytics payloads.

---

## Acceptance Criteria

- [ ] `STU-API-001` — `$api` wrapper matches admin's pattern (ofetch + interceptors).
- [ ] `STU-API-002` — Firebase token attached to every REST request and refreshed on 401.
- [ ] `STU-API-003` — SignalR client uses `@microsoft/signalr`, auto-reconnects, handles token expiry.
- [ ] `STU-API-004` — Hub events typed via shared TS types generated from `HubContracts.cs`.
- [ ] `STU-API-005` — BusEnvelope unwrapping centralized in a single composable.
- [ ] `STU-API-006` — Optimistic UI with rollback for XP, streak, mastery.
- [ ] `STU-API-007` — Rate limit (429) handled with backoff and toast.
- [ ] `STU-API-008` — Sentry wired up for frontend errors and performance.
- [ ] `STU-API-009` — All new endpoints listed above are specified (OpenAPI) and added to backend backlog.
- [ ] `STU-API-010` — TypeScript types for every endpoint and hub event colocated in `src/student/full-version/src/api/types/`.
- [ ] `STU-API-011` — Retry logic in `$api` covers 502/503/504 with exponential backoff, max 3 attempts.
- [ ] `STU-API-012` — Correlation IDs propagated in all requests and logged client-side.
- [ ] `STU-API-013` — Service worker caches shell for offline reload.
- [ ] `STU-API-014` — 401 refresh retry works once, then redirects to `/login`.
- [ ] `STU-API-015` — Draft autosave composable saves every 5 s to localStorage.
