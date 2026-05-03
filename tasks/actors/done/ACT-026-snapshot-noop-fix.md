# ACT-026: ForceSnapshot Is a No-Op — Enable Marten Inline Snapshots

**Priority:** P0 — CRITICAL (production blocker)
**Blocked by:** None
**Estimated effort:** 1 day
**Source:** Architect review 2026-03-27, Issue #1

---

## Problem

`StudentActor.ForceSnapshot()` opens a Marten session, resets `_eventsSinceSnapshot` to 0, but never appends a snapshot document or calls `SaveChangesAsync()` with projection data. The Marten inline snapshot projection is also **commented out** in `MartenConfiguration.cs:52`.

**Impact:** Every actor activation replays the **entire** event stream from event 0. At scale (thousands of events per student), activation latency will degrade linearly with event count.

## Files

- `src/actors/Cena.Actors/Students/StudentActor.cs` — `ForceSnapshot()` (lines 399–422)
- `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` — commented-out projection (line 52)
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` — snapshot aggregate type

## Subtasks

### ACT-026.1: Uncomment and wire Marten inline snapshot projection
- [ ] Uncomment `opts.Projections.Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline, 100);`
- [ ] Verify `StudentProfileSnapshot` Apply methods cover all event types that `StudentState` handles
- [ ] Register any missing event types in the projection

### ACT-026.2: Fix ForceSnapshot to actually persist
- [ ] `ForceSnapshot` must store the snapshot document explicitly via `session.Store(snapshot)` + `SaveChangesAsync()`
- [ ] Verify snapshot is loadable via `AggregateStreamAsync<StudentProfileSnapshot>` after creation
- [ ] `_eventsSinceSnapshot` only resets to 0 AFTER successful persistence

### ACT-026.3: Fix ConceptMasteryState deserialization
- [ ] `ConceptMasteryState` properties need `public set` or `init` for Marten STJ deserialization
- [ ] Add integration test: persist snapshot, load it, verify all fields roundtrip

### ACT-026.4: Activation benchmark
- [ ] Test: append 500 events, create snapshot, measure activation WITH vs WITHOUT snapshot
- [ ] Document the performance improvement

## Acceptance Criteria

- [ ] Snapshots are created every 100 events
- [ ] Actor activation loads snapshot + replays only post-snapshot events
- [ ] Build and all existing tests pass
