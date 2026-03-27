# ACT-022: Consolidate Offline Sync — Use OfflineSyncHandler

**Priority:** P2 — dead code / duplication
**Blocked by:** None
**Estimated effort:** 0.5 days
**Source:** Actor system review — OfflineSyncHandler is unused dead code

---

## Context
`StudentActor.HandleSyncOfflineEvents` does its own inline Redis-based idempotency check (lines 807-876) rather than delegating to the dedicated `OfflineSyncHandler` class. The handler has a richer three-tier classification system (Unconditional, Conditional, ServerAuthoritative) and proper weight-based acceptance. The inline version is a simplified duplicate.

## Subtasks

### ACT-022.1: Wire OfflineSyncHandler into StudentActor
**Files:**
- `src/actors/Cena.Actors/Students/StudentActor.cs` — modify HandleSyncOfflineEvents
- `src/actors/Cena.Actors/Sync/OfflineSyncHandler.cs` — no changes needed

**Acceptance:**
- [ ] Inject `OfflineSyncHandler` into `StudentActor` constructor
- [ ] Replace inline sync logic with `await _syncHandler.ProcessAsync(cmd, _state)`
- [ ] Stage returned domain events via `StageEvent` for each
- [ ] Flush atomically via `FlushEvents()`
- [ ] Apply events to `_state`
- [ ] Return `ActorResult` based on `SyncResult`
- [ ] Remove dead inline sync code (lines 807-876)

### ACT-022.2: Test
**Acceptance:**
- [ ] Test: duplicate idempotency key is rejected (Redis SET NX)
- [ ] Test: concept removed from curriculum → weight=0, rejected
- [ ] Test: methodology changed → weight=0.75, accepted with reduced confidence
