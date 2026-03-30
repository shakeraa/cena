# LCM-003: Account Locking — Failed Login Protection

**Priority:** P1 — security hardening
**Blocked by:** LCM-001 (Actor Status Gate)
**Estimated effort:** 1.5 days
**Phase:** 3

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

Firebase has basic rate limiting on auth endpoints, but it's too lenient for a children's education platform. We need custom lock thresholds: 5 failures → 15-minute temp lock, 10 failures → permanent lock until admin/parent unlocks. Lock state propagates to the actor system via LCM-001 status gate.

## Subtasks

### LCM-003.1: Redis-Based Login Failure Counter

**Files to create:**
- `src/shared/Cena.Infrastructure/Auth/AccountLockService.cs` — `IAccountLockService`

**Acceptance:**
- [ ] `RecordFailedLogin(uid)` — increments `login_failures:{uid}` in Redis (TTL: 15 min auto-decay)
- [ ] `IsLocked(uid)` — returns lock state and unlock timestamp
- [ ] 5 failures in 15 min → set `account_status:{uid} locked` with 15-min TTL
- [ ] 10 failures total → set `account_status:{uid} locked` with no TTL (permanent until manual unlock)
- [ ] `RecordSuccessfulLogin(uid)` — clears failure counter
- [ ] `Unlock(uid)` — clears lock, clears failure counter

### LCM-003.2: Lock Check in Auth Flow

**Files to modify:**
- `src/shared/Cena.Infrastructure/Auth/TokenRevocationMiddleware.cs` — add lock check

**Acceptance:**
- [ ] Before token validation, check `IAccountLockService.IsLocked(uid)`
- [ ] If locked: return 423 Locked with `{ lockedUntil, reason: "too_many_failed_logins" }`
- [ ] If temp locked: include countdown in response

### LCM-003.3: AdminUser Lock Fields

**Files to modify:**
- `src/shared/Cena.Infrastructure/Documents/AdminUser.cs` — add lock fields
- `src/api/Cena.Admin.Api/AdminUserService.cs` — add unlock method

**Acceptance:**
- [ ] Add `LockedAt`, `LockedUntil`, `LockReason` fields to `AdminUser`
- [ ] `UnlockUserAsync(uid)` — clears lock fields, calls `IAccountLockService.Unlock(uid)`, publishes `cena.account.status_changed` with `active`
- [ ] Endpoint: `POST /api/admin/users/{id}/unlock` (AdminOnly)

### LCM-003.4: Admin Dashboard — Lock UI

**Files to modify:**
- `src/admin/full-version/src/views/apps/user/UserTabSecurity.vue` — add lock status + unlock button
- `src/admin/full-version/src/views/apps/user/UserList.vue` — add "locked" status badge

**Acceptance:**
- [ ] Security tab shows: lock status, locked-at time, failed login count, unlock-at time
- [ ] One-click unlock button with confirmation
- [ ] User list: red "Locked" badge next to locked accounts
- [ ] Failed login history table (last 10 attempts with timestamps)

### LCM-003.5: Parent Unlock Capability

**Files to modify:**
- Parent endpoint group (created in LCM-005)

**Acceptance:**
- [ ] `POST /api/parent/children/{id}/unlock` — parent unlocks child's locked account
- [ ] Requires parent role + `student_ids` claim check
- [ ] Same unlock logic as admin unlock
- [ ] Notification to parent on child account lock (push/email)

### LCM-003.6: Flutter — Locked Account Screen

**Files to create:**
- Flutter: `lib/features/auth/locked_account_screen.dart`

**Acceptance:**
- [ ] Intercept 423 response → show locked screen
- [ ] Temp lock: countdown timer, "Try again in X minutes"
- [ ] Permanent lock: "Contact your school admin or parent"
- [ ] "Forgot Password?" link prominently displayed
- [ ] Auto-retry when temp lock expires

### LCM-003.7: Tests

**Acceptance:**
- [ ] Test: 5 failures triggers 15-min lock
- [ ] Test: 10 failures triggers permanent lock
- [ ] Test: successful login clears counter
- [ ] Test: admin unlock restores access
- [ ] Test: lock propagates to actor system via Redis gate
- [ ] Test: temp lock auto-expires after 15 min
