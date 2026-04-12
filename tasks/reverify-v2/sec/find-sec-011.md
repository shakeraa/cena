---
id: FIND-SEC-011
task_id: t_1705a03eaaba
severity: P0 — Critical
lens: sec
tags: [reverify, sec, security, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
---

# FIND-sec-011: Cross-tenant reads + destructive writes in Mastery/Messaging/GDPR (partial regression of FIND-sec-005)

## Summary

Cross-tenant reads + destructive writes in Mastery/Messaging/GDPR (partial regression of FIND-sec-005)

## Severity

**P0 — Critical** — REGRESSION

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

# FIND-sec-011 (P0, partial regression of FIND-sec-005): cross-tenant reads & destructive writes in Mastery / Messaging / GDPR services

**Severity**: P0 (cross-tenant reads + destructive writes; right-to-erasure executable cross-school)

**related_prior_finding**: FIND-sec-005

**Files**:
- src/api/Cena.Admin.Api/MasteryTrackingService.cs (74-198)
- src/api/Cena.Admin.Api/AdminApiEndpoints.cs (211-274 — wire ClaimsPrincipal)
- src/api/Cena.Admin.Api/MessagingAdminService.cs (34-169)
- src/api/Cena.Admin.Api/MessagingAdminEndpoints.cs (33-62)
- src/api/Cena.Admin.Api/GdprEndpoints.cs (28-110)
- src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs (and IRightToErasureService impl)
- src/api/Cena.Admin.Api.Tests/ (new file: CrossTenantWriteEnforcementTests.cs)

**Goal**: extend the FIND-sec-005 contract to every admin service that reads or writes per-student / per-class data. No method may dereference a student/class id without verifying that the caller's school matches. The right-to-erasure route must NOT be reachable cross-school under any non-SUPER_ADMIN role.

**Background**: FIND-sec-005 closed cross-tenant read holes in `FocusAnalyticsService` only. Three other admin services have the identical bug pattern:

1. **MasteryTrackingService** — `GetClassMasteryAsync(classId)`, `GetStudentOverridesAsync(studentId)`, `RemoveOverrideAsync(studentId, overrideId)` execute against any classId/studentId without verifying the caller's school. The rollup query joins to ClassMasteryRollupDocument by classId, then queries StudentProfileSnapshot by `r => r.SchoolId == rollup.SchoolId` (the rollup's school, not the caller's).

2. **MessagingAdminService** — `GetThreadsAsync`, `GetThreadDetailAsync`, `GetContactsAsync` query global ThreadSummary and AdminUser collections. A moderator at any school sees every messaging thread across the platform, every participant id/email, and the full text of every conversation by id.

3. **GdprEndpoints** — every consent, export, and erasure route is AdminOnly with no per-id tenant check. The export route returns the full StudentProfileSnapshot. The erasure route DESTROYS data. A school-A admin can erase a school-B student's data with one call.

**Scope**:
1. Mastery: add ClaimsPrincipal to GetClassMasteryAsync, GetStudentOverridesAsync, RemoveOverrideAsync. Each fetches the doc, then enforces caller-school == doc-school or returns null/false. Use the existing GetStudentMasteryAsync (line 62) as the reference pattern — it already does this.
2. Messaging: add ClaimsPrincipal to GetThreadsAsync, GetThreadDetailAsync, GetContactsAsync. Threads/contacts must filter by the caller's school via the participant's school_id (for student participants) or the AdminUser.School field (for admin participants). For non-SUPER_ADMIN, drop any thread that has no participant in the caller's school.
3. GDPR: rewrite GdprEndpoints to load the AdminUser/Student profile first and then call a new `GdprResourceGuard.VerifyStudentBelongsToCallerSchool(student, caller)` which throws 404 on mismatch. Apply to all 6 routes (consents GET/POST/DELETE, export, erasure POST + GET status).
4. Cross-tenant test file `CrossTenantWriteEnforcementTests.cs` with at least 12 tests (6 negative cross-school + 6 positive same-school).

**Definition of Done**:
- [ ] Every public method on MasteryTrackingService, MessagingAdminService, GdprConsentManager, RightToErasureService, StudentDataExporter taking a studentId/classId also takes a ClaimsPrincipal
- [ ] All endpoints in AdminApiEndpoints.cs / MessagingAdminEndpoints.cs / GdprEndpoints.cs pass ctx.User to those methods
- [ ] CrossTenantWriteEnforcementTests.cs has at least 12 tests
- [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
- [ ] Branch: `<worker>/<task-id>-sec-011-mastery-messaging-gdpr-tenant-scope`

**Files to read first**:
- src/api/Cena.Admin.Api/MasteryTrackingService.cs (especially line 62 GetStudentMasteryAsync — reference pattern)
- src/api/Cena.Admin.Api/FocusAnalyticsService.cs (FIND-sec-005 fix — reference pattern)
- src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs
- src/api/Cena.Admin.Api/GdprEndpoints.cs
- src/shared/Cena.Infrastructure/Compliance/GdprConsentManager.cs
- src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs
- src/shared/Cena.Infrastructure/Compliance/StudentDataExporter.cs


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_1705a03eaaba`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
