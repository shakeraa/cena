---
id: FIND-SEC-010
task_id: t_d4524ac529cd
severity: P0 — Critical
lens: sec
tags: [reverify, sec, security]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-sec-010: Privilege escalation via POST /api/admin/users/{id}/role (any ADMIN can mint SUPER_ADMIN)

## Summary

Privilege escalation via POST /api/admin/users/{id}/role (any ADMIN can mint SUPER_ADMIN)

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

# FIND-sec-010 (P0): Privilege escalation via /api/admin/users/{id}/role

**Severity**: P0 (any school admin can promote themselves or anyone else to SUPER_ADMIN)

**Files**:
- src/api/Cena.Admin.Api/AdminRoleEndpoints.cs (line 105-128)
- src/api/Cena.Admin.Api/AdminRoleService.cs (line 164-196)
- src/api/Cena.Admin.Api.Tests/ (new file: AdminRoleServicePrivilegeEscalationTests.cs)

**Goal**: prevent any caller without the SUPER_ADMIN role from issuing the SUPER_ADMIN role, and prevent any school admin from assigning roles to users outside their own school.

**Background**: `POST /api/admin/users/{id}/role` is currently `RequireAuthorization(CenaAuthPolicies.AdminOnly)` (which admits both ADMIN and SUPER_ADMIN). The handler calls `AdminRoleService.AssignRoleToUserAsync(userId, request)` with NO ClaimsPrincipal parameter and NO check on the caller's school against the target user's school. The only safety guard is "you cannot remove the last SUPER_ADMIN" — there is NO guard against creating a new SUPER_ADMIN. A school-A admin can: enumerate user IDs, POST `{role: "SUPER_ADMIN"}` to their own UID, then on the next ID-token refresh the actor host honours the new role. Full vertical privilege escalation reachable from any school-scoped admin account.

**Scope**:
1. Change endpoint policy from `AdminOnly` to `SuperAdminOnly` at `AdminRoleEndpoints.cs:128`. Verify no other call site assumed the looser policy.
2. Add `ClaimsPrincipal caller` to `IAdminRoleService.AssignRoleToUserAsync` and the implementation. Apply tenant + role-step checks:
   - same-school enforcement via `TenantScope.GetSchoolFilter(caller)`
   - SUPER_ADMIN-only gate on assigning SUPER_ADMIN
   - Reject if `callerRole == "ADMIN"` AND `newRole == CenaRole.ADMIN` AND target is in a different school
3. Emit a StudentRecordAccessLog row (category=privileged_action) on every successful role change so the FERPA audit endpoint can surface the change.
4. Add tests in `AdminRoleServicePrivilegeEscalationTests.cs`:
   - school-A admin assigning SUPER_ADMIN to themselves -> 403
   - school-A admin assigning ADMIN to a school-B user -> 404
   - SUPER_ADMIN assigning any role anywhere -> 200 (positive case)

**Definition of Done**:
- [ ] `RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)` is on the role-assignment endpoint
- [ ] AssignRoleToUserAsync rejects the three illegal cases above
- [ ] Tests in AdminRoleServicePrivilegeEscalationTests.cs pass
- [ ] StudentRecordAccessLog row is emitted on success (verified by test)
- [ ] `dotnet test src/api/Cena.Admin.Api.Tests` green
- [ ] Branch: `<worker>/<task-id>-sec-010-role-assignment-escalation`

**Files to read first**:
- src/api/Cena.Admin.Api/AdminRoleEndpoints.cs
- src/api/Cena.Admin.Api/AdminRoleService.cs
- src/shared/Cena.Infrastructure/Auth/CenaAuthPolicies.cs
- src/shared/Cena.Infrastructure/Tenancy/TenantScope.cs
- src/shared/Cena.Infrastructure/Compliance/StudentRecordAccessLog.cs


## Evidence & context

- Lens report: `docs/reviews/agent-sec-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d4524ac529cd`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
