# LCM-002: GDPR Self-Service Deletion — 30-Day Hold with Data Export

**Priority:** P0 — legal compliance (GDPR Article 17, Israeli PPL 5741-1981)
**Blocked by:** LCM-001 (Actor Status Gate)
**Estimated effort:** 3 days
**Phase:** 2

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Context

Students and parents must be able to request account deletion. Israeli Privacy Protection Law and GDPR require data erasure within 30 days of request. A 30-day cooling-off period allows cancellation. After 30 days, a scheduled job performs hard delete: Firebase user removal, Marten event stream purge, Redis key cleanup, and PII anonymization in analytics.

Builds on SEC-005 (crypto-shredding) for event store PII removal.

## Subtasks

### LCM-002.1: AdminUser Data Model Extensions

**Files to modify:**
- `src/shared/Cena.Infrastructure/Documents/AdminUser.cs` — add deletion fields

**Acceptance:**
- [ ] Add `DeletionRequestedAt` (DateTimeOffset?)
- [ ] Add `DeletionScheduledFor` (DateTimeOffset?) — `DeletionRequestedAt + 30 days`
- [ ] Add `DeletionRequestedBy` (string?) — UID of requester (self or parent)
- [ ] Add `DataExportedAt` (DateTimeOffset?) — tracks GDPR portability compliance

### LCM-002.2: Self-Service Deletion API

**Files to create/modify:**
- `src/api/Cena.Admin.Api/AccountLifecycleService.cs` — new service
- `src/api/Cena.Admin.Api/AccountLifecycleEndpoints.cs` — new endpoint group

**Endpoints:**
- `POST /api/account/delete-request` — request own deletion (requires re-authentication)
- `POST /api/account/cancel-deletion` — cancel pending deletion
- `GET /api/account/export` — GDPR data export (JSON download)

**Acceptance:**
- [ ] Deletion request: sets `DeletionRequestedAt`, `DeletionScheduledFor`, status → `PendingDelete`
- [ ] Publishes `cena.account.status_changed` with `pending_delete` (triggers LCM-001 actor gate)
- [ ] Firebase user disabled immediately (no new sessions)
- [ ] Cancel deletion: clears deletion fields, status → `Active`, re-enables Firebase
- [ ] Cancel only allowed within 30-day window
- [ ] Data export: returns JSON with all Marten events, mastery data, session history (no PII from other students)

### LCM-002.3: Parent Deletion on Behalf of Minor

**Files to modify:**
- `src/api/Cena.Admin.Api/AccountLifecycleEndpoints.cs` — parent-scoped endpoint

**Endpoints:**
- `POST /api/parent/children/{id}/delete-request` — parent requests child deletion

**Acceptance:**
- [ ] Requires parent role + `student_ids` claim containing the child ID
- [ ] Same 30-day hold as self-service
- [ ] Parent can cancel deletion within 30 days
- [ ] Notification sent to school admin (awareness, not approval required)

### LCM-002.4: Scheduled Hard Delete Job

**Files to create:**
- `src/actors/Cena.Actors.Host/Jobs/DeletionPurgeJob.cs` — hosted service, runs daily

**Acceptance:**
- [ ] Queries Marten for `AdminUser` where `DeletionScheduledFor <= now` and `SoftDeleted == false`
- [ ] For each user:
  - [ ] Delete Firebase Auth user (`DeleteUserAsync`)
  - [ ] Purge Marten event stream (`session.Events.ArchiveStream(studentId)`)
  - [ ] Delete Redis keys: `account_status:*`, `student:*:profile`, `student:*:review_schedule`
  - [ ] Mark `AdminUser.SoftDeleted = true`
  - [ ] Log deletion to FERPA audit trail
- [ ] Runs once daily at 03:00 UTC (low-traffic window)
- [ ] Publishes `cena.account.hard_deleted` NATS event for downstream cleanup

### LCM-002.5: Flutter Deletion Flow

**Files to create:**
- Flutter: `lib/features/settings/delete_account_screen.dart`

**Acceptance:**
- [ ] Settings → Account → Delete My Account
- [ ] Multi-step: password re-entry + "I understand my data will be permanently deleted" checkbox
- [ ] 30-day countdown displayed after request
- [ ] "Cancel Deletion" button visible during hold period
- [ ] After hard delete: app shows "Account deleted" screen, clears all local data

### LCM-002.6: Tests

**Files to create:**
- `src/actors/Cena.Actors.Tests/Lifecycle/DeletionFlowTests.cs`

**Acceptance:**
- [ ] Test: deletion request sets correct fields and publishes NATS event
- [ ] Test: cancel deletion restores active status
- [ ] Test: cancel fails after 30-day window
- [ ] Test: hard delete purges all data stores
- [ ] Test: parent can request deletion for linked child only
- [ ] Test: data export returns correct student data (no cross-tenant leaks)
