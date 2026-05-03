# TASK-STB-01: `POST /api/sessions/start` + Session Hub Wiring

**Priority**: HIGH — blocks all session-related UI
**Effort**: 2-3 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md)
**UI consumers**: [STU-W-06](../student-web/TASK-STU-W-06-learning-session-core.md)
**Status**: Not Started

---

## Goal

Add the session-start REST endpoint that the web launcher calls and confirm the full actor path from REST → NATS → `LearningSessionActor` → SignalR is consistent for the web client. Add any hub contracts the web needs that aren't already live.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `POST` | `/api/sessions/start` | Start a new learning session with full launcher config | `api` (5/min, idempotent with `Idempotency-Key` header) | JWT |

Request body:

```json
{
  "mode": "standard|review|deep-study|boss|diagnostic|teacher-assigned",
  "subjectIds": ["math", "physics"],
  "targetDurationMinutes": 30,
  "trainingWheels": {
    "hintGenerosity": "normal",
    "timerStrictness": "normal",
    "immediateFeedback": true,
    "confidencePrompts": true
  },
  "deepStudyConfig": {
    "blockCount": 3,
    "blockDurationMinutes": 30,
    "recoveryBreakMinutes": 5,
    "contentFocus": "single-subject"
  },
  "bossBattleId": null,
  "teacherAssignmentId": null,
  "classroomId": null
}
```

Response:

```json
{
  "sessionId": "sess_abc123",
  "hubGroup": "session:sess_abc123",
  "expectedQuestionCount": 22,
  "startedAt": "2026-04-10T14:32:00Z"
}
```

## Data Access

- **Writes**: appends `SessionStarted_V1` to the student stream; the actor system does the heavy lifting
- **Reads**: checks `TutoringSessionDocument` for an existing active session (returns 409 if one exists unless `?force=true`)
- **Statement timeout**: handler itself is trivial (< 100 ms); the actor activation is async and does not block the HTTP response beyond the initial write

## Hub Events

This task confirms the existing session-related hub events work correctly from the web client. The new events added here (if any) land in STB-10.

Existing events that must be re-verified against the web client:
- `QuestionDelivered`
- `AnswerEvaluated`
- `HintDelivered`
- `PhaseChanged`
- `FlowScoreUpdated`
- `CognitiveLoadHigh`
- `XpAwarded`
- `BadgeEarned`
- `SessionEnded`

Client → Server commands (verified by web):
- `SubmitAnswer`, `RequestHint`, `SkipQuestion`, `EndSession`, `PauseSession`, `ResumeSession`, `SubmitConfidenceRating`, `SubmitTeachBack`

## Contracts

Add to `Cena.Api.Contracts/Dtos/Sessions/`:
- `SessionStartRequest`
- `SessionStartResponse`
- `TrainingWheelsConfigDto`
- `DeepStudyConfigDto`

Reuse existing hub contract types from `Cena.Api.Contracts/Hub/`.

## Auth & Authorization

- Firebase JWT validation
- `ResourceOwnershipGuard.VerifyStudentAccess(user, studentId)` — student ID comes from claims
- If `classroomId` is provided, verify enrollment via a cheap classroom membership lookup

## Cross-Cutting

- `Idempotency-Key` header supported: same key + same payload returns the same `sessionId` within a 5-minute window
- 409 Conflict with `{ activeSessionId }` if an active session exists and `force=false`
- Handler logs with `correlationId` and tags `endpoint=sessions.start`
- Statement timeout budget: 500 ms (far under the 5 s `cena_student` limit)
- Response cache headers: `no-store`

## Definition of Done

- [ ] `POST /api/sessions/start` implemented, registered in `Cena.Student.Api.Host`
- [ ] DTOs live in `Cena.Api.Contracts/Dtos/Sessions/`
- [ ] All six session modes supported and routed correctly to the actor layer
- [ ] Idempotency key honored (Playwright test with duplicate POST)
- [ ] 409 returned on active session conflict; `force=true` ends the prior session cleanly
- [ ] Hub connection handshake with JWT verified from a Vue dev app
- [ ] All existing session hub events deliver payload-matching DTOs to a TypeScript client
- [ ] Integration test starts a session end-to-end (REST → actor activation → first `QuestionDelivered` received by test client)
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated
- [ ] Mobile lead review: confirm Flutter can also call this endpoint without new mobile work

## Out of Scope

- Actor-layer logic for new session modes — existing `LearningSessionActor` handles them
- Session resume — exists already (`POST /api/sessions/{id}/resume`)
- Deep-study block orchestration — exists in the actor layer
- New hub events — STB-10
