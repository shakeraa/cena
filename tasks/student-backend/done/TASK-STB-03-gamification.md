# TASK-STB-03: Gamification Endpoints (Badges, Quests, Leaderboards)

**Priority**: HIGH — blocks STU-W-07 gamification and STU-W-12 leaderboard surfaces
**Effort**: 4-5 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-07](../student-web/TASK-STU-W-07-gamification.md), [STU-W-12](../student-web/TASK-STU-W-12-social-learning.md)
**Status**: Not Started

---

## Goal

Stand up the gamification REST surface — badges, quests, leaderboards — plus additive hub events for realtime quest and leaderboard updates. Quest generation logic lives here too (mirroring mobile's `quest_generator.dart`).

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/gamification/badges` | Full badge catalog with unlock state for current student | `api` | JWT |
| `GET` | `/api/gamification/quests/active` | Active daily + weekly quests with progress | `api` | JWT |
| `GET` | `/api/gamification/quests/history` | Completed / expired quests | `api` | JWT |
| `GET` | `/api/gamification/leaderboard` | Leaderboard with scope + period + metric query params | `api` | JWT |

Query shapes:

- `/api/gamification/leaderboard?scope=class|friends|grade|school|region|global&period=today|week|month|all&metric=xp|mastery&limit=50`
- Response includes the requesting student's own rank even if outside the returned page

## Data Access

- **Reads**: `BadgeCatalogDocument` (new), `StudentBadgesProjection` (new async), `QuestInstanceDocument` (new), `LeaderboardProjection` (new async, segmented by scope+period)
- **Writes**: quest generation on schedule appends `QuestCreated_V1` events; badge unlock on rule match appends `BadgeEarned_V1` (existing)
- **Async projections** are critical here:
  - `LeaderboardProjection` — maintained per scope+period, segmented by `classId` / `gradeId` / `schoolId` / `region`. Recomputed on `XpAwarded` events.
  - `StudentBadgesProjection` — lists earned + progress-toward badges per student.
  - `QuestProgressProjection` — maintains per-student quest progress, notifies on completion.
- **Statement timeout**: all endpoints are projection lookups, well under 100 ms

## Hub Events (additive, land in STB-10)

- `QuestCreated` — new quest available
- `QuestUpdated` — progress changed
- `QuestCompleted` — quest finished, XP awarded
- `BadgeEarned` — already exists; wire correctly for web
- `LeaderboardChanged` — ranking shifted within the student's current scope+period view
- `XpAwarded` — already exists; wire

## Quest Generation

- Quest generator service runs on a cron (every hour for daily quest refresh at midnight per timezone; every Sunday for weekly)
- Quest types (mirroring mobile):
  - Complete N sessions
  - Master N concepts
  - Hit M% accuracy in a subject
  - Maintain streak
  - Teach back N times
  - Help N peers
- Each student gets a personalized quest bundle based on their mastery + subject preferences
- Quest payload matches mobile's `quest_models.dart`

## Contracts

Add to `Cena.Api.Contracts/Dtos/Gamification/`:

- `BadgeDto`, `BadgeCategoryDto`, `BadgeRarityDto`
- `QuestDto`, `QuestProgressDto`, `QuestTypeDto`
- `LeaderboardEntryDto`, `LeaderboardPageDto`, `LeaderboardScopeDto`
- `XpAwardDto` (for the hub event)

## Auth & Authorization

- Firebase JWT
- Leaderboard with `scope=class` requires classroom enrollment
- Leaderboard with `scope=friends` filters by the student's friends list (see STB-06)
- Under-13 students can see class and friends leaderboards but not global (privacy rule — enforced server-side)

## Cross-Cutting

- Badge catalog is cacheable publicly with `ETag` and `max-age=3600`
- Personal badge state and quest state are cacheable privately with short TTL
- Leaderboard is cacheable privately with 30 s TTL
- Quest generation logs each generated batch for debugging
- Handler logs with `correlationId`, tags `endpoint=gamification.*`

## Definition of Done

- [ ] Four endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Gamification/`
- [ ] Quest generator cron job running in `Cena.Actors.Host` or equivalent
- [ ] Three new async projections enabled in `MartenConfiguration`
- [ ] Leaderboard returns own-rank even when outside the page
- [ ] Global leaderboard hidden for under-13 (verified with a test account)
- [ ] Hub events deliver correct payloads
- [ ] Integration tests cover: fresh student (empty quests), student with 5 quests, leaderboard rank shift after XP award, under-13 privacy enforcement
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: Flutter app can consume these endpoints for its own gamification

## Out of Scope

- Badge artwork or Rive animation assets — content team
- Tournament-specific leaderboards — STB-05
- Quest manual creation by teachers — future
- Anti-cheat for leaderboard manipulation — future (server rate limits + event sourcing make the baseline hard to game)
