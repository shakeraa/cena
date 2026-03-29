# ACT-010: Session Event Publisher — Actor → NATS for SignalR Bridge

**Priority:** P0 — actors currently don't publish session events to NATS for external consumption
**Blocked by:** None (actors exist, NATS bus exists)
**Estimated effort:** 1 day

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The `LearningSessionActor` and `TutorActor` process learning interactions internally but many events stay within the actor or only go to Marten (event store). For the SignalR bridge (SES-001) to push real-time updates to browsers, these actors need to publish key events to NATS on per-student subjects (`cena.events.student.{studentId}.*`). The `NatsBusRouter` already handles inbound commands from NATS → actors. This task adds the outbound path: actors → NATS → SignalR.

## Subtasks

### ACT-010.1: Session Event NATS Publisher

**Files:**
- `src/actors/Cena.Actors/Sessions/SessionNatsPublisher.cs` — publishes session events to NATS
- `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs` — inject publisher, call on state changes

**Events to publish (per-student subjects):**
```
cena.events.student.{studentId}.session_started      → { sessionId, subject, methodology, startedAt }
cena.events.student.{studentId}.question_presented    → { sessionId, questionId, conceptId, conceptName, format, difficulty }
cena.events.student.{studentId}.answer_evaluated      → { sessionId, questionId, correct, score, explanation, masteryDelta, xpEarned }
cena.events.student.{studentId}.mastery_updated       → { conceptId, previousLevel, newLevel, status }
cena.events.student.{studentId}.hint_delivered         → { sessionId, questionId, hintText, hintLevel }
cena.events.student.{studentId}.cognitive_load_warning → { sessionId, fatigueScore, recommendation }
cena.events.student.{studentId}.session_ended          → { sessionId, summary }
```

**Acceptance:**
- [ ] `ISessionEventPublisher` interface, `NatsSessionEventPublisher` implementation
- [ ] Registered as scoped service via DI
- [ ] Each publish includes `Nats-Msg-Id` header for idempotency (UUIDv7)
- [ ] JSON serialization with `System.Text.Json` (camelCase)
- [ ] Fire-and-forget publish (don't block actor processing)
- [ ] Metrics: `cena_session_events_published_total` counter with `event_type` label

### ACT-010.2: Tutoring Event NATS Publisher

**Files:**
- `src/actors/Cena.Actors/Tutoring/TutoringNatsPublisher.cs`
- `src/actors/Cena.Actors/Tutoring/TutorActor.cs` — inject publisher

**Events to publish:**
```
cena.events.student.{studentId}.tutoring_started  → { sessionId, conceptId, triggerReason, methodology }
cena.events.student.{studentId}.tutor_message     → { sessionId, role, messagePreview (first 100 chars), turnNumber }
cena.events.student.{studentId}.tutoring_ended    → { sessionId, reason, turnCount, durationSeconds }
```

**Acceptance:**
- [ ] Same patterns as ACT-010.1 (idempotent, fire-and-forget, metrics)
- [ ] `messagePreview` truncated to 100 chars (full conversation NOT sent over NATS — privacy)
- [ ] `role` is `student` or `tutor`

### ACT-010.3: Verify Existing Event Subjects

**Files:**
- `src/actors/Cena.Actors/Bus/NatsSubjects.cs` — add new subject constants
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — verify no conflicts

**Acceptance:**
- [ ] All new subjects added as `const string` in `NatsSubjects`
- [ ] No overlap with existing command subjects
- [ ] Subject naming follows existing `cena.events.student.{studentId}.{event_type}` pattern

## Definition of Done
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Events appear on NATS subjects when actor processes commands (verified via `nats sub`)
- [ ] Idempotency headers prevent duplicate event processing
- [ ] No actor processing is blocked by event publishing
