# Student Backend — Task Bundle

**Source**: [docs/student/15-backend-integration.md](../../docs/student/15-backend-integration.md) + every feature doc's "Backend Dependencies" section
**Date**: 2026-04-10
**Architect**: Lead Senior Architect review
**Status**: Ready for implementation (gated on `DB-05` and `DB-06`)

---

## Overview

This bundle gathers every new REST endpoint and SignalR hub event the student web needs. Every feature doc in [docs/student/](../../docs/student/README.md) lists the endpoints it consumes at the bottom; those lists have been deduplicated, grouped by functional area, and turned into 11 backend delivery tasks.

Prefix: `STB-*` (Student Backend).

Total estimated effort: **~35-50 engineer-days**, partially parallelizable after `STB-00` lands.

---

## Prerequisites

| Prereq | Why |
|---|---|
| [DB-05](../../docs/tasks/infra-db-migration/TASK-DB-05-contracts-library.md) — Extract `Cena.Api.Contracts` | All new DTOs and hub events land in the shared contracts library, not inside a host project |
| [DB-06](../../docs/tasks/infra-db-migration/TASK-DB-06-split-hosts.md) — Split hosts | New endpoints map onto `Cena.Student.Api.Host`, not the mixed host |
| [DB-08](../../docs/tasks/infra-db-migration/TASK-DB-08-role-timeouts-pool-isolation.md) — Role timeouts | New endpoints must run under `cena_student` role with 5 s statement timeout; any query that can't fit must move to an async projection |

---

## Task Index

| ID | Task | Feature Docs | Effort | Depends On | UI Consumers |
|---|---|---|---|---|---|
| [STB-00](TASK-STB-00-me-profile-onboarding.md) | `/api/me/*` — bootstrap, profile, settings, preferences, onboarding, devices | [03](../../docs/student/03-auth-onboarding.md), [04](../../docs/student/04-home-dashboard.md), [13](../../docs/student/13-notifications-profile.md) | 3-4d | DB-05, DB-06 | STU-W-04, STU-W-05, STU-W-14 |
| [STB-01](TASK-STB-01-session-start-and-hub.md) | `POST /api/sessions/start` + session launcher wiring through actors | [05](../../docs/student/05-learning-session.md) | 2-3d | STB-00 | STU-W-06 |
| [STB-02](TASK-STB-02-plan-review-recommendations.md) | `/api/me/plan/today`, `/api/review/due`, `/api/recommendations/sessions` | [04](../../docs/student/04-home-dashboard.md) | 3-4d | STB-00 | STU-W-05 |
| [STB-03](TASK-STB-03-gamification.md) | `/api/gamification/badges`, `/api/gamification/quests/*`, `/api/gamification/leaderboard` + new hub events | [06](../../docs/student/06-gamification.md), [11](../../docs/student/11-social-learning.md) | 4-5d | STB-00 | STU-W-07, STU-W-12 |
| [STB-04](TASK-STB-04-tutor.md) | `/api/tutor/*` — threads, messages (streaming), tool calls, OCR, TTS, export | [07](../../docs/student/07-ai-tutor.md) | 6-8d | STB-00, existing SAI work | STU-W-08 |
| [STB-05](TASK-STB-05-challenges.md) | `/api/challenges/*` — daily, boss, chains, tournaments | [10](../../docs/student/10-challenges.md) | 4-5d | STB-01, STB-03 | STU-W-11 |
| [STB-06](TASK-STB-06-social.md) | `/api/social/*` — class feed, peer solutions, friends, study rooms | [11](../../docs/student/11-social-learning.md) | 5-6d | STB-00, STB-03 | STU-W-12 |
| [STB-07](TASK-STB-07-notifications.md) | `/api/notifications/*` + receptive timing + quiet hours + channels | [13](../../docs/student/13-notifications-profile.md) | 3-4d | STB-00 | STU-W-14 |
| [STB-08](TASK-STB-08-knowledge-graph.md) | `/api/content/concepts*`, `/api/knowledge/path`, concept annotations | [09](../../docs/student/09-knowledge-graph.md) | 3-4d | STB-00 | STU-W-10 |
| [STB-09](TASK-STB-09-analytics-export.md) | `/api/analytics/time-breakdown`, `/api/analytics/flow-vs-accuracy`, PDF export, share tokens | [08](../../docs/student/08-progress-mastery.md) | 3-4d | STB-00 | STU-W-09 |
| [STB-10](TASK-STB-10-hub-contracts-expansion.md) | Additive hub events for all features + typed TS codegen | [05](../../docs/student/05-learning-session.md), [06](../../docs/student/06-gamification.md), [11](../../docs/student/11-social-learning.md) | 2-3d | DB-05 | STU-W-03, STU-W-06, STU-W-07, STU-W-12 |

