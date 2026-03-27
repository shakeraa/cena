# ACT-027: Remove Duplicate NATS Publishing — Keep Outbox Only

**Priority:** P0 — CRITICAL (data integrity)
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Architect review 2026-03-27, Issue #2

---

## Problem

Events are published to NATS **twice**:
1. **Inline** in `StudentActor.FlushEvents()` (lines 367–373) — publishes after Marten commit
2. **Outbox** via `NatsOutboxPublisher` — polls Marten and publishes on 5s interval

Every downstream consumer receives each event twice, corrupting analytics and double-counting.

## Files

- `src/actors/Cena.Actors/Students/StudentActor.cs` — `FlushEvents()`, `PublishToNats()`
- `src/actors/Cena.Actors/Infrastructure/NatsOutboxPublisher.cs`

## Subtasks

### ACT-027.1: Remove inline NATS publishing from FlushEvents
- [ ] Remove the `foreach` loop (lines 367–373) that publishes events inline
- [ ] Remove `PublishToNats<T>` method if no longer called elsewhere
- [ ] Keep the escalation publish in `HandleStagnationDetected` (Queries.cs line 86) — fire-and-forget, not a domain event

### ACT-027.2: Align NATS subject naming
- [ ] Outbox uses `cena.events.{TypeName}`, inline used `cena.student.events.{typename}`
- [ ] Pick one convention and ensure consumers match

## Acceptance Criteria

- [ ] Each domain event published to NATS exactly once (via outbox)
- [ ] `FlushEvents` only persists to Marten
- [ ] Build and tests pass
