# TASK-STB-10: Hub Contracts Expansion + TypeScript Codegen

**Priority**: HIGH — unblocks realtime features across the board
**Effort**: 2-3 days
**Depends on**: [DB-05](../../docs/tasks/infra-db-migration/TASK-DB-05-contracts-library.md)
**UI consumers**: [STU-W-03](../student-web/TASK-STU-W-03-api-signalr-client.md), [STU-W-06](../student-web/TASK-STU-W-06-learning-session-core.md), [STU-W-07](../student-web/TASK-STU-W-07-gamification.md), [STU-W-12](../student-web/TASK-STU-W-12-social-learning.md)
**Status**: Not Started

---

## Goal

Consolidate all new hub events implied by STB-01 through STB-09 into a single additive expansion of `HubContracts.cs` (now living in `Cena.Api.Contracts/Hub/`), publish them through `CenaHub`, and generate the TypeScript counterparts via the codegen added in DB-05. This task is the integration seam between backend realtime work and the web client.

## Scope

### New events (server → client)

Session (from STB-01, many already exist):
- `QuestionDelivered` — existing, verify payload matches new DTOs
- `AnswerEvaluated` — existing, verify
- `HintDelivered` — existing, verify
- `PhaseChanged` — add if not present
- `FlowScoreUpdated` — add if not present
- `CognitiveLoadHigh` — add if not present
- `SessionEnded` — existing, verify

Gamification (from STB-03):
- `XpAwarded` — existing, verify
- `BadgeEarned` — existing, verify
- `QuestCreated`
- `QuestUpdated`
- `QuestCompleted`
- `LeaderboardChanged`

Home / Plan (from STB-02):
- `PlanUpdated`
- `ReviewDueChanged`

Tutor (from STB-04):
- `TutorTokenStreamed`
- `TutorToolCalled`
- `TutorToolCompleted`
- `TutorMessageComplete`

Challenges (from STB-05):
- `DailyChallengeLeaderboardUpdated`
- `BossBattleAttemptsChanged`
- `ChainUnlocked`
- `TournamentStarted`
- `TournamentEnded`

Social (from STB-06):
- `ClassFeedItemAdded`
- `ReactionChanged`
- `CommentAdded`
- `FriendRequestReceived`
- `FriendRequestAccepted`
- `StudyRoomPresenceChanged`
- `StudyRoomMessagePosted`

Notifications (from STB-07):
- `NotificationDelivered`
- `NotificationInboxChanged`

### New events (client → server)

Most session commands already exist. Verify:
- `SubmitAnswer`, `RequestHint`, `SkipQuestion`, `EndSession`, `PauseSession`, `ResumeSession`, `SubmitConfidenceRating`, `SubmitTeachBack`

No new client commands from v1 feature set.

### BusEnvelope consistency

Every event wraps in `BusEnvelope<T>`:

```csharp
public record BusEnvelope<T>(
    Guid Id,
    string Type,
    DateTimeOffset Timestamp,
    string? SessionId,
    string StudentId,
    T Payload,
    string? CorrelationId);
```

This is already the pattern; confirm every new event uses it consistently.

### Group membership

On connect, the hub auto-joins:
- `student:{studentId}` — personal events
- `class:{classId}` — class-wide events (if enrolled)
- `session:{sessionId}` — session events (on session start)
- `tutor:{threadId}` — tutor streaming (on first message in a thread)
- `studyroom:{roomId}` — study room presence (on join)

Document these in `HubContracts.cs` as constants and reuse via helpers.

### TypeScript codegen

Extend the codegen script added in DB-05 to:

1. Read every `public record` in `Cena.Api.Contracts/Dtos/**` and emit `.ts` types
2. Read every event record in `Cena.Api.Contracts/Hub/**` and emit:
   - `export type HubEventPayloads = { [eventName]: PayloadType }` map
   - Helper types: `type HubEvent<K extends keyof HubEventPayloads> = BusEnvelope<HubEventPayloads[K]>`
3. Emit a single `hub-events.ts` barrel file with the string-literal union of event names
4. Check the generated files into `src/student/full-version/src/api/types/` and fail CI if the checked-in files drift from regeneration

## Contracts

All type additions land in:

- `Cena.Api.Contracts/Hub/HubContracts.cs` — event type constants and payload records
- `Cena.Api.Contracts/Hub/BusEnvelope.cs` — envelope generic
- `Cena.Api.Contracts/Hub/HubGroups.cs` — new helper for group name constants

## Auth & Authorization

- Hub auth already exists via Firebase JWT + `access_token` query param
- Group join verification enforced server-side — a student cannot join `session:{id}` unless they own that session
- New tutor and study room groups enforce the same ownership rule

## Cross-Cutting

- Rate limit: existing hub policy (60 msg/min per connection)
- Every event carries `correlationId` for distributed tracing across REST → hub
- Every payload record is a C# `record` (immutable, camelCase-serialized)
- No breaking changes to existing events — this is strictly additive
- Version suffix convention: if an existing payload must change shape, introduce `EventName_V2` alongside, never mutate

## Definition of Done

- [ ] All new event types defined in `Cena.Api.Contracts/Hub/`
- [ ] Every new event has a payload record and is registered in `HubContracts.cs`
- [ ] Every new event is emitted by the corresponding STB task's server logic
- [ ] Group membership helpers added and used consistently
- [ ] Codegen script updated to handle DTOs + hub events + BusEnvelope generic
- [ ] Generated `.ts` files checked in under `src/student/full-version/src/api/types/`
- [ ] CI fails on stale generated files
- [ ] Web client (STU-W-03) consumes the typed events without `any` casts
- [ ] Mobile client continues to work — no existing event changed shape
- [ ] Integration tests covering at least 3 new event round-trips
- [ ] Documentation updated in `src/api/Cena.Api.Contracts/Hub/README.md`

## Out of Scope

- Refactoring existing event shapes — strictly additive
- New client → server commands — none required by v1 feature set
- Hub throttling changes — existing policy is sufficient
- New hub at a different path — everything goes through `/hub/cena`
- TypeScript runtime validation (Zod etc.) — types only, trust the server