---

## Endpoint Inventory (Canonical List)

Collected from every feature doc's "Backend Dependencies" section. Organized by functional area.

### Bootstrap & Identity — STB-00

- `GET /api/me`
- `GET /api/me/profile`
- `PATCH /api/me/profile`
- `GET /api/me/settings`
- `PATCH /api/me/settings`
- `POST /api/me/onboarding`
- `POST /api/classrooms/join`
- `PUT /api/me/preferences/home-layout`
- `GET /api/me/devices`
- `POST /api/me/devices/{id}/revoke`
- `POST /api/me/share-tokens`

### Sessions — STB-01 (+ existing)

- `POST /api/sessions/start` *(new)*
- `GET /api/sessions/*` *(exists)*
- `POST /api/sessions/{id}/resume` *(exists)*

### Plan & Recommendations — STB-02

- `GET /api/me/plan/today`
- `GET /api/review/due`
- `GET /api/recommendations/sessions`

### Gamification — STB-03

- `GET /api/gamification/badges`
- `GET /api/gamification/quests/active`
- `GET /api/gamification/quests/history`
- `GET /api/gamification/leaderboard?scope=&period=`

### Tutor — STB-04

- `GET /api/tutor/threads`
- `POST /api/tutor/threads`
- `GET /api/tutor/threads/{id}/messages`
- `POST /api/tutor/threads/{id}/messages`
- `POST /api/tutor/ocr`
- `POST /api/tutor/tts`
- `GET /api/tutor/threads/{id}/export?format=md|pdf`

### Challenges — STB-05

- `GET /api/challenges/daily`
- `POST /api/challenges/daily/start`
- `GET /api/challenges/daily/leaderboard`
- `GET /api/challenges/daily/history?limit=30`
- `GET /api/challenges/boss`
- `POST /api/challenges/boss/{id}/start`
- `GET /api/challenges/chains`
- `GET /api/challenges/chains/{id}`
- `GET /api/challenges/tournaments`

### Social — STB-06

- `GET /api/social/class-feed?page=&filter=`
- `POST /api/social/reactions`
- `POST /api/social/comments`
- `GET /api/social/peers/solutions?questionId=&subject=&sort=`
- `POST /api/social/peers/solutions/{id}/vote`
- `POST /api/social/peers/solutions/{id}/report`
- `GET /api/social/friends`
- `POST /api/social/friends/request`
- `POST /api/social/friends/{id}/accept`
- `GET /api/social/study-rooms`
- `POST /api/social/study-rooms`

### Notifications — STB-07

- `GET /api/notifications`
- `POST /api/notifications/{id}/read`
- `POST /api/notifications/mark-all-read`
- `DELETE /api/notifications/{id}`

### Knowledge Graph — STB-08

- `GET /api/content/concepts?subject=&depth=`
- `GET /api/content/concepts/{id}`
- `POST /api/knowledge/path?from=&to=`
- `POST /api/me/concept-annotations`
- `GET /api/me/concept-annotations?conceptId=`
- `POST /api/me/diagram-annotations`
- `GET /api/me/diagram-annotations?diagramId=`
- `GET /api/content/diagrams/{id}/teacher-layer`
- `POST /api/content/diagrams/{id}/export`

### Analytics & Export — STB-09

- `GET /api/analytics/time-breakdown?period=30d`
- `GET /api/analytics/flow-vs-accuracy?period=30d`
- `GET /api/analytics/concepts/{id}/history`
- `POST /api/analytics/export/pdf`

### Hub Events (additive) — STB-10

Client → Server (session):
- `SubmitAnswer`, `RequestHint`, `SkipQuestion`, `EndSession`, `PauseSession`, `ResumeSession`, `SubmitConfidenceRating`, `SubmitTeachBack`

Server → Client:
- `QuestionDelivered`, `AnswerEvaluated`, `HintDelivered`, `PhaseChanged`, `FlowScoreUpdated`, `CognitiveLoadHigh`
- `XpAwarded`, `BadgeEarned`, `QuestUpdated`, `QuestCompleted`, `LeaderboardChanged`
- `TutorTokenStreamed`, `TutorToolCalled`, `TutorToolCompleted`, `TutorMessageComplete`
- `NotificationDelivered`, `ClassFeedItemAdded`, `StudyRoomPresenceChanged`, `SessionEnded`

