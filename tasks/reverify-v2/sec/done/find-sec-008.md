---
id: FIND-SEC-008
task_id: t_074e96ac9059
severity: P0 — Critical
lens: sec
tags: [reverify, sec, security]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-008: Cross-tenant write surface in AdminUserService (any ADMIN can edit/delete any user)

## Summary

Cross-tenant write surface in AdminUserService (any ADMIN can edit/delete any user)

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

# FIND-sec-008 (P0): cross-tenant write surface in AdminUserService

**Severity**: P0 (cross-tenant write/read surface across the entire admin user-management API)

**Files**:
- src/api/Cena.Admin.Api/AdminUserService.cs (130-560)
- src/api/Cena.Admin.Api/AdminUserEndpoints.cs (47-213)
- src/api/Cena.Admin.Api.Tests/ (new file: AdminUserServiceTenantScopingTests.cs)

**Goal**: enforce that an ADMIN-role caller can only read/write AdminUser documents that belong to their own school_id claim. Match the existing pattern in ListUsersAsync/GetStatsAsync which already do this correctly. SUPER_ADMIN keeps unrestricted access.

**Background**: see docs/reviews/agent-sec-reverify-2026-04-11.md FIND-sec-008. The entire admin user-management surface (`/api/admin/users/{id}` GET, PUT, DELETE; `/suspend`, `/activate`, `/force-reset`, `/sessions/{sid}` DELETE, `/api-keys/{keyId}` DELETE, `/security`, `/activity`, `/sessions`) is reachable by any caller with the ADMIN role on ANY school, without checking that the target user id belongs to the caller's school. ResourceOwnershipGuard.cs:56-61 explicitly comments "ADMIN/MODERATOR... no per-student ownership check is needed here. School-level scoping is handled by TenantScope.GetSchoolFilter()." That promise is broken on every method in this file.

**Scope**:
1. Refactor `IAdminUserService` to add `ClaimsPrincipal caller` to: GetUserAsync, UpdateUserAsync, SoftDeleteUserAsync, SuspendUserAsync, ActivateUserAsync, ForcePasswordResetAsync, RevokeApiKeyAsync, GetSessionsAsync, RevokeSessionAsync, GetActivityAsync.
2. At the top of each, call `var schoolId = TenantScope.GetSchoolFilter(caller);` and after the Marten LoadAsync<AdminUser>(id), reject with 404 (NOT 403 — a 403 leaks existence) when `schoolId is not null && user.School != schoolId`.
3. Plumb ctx.User through every endpoint handler in AdminUserEndpoints.cs.
4. Add a regression test in `src/api/Cena.Admin.Api.Tests/AdminUserServiceTenantScopingTests.cs`.

**Definition of Done**:
- [ ] All 9 per-id methods take a ClaimsPrincipal and apply TenantScope.GetSchoolFilter
- [ ] No method on AdminUserService loads an AdminUser by id without a tenant check (`grep -n 'LoadAsync<AdminUser>' src/api/Cena.Admin.Api/AdminUserService.cs` shows every line guarded)
- [ ] AdminUserEndpoints.cs passes ctx.User to every per-id call
- [ ] New AdminUserServiceTenantScopingTests.cs covers ADMIN/ADMIN cross-school (denied), ADMIN/ADMIN same-school (allowed), SUPER_ADMIN/* (allowed) for all 9 methods
- [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
- [ ] Branch: `<worker>/<task-id>-sec-008-admin-user-tenant-scope`
- [ ] Push branch; do not merge

**Files to read first**:
- src/api/Cena.Admin.Api/AdminUserService.cs (especially ListUsersAsync at 380 — reference pattern)
- src/api/Cena.Admin.Api/AdminUserEndpoints.cs
- src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs
- src/shared/Cena.Infrastructure/Auth/ResourceOwnershipGuard.cs

**Reporting**: complete with `--result` describing the 9 method changes, test count delta, and any callers in non-test code that had to change.


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_074e96ac9059`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
