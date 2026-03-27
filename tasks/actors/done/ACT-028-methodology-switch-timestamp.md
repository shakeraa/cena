# ACT-028: MethodologySwitched_V1 Uses Wall Clock in Apply — Non-Deterministic Replay

**Priority:** P0 — CRITICAL (event sourcing correctness)
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Architect review 2026-03-27, Issue #3

---

## Problem

`StudentState.Apply(MethodologySwitched_V1 e)` records `DateTimeOffset.UtcNow` instead of an event timestamp:

```csharp
MethodAttemptHistory[clusterKey].Add(new MethodologyAttemptRecord(
    e.NewMethodology, e.Trigger, e.StagnationScore, DateTimeOffset.UtcNow)); // BUG
```

During event replay (rehydration), this records **replay time** instead of the original event time. The same event produces different state depending on when it's replayed. This violates the codebase rule: "Uses event timestamp for deterministic replay — never wall clock."

**Impact:** `OfflineSyncHandler` compares `history[^1].SwitchedAt > evt.ClientTimestamp` to decide offline event acceptance weights. Non-deterministic timestamps cause inconsistent sync behavior across actor restarts.

## Files

- `src/actors/Cena.Actors/Students/StudentState.cs` — `Apply(MethodologySwitched_V1)` line 134
- `src/actors/Cena.Actors/Events/LearnerEvents.cs` — `MethodologySwitched_V1` record (no Timestamp field)

## Subtasks

### ACT-028.1: Add Timestamp to MethodologySwitched_V1
- [ ] Add `DateTimeOffset Timestamp` field to the event record
- [ ] This is a **schema evolution** — existing events in Marten won't have this field
- [ ] Default missing field to `DateTimeOffset.MinValue` in deserialization (STJ handles this)

### ACT-028.2: Fix Apply method
- [ ] Replace `DateTimeOffset.UtcNow` with `e.Timestamp` in the Apply method
- [ ] Update `StudentProfileSnapshot.Apply(MethodologySwitched_V1)` similarly if needed

### ACT-028.3: Fix all event emission sites
- [ ] `StudentActor.Commands.cs` — `HandleSwitchMethodology` (line 407)
- [ ] `StudentActor.Queries.cs` — `HandleStagnationDetected` (line 68)
- [ ] Pass `DateTimeOffset.UtcNow` as Timestamp when creating the event

## Acceptance Criteria

- [ ] `Apply(MethodologySwitched_V1)` uses event timestamp, not wall clock
- [ ] Replaying events produces identical state regardless of replay time
- [ ] Existing persisted events deserialize without error (default for missing Timestamp)
- [ ] Build and tests pass