All events wrap in `BusEnvelope<T>` and land in `Cena.Api.Contracts/Hub/`.

---

## Dependency Graph

```text
                    [Infra Prereqs]
                 DB-05 ── DB-06 ── DB-08
                            │
                            ▼
                        STB-00 (me/profile/bootstrap)
                            │
              ┌────────────┬┴┬────────────┬────────────┐
              ▼            ▼ ▼            ▼            ▼
          STB-01       STB-02 STB-03  STB-07      STB-08
        (session)     (plan) (gami)  (notif)    (graph)
              │               │
              ▼               ▼
          STB-05         STB-06
        (challenges)   (social)
              │
              ▼
          STB-10 (hub events — can start earlier if contracts are ready)

                                        STB-04 (tutor) — depends on existing SAI work
                                        STB-09 (analytics) — depends on STB-00
```

---

## Task File Template

```markdown
# TASK-STB-NN: <title>

**Priority**: HIGH | MEDIUM | LOW
**Effort**: <range> days
**Depends on**: <list of IDs>
**UI consumers**: <list of STU-W-* IDs>
**Status**: Not Started

## Goal
<1-2 sentences>

## Endpoints
<table: method, path, purpose, rate limit, auth>

## Data Access
- Marten documents / projections involved
- Event types read or appended
- Async projection needs (if any query would exceed 5 s statement timeout)

## Hub Events (if applicable)
- Client → Server additions
- Server → Client additions

## Contracts
- DTOs to add under `Cena.Api.Contracts/Dtos/<area>/`
- Shared between mobile + web
- TypeScript codegen generated via script from DB-05

## Auth & Authorization
- Firebase JWT validation
- `ResourceOwnershipGuard.VerifyStudentAccess` usage
- Claims required

## Cross-cutting
- Rate limits (`api` policy or stricter)
- Statement-timeout budget (must fit in 5 s as `cena_student`)
- Observability (correlation ID, structured logs, Sentry)
- Pagination + filtering standard
- i18n-aware responses where text is returned

## Definition of Done
- [ ] All endpoints implemented and return correct shapes
- [ ] OpenAPI spec updated
- [ ] Unit tests on handlers
- [ ] Integration tests hitting a real Marten instance
- [ ] Rate limit policy assigned
- [ ] Host registers the endpoint group in `Cena.Student.Api.Host`
- [ ] DTOs live in `Cena.Api.Contracts`
- [ ] TypeScript types regenerated and checked in
- [ ] Mobile client lead has reviewed contract shape (parity guarantee)

## Out of Scope
<compact list>
```

---

## Cross-Cutting Concerns Every Backend Task Must Handle

Applies to every `STB-*` task:

1. **`cena_student` role constraints** — every query must complete in ≤ 5 s. Anything heavier goes into a Marten async projection (enable the commented-out projections in [MartenConfiguration.cs](../../src/actors/Cena.Actors/Configuration/MartenConfiguration.cs#L173)).
2. **Ownership guards** — `ResourceOwnershipGuard.VerifyStudentAccess(user, studentId)` before any student-scoped read or write.
3. **Idempotency** — any POST that can be retried must accept an idempotency key header.
4. **Pagination** — all list endpoints return `{ items, page, pageSize, total }` with sensible limits (≤ 100 per page).
5. **Filtering** — consistent query-param style (`?subject=math&from=2026-01-01`).
6. **ETag + Cache-Control** — for anything that can be CDN-cached (content, diagrams, subjects).
7. **i18n** — return Accept-Language-aware content where applicable.
8. **Mobile parity** — every new endpoint must be consumable by the Flutter app; confirm via code review from the mobile lead.
9. **Hub contract additions are additive only** — never remove or rename an existing event until mobile has migrated.
10. **Observability** — every handler logs with correlation ID, every error lands in Sentry with context.

---

## Related Task Bundles

- **[Student Web](../student-web/README.md)** — UI consumers of these endpoints
- **[Infra DB Migration](../../docs/tasks/infra-db-migration/README.md)** — prereqs DB-05, DB-06, DB-08
- **[Student AI Interaction](../../docs/tasks/student-ai-interaction/README.md)** — existing backend LLM / tutor foundation that STB-04 builds on
- [docs/student/15-backend-integration.md](../../docs/student/15-backend-integration.md) — canonical spec for all REST + hub contracts
