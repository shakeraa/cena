# LCM-001: Actor Status Gate — Account-Aware Routing & Actor Enforcement

**Priority:** P0 — blocks production launch (security gap)
**Blocked by:** None (all dependencies exist)
**Estimated effort:** 2 days
**Phase:** 1 (Foundation — everything else depends on this)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

`StudentActor` has zero awareness of account status. If an admin suspends a student whose actor is already warm in memory, the actor continues processing NATS messages. The HTTP layer blocks via `TokenRevocationMiddleware`, but NATS commands bypass HTTP entirely — they go straight to the actor through `NatsBusRouter`. This is a security gap that must be closed before production.

**Two-layer enforcement:**
1. **NatsBusRouter Redis gate** — O(1) pre-routing check, catches 99% of cases
2. **StudentActor in-memory check** — catches the edge case where actor was warm before Redis key was set

## Subtasks

### LCM-001.1: AccountStatus Enum & StudentState Field

**Files to modify:**
- `src/actors/Cena.Actors/Students/StudentMessages.cs` — add `AccountStatus` enum
- `src/actors/Cena.Actors/Students/StudentState.cs` — add `AccountStatus` field
- `src/actors/Cena.Actors/Events/LearnerEvents.cs` — add `AccountStatusChanged_V1` event
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` — include `AccountStatus` in snapshot

**Acceptance:**
- [ ] `AccountStatus` enum: `Active`, `Suspended`, `Locked`, `Frozen`, `PendingDelete`, `Expired`, `Grace`
- [ ] `StudentState.AccountStatus` defaults to `Active`
- [ ] `AccountStatusChanged_V1` event persisted to Marten stream
- [ ] Snapshot includes and restores `AccountStatus`

### LCM-001.2: NATS Event — Account Status Changed

**Files to modify:**
- `src/actors/Cena.Actors/Bus/NatsSubjects.cs` — add `cena.account.status_changed` subject
- `src/actors/Cena.Actors/Bus/NatsBusMessages.cs` — add `BusAccountStatusChanged` message type
- `src/api/Cena.Admin.Api/AdminUserService.cs` — publish NATS event on suspend/activate/delete

**Acceptance:**
- [ ] `AdminUserService.SuspendUserAsync()` publishes `cena.account.status_changed` with `{studentId, newStatus: "suspended", reason}`
- [ ] `AdminUserService.ActivateUserAsync()` publishes with `{studentId, newStatus: "active"}`
- [ ] `AdminUserService.SoftDeleteUserAsync()` publishes with `{studentId, newStatus: "pending_delete"}`
- [ ] Message includes `changedBy` (admin UID) and `changedAt` (timestamp)

### LCM-001.3: NatsBusRouter Redis Status Gate

**Files to modify:**
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — add pre-routing status check

**Acceptance:**
- [ ] Before routing any command, check Redis key `account_status:{studentId}`
- [ ] If value is `suspended`, `locked`, `frozen`, or `pending_delete` → reject, log, increment error counter
- [ ] Redis miss = assume `active` (fail-open at gate layer; actor is the backstop)
- [ ] Status check adds < 1ms latency (Redis GET is sub-millisecond)
- [ ] Rejected messages get a new error category `"account_blocked"` in `RecordError`

### LCM-001.4: Redis Status Cache Management

**Files to modify:**
- `src/api/Cena.Admin.Api/AdminUserService.cs` — set/clear Redis status key on status change
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — subscribe to `cena.account.status_changed` and update local cache

**Acceptance:**
- [ ] On suspend: `SET account_status:{studentId} suspended EX 86400` (24h TTL, refreshed on change)
- [ ] On activate: `DEL account_status:{studentId}` (absence = active)
- [ ] On soft delete: `SET account_status:{studentId} pending_delete EX 2592000` (30d)
- [ ] On lock: `SET account_status:{studentId} locked EX {lockDuration}`

### LCM-001.5: StudentActor — AccountStatusChanged Handler

**Files to modify:**
- `src/actors/Cena.Actors/Students/StudentActor.cs` — add `AccountStatusChanged` to message dispatch
- `src/actors/Cena.Actors/Students/StudentActor.Commands.cs` — handler implementation
- `src/actors/Cena.Actors/Bus/NatsBusRouter.cs` — route `cena.account.status_changed` to actor

**Acceptance:**
- [ ] `StudentActor.ReceiveAsync` handles `AccountStatusChanged` message
- [ ] On `Suspended`/`Locked`/`Frozen`: end active session (if any), update `_state.AccountStatus`, respond with error to any pending commands
- [ ] On `PendingDelete`: end session, passivate actor (poison self)
- [ ] On `Active`: clear status, resume normal operation
- [ ] All commands (`StartSession`, `AttemptConcept`, etc.) check `_state.AccountStatus != Active` before processing — reject with `ACCOUNT_BLOCKED` error code
- [ ] `AccountStatusChanged_V1` event persisted to event stream

### LCM-001.6: Tests

**Files to create:**
- `src/actors/Cena.Actors.Tests/Bus/NatsBusRouterStatusGateTests.cs`
- `src/actors/Cena.Actors.Tests/Students/StudentActorStatusTests.cs`

**Acceptance:**
- [ ] Test: suspended student's commands are rejected at router gate
- [ ] Test: suspended student's warm actor ends session and rejects commands
- [ ] Test: reactivated student can start sessions again
- [ ] Test: Redis miss allows routing (fail-open at gate)
- [ ] Test: `AccountStatusChanged_V1` event is persisted
- [ ] Test: snapshot round-trip preserves `AccountStatus`
