# ACT-029: DelegateEvent Staged but Never Flushed — Events Lost

**Priority:** P1 — HIGH (data loss)
**Blocked by:** None
**Estimated effort:** 0.25 days
**Source:** Architect review 2026-03-27, Issue #5

---

## Problem

`HandleDelegateEvent` stages events from child actors but never calls `FlushEvents()`:

```csharp
private Task HandleDelegateEvent(DelegateEvent del)
{
    StageEvent(del.Event);
    return Task.CompletedTask; // No FlushEvents()!
}
```

Events accumulate in `_pendingEvents` until the next command handler calls `FlushEvents()`, which could be arbitrarily far away — or never, if the actor passivates first. These events will be silently lost.

## Files

- `src/actors/Cena.Actors/Students/StudentActor.Queries.cs` — `HandleDelegateEvent` (lines 111–117)

## Fix

### ACT-029.1: Flush after staging delegate events
- [ ] Change `HandleDelegateEvent` to `async Task` and call `await FlushEvents()` after staging
- [ ] Apply the event to local state after flush (match pattern used by all command handlers)
- [ ] Add type-switch to apply the correct `_state.Apply()` overload based on event type

## Acceptance Criteria

- [ ] Delegated events from child actors are persisted immediately
- [ ] Events are applied to local state after successful persistence
- [ ] Build and tests pass
