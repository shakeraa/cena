# TASK-STB-05: Challenges (Daily, Boss Battles, Card Chains, Tournaments)

**Priority**: MEDIUM — engagement driver
**Effort**: 4-5 days
**Depends on**: [STB-01](TASK-STB-01-session-start-and-hub.md), [STB-03](TASK-STB-03-gamification.md)
**UI consumers**: [STU-W-11](../student-web/TASK-STU-W-11-challenges.md)
**Status**: Not Started

---

## Goal

Deliver the challenges REST surface: daily challenge generation + leaderboard + history, boss battles with mastery gating and attempt caps, card chain state, and scheduled tournaments.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/challenges/daily` | Today's daily challenge + expiry | `api` | JWT |
| `POST` | `/api/challenges/daily/start` | Begin today's daily challenge session | `api` (2/day) | JWT |
| `GET` | `/api/challenges/daily/leaderboard` | Today's daily challenge leaderboard with own rank | `api` | JWT |
| `GET` | `/api/challenges/daily/history?limit=30` | Past 30 days of daily challenges with student's scores | `api` | JWT |
| `GET` | `/api/challenges/boss` | List available boss battles with unlock state | `api` | JWT |
| `GET` | `/api/challenges/boss/{id}` | Boss detail + attempts remaining today | `api` | JWT |
| `POST` | `/api/challenges/boss/{id}/start` | Begin a boss battle session | `api` (5/hour) | JWT |
| `GET` | `/api/challenges/chains` | List active chains for student | `api` | JWT |
| `GET` | `/api/challenges/chains/{id}` | Chain progress with timeline | `api` | JWT |
| `GET` | `/api/challenges/tournaments` | Scheduled tournaments with participation state | `api` | JWT |

## Data Access

- **Reads**:
  - `DailyChallengeDocument` (new, per date, shared across all students in the same locale)
  - `DailyChallengeAttemptProjection` (new async, per student per date)
  - `BossBattleDefinitionDocument` (catalog)
  - `StudentBossBattleProjection` (new, tracks attempts remaining + unlocked state)
  - `CardChainDefinitionDocument` + `StudentChainProgressProjection`
  - `TournamentDocument`
- **Writes**: `POST /start` appends a `ChallengeStarted_V1` event to the student stream, then delegates to `LearningSessionActor` via the regular session-start flow with a `mode` flag
- **Async projections** for leaderboards and attempt tracking — same pattern as STB-03
- **Statement timeout**: all reads are projection lookups; starts are delegations to the actor layer

## Hub Events

- `DailyChallengeLeaderboardUpdated` — ranking shift
- `BossBattleAttemptsChanged` — attempts remaining updated
- `ChainUnlocked` — new card unlocked in a chain
- `TournamentStarted` / `TournamentEnded`

All additive, land in STB-10.

## Daily Challenge Generation

- Cron job generates tomorrow's daily challenge at 23:30 UTC (configurable per region)
- Challenge is a fixed set of 10 questions drawn from the published question bank with balanced difficulty
- Same challenge for all students in a given locale; different locales may get different challenges if content coverage demands it
- Generation is idempotent on date — re-running the cron does not overwrite an existing challenge

## Boss Battle Gating

- Each boss has a `requiredMasteryThreshold` and a `conceptPrerequisites` list
- Server checks both before allowing a start; returns 403 with a structured error on fail
- Max 3 attempts per boss per day per student (configurable)
- Successful completion unlocks the exclusive badge + XP multiplier

## Card Chains

- Each chain has an ordered list of "cards" (question references)
- Answering a card correctly unlocks the next; wrong answers don't reset the chain, but the student sees the correct answer as part of the chain narrative
- Chain story context is rendered as markdown (client-side)
- Chain completion triggers a chain-complete badge

## Contracts

Add to `Cena.Api.Contracts/Dtos/Challenges/`:

- `DailyChallengeDto`, `DailyChallengeHistoryEntryDto`, `DailyChallengeLeaderboardPageDto`
- `BossBattleDto`, `BossBattleGateErrorDto`
- `CardChainDto`, `CardChainTimelineDto`, `CardChainNodeDto`
- `TournamentDto`, `TournamentParticipationStateDto`

## Auth & Authorization

- Firebase JWT
- `ResourceOwnershipGuard` on all student-scoped reads
- Daily challenge leaderboard visible to all; global scope for under-13 is hidden (reuse STB-03 privacy rule)

## Cross-Cutting

- Daily challenge is cacheable publicly per date with ETag
- Boss catalog is cacheable publicly
- Leaderboard short-TTL privately cached
- Handler logs with `correlationId`, tags `endpoint=challenges.*`
- Generation cron logs each run with a summary (questions picked, difficulty profile)

## Definition of Done

- [ ] All 10 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Challenges/`
- [ ] Daily challenge cron runs and produces a valid challenge for every active locale
- [ ] Cron is idempotent on re-run
- [ ] Boss gate enforces mastery threshold + attempts remaining server-side
- [ ] Chain state persists across sessions
- [ ] Tournament listing shows upcoming, active, and past tournaments correctly
- [ ] Integration tests for all four challenge types
- [ ] Hub events deliver on rank shifts and attempt changes
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: mobile will consume these same endpoints when it adopts challenges

## Out of Scope

- Boss artwork and Rive assets — content team
- Chain story authoring — content team
- Tournament pairing algorithms — use simple global rankings for v1
- Classroom-scoped challenges — future
- Replay of past daily challenges against the real leaderboard — only the current day counts; practice mode is client-only (STU-W-11 handles)
