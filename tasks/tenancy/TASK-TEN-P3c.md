# TASK-TEN-P3c: Instructor-Scoped View

**Phase**: 3
**Priority**: normal
**Effort**: 2--3d
**Depends on**: TEN-P3b
**Blocks**: nothing
**Queue ID**: `t_f7afcb20c570`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Create a read-only subset of the mentor dashboard for users with the "instructor" role within an institute. Instructors see only the classrooms they are assigned to, can view student progress, but cannot create institutes, programs, or manage other instructors.

## Background

ADR-0001 defines the Mentor as the single role, but within an institute, a mentor can delegate classroom-level access to instructors. An instructor is a mentor with a scoped `institutes[]` claim where their `role` is `"instructor"` rather than `"owner"`. The instructor view is a filtered projection of the full mentor dashboard (TEN-P3b).

## Specification

### Access control

| Resource | Owner | Instructor |
|---|---|---|
| Institute list | All owned institutes | Institutes where assigned |
| Institute settings | Full CRUD | Read-only |
| Program list | All programs in institute | Programs of assigned classrooms only |
| Program settings | Full CRUD | Read-only |
| Classroom list | All classrooms in institute | Only assigned classrooms |
| Classroom settings | Full CRUD | Read-only (except roster management) |
| Roster | Full access | Full access (within assigned classrooms) |
| Assignments | Create/manage | Create/manage (within assigned classrooms) |
| Analytics | Institute-wide | Classroom-scoped |
| Join requests | All classrooms | Assigned classrooms only |

### Implementation approach

The instructor view reuses the same Vue pages from TEN-P3b but with a `usePermissions` composable that gates UI elements:

```typescript
// src/admin/full-version/src/composables/usePermissions.ts
interface Permissions {
    canCreateInstitute: boolean;
    canEditInstitute: boolean;
    canCreateProgram: boolean;
    canEditProgram: boolean;
    canCreateClassroom: boolean;
    canEditClassroom: boolean;
    canManageRoster: boolean;
    canManageAssignments: boolean;
    canViewAnalytics: boolean;
    canApproveJoinRequests: boolean;
}
```

### Backend filtering

All mentor API endpoints must filter by the caller's `institutes[]` claim role:

- If `role == "owner"`: unrestricted within the institute.
- If `role == "instructor"`: filter classrooms by `MentorIds.Contains(callerId)`.

Add a helper to the mentor service:

```csharp
private async Task<IReadOnlyList<string>> GetAccessibleClassroomIds(
    ClaimsPrincipal user, string instituteId)
{
    var claim = GetInstituteRole(user, instituteId);
    if (claim.Role == "owner") return null; // unrestricted
    // instructor: return only classrooms where MentorIds contains user ID
}
```

### Route guards

The instructor cannot navigate to routes that require owner permissions. Vue Router guard checks `usePermissions` and redirects to a 403 page if unauthorized.

## Implementation notes

- Reuse TEN-P3b pages -- do NOT create separate instructor pages. Use `v-if="permissions.canEditInstitute"` to show/hide edit buttons.
- Follow FIND-sec-005: instructor-scoped filtering must happen server-side, not just client-side. The backend must enforce the scope even if the UI guard is bypassed.
- The `usePermissions` composable reads from the authenticated user's Firebase claims (parsed by `ClaimsTransformer` from TEN-P3a).
- An instructor can be assigned to classrooms across multiple programs within the same institute.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-sec-005 tenant scoping. Instructor scope enforcement must be server-side, not just UI-level. A motivated instructor bypassing the UI must still be blocked by the API.

## Tests required

**Test class**: `InstructorScopeTests` in `src/api/Cena.Admin.Api.Tests/InstructorScopeTests.cs`

| Test method | Assertion |
|---|---|
| `Instructor_SeesOnlyAssignedClassrooms` | Instructor assigned to classroom A, assert list returns only A. |
| `Instructor_CannotCreateInstitute` | Instructor calls `POST /api/mentor/institutes`, assert 403. |
| `Instructor_CannotEditProgram` | Instructor calls `PUT /api/mentor/programs/:id`, assert 403. |
| `Instructor_CanViewRoster_InAssignedClassroom` | Instructor gets roster for assigned classroom, assert 200. |
| `Instructor_CannotViewRoster_InUnassignedClassroom` | Instructor gets roster for unassigned classroom, assert 403. |
| `Instructor_CanCreateAssignment_InAssignedClassroom` | Instructor with `PushTasks` creates assignment, assert 201. |
| `Owner_SeesAllClassrooms` | Owner sees all classrooms in their institute (contrast with instructor). |

**Test class**: `InstructorPermissionsComposableTests` (Vitest)

| Test method | Assertion |
|---|---|
| `Owner_HasFullPermissions` | `usePermissions` with owner claim, assert all `true`. |
| `Instructor_HasLimitedPermissions` | `usePermissions` with instructor claim, assert `canCreateInstitute == false`. |

## Definition of Done

- [ ] `usePermissions` composable created
- [ ] Mentor dashboard pages conditionally show/hide edit UI based on permissions
- [ ] Backend API enforces instructor scope on all mentor endpoints
- [ ] `GetAccessibleClassroomIds` helper implemented
- [ ] Vue Router guard redirects unauthorized instructor routes
- [ ] All 7 backend tests pass
- [ ] All 2 component tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/admin/full-version/src/pages/mentor/` -- mentor dashboard pages (from TEN-P3b)
2. `src/api/Cena.Admin.Api/MentorDashboardService.cs` -- mentor endpoints (from TEN-P3b)
3. `src/shared/Cena.Infrastructure/Auth/InstituteRoleClaim.cs` -- claim type (from TEN-P3a)
4. `docs/adr/0001-multi-institute-enrollment.md` -- instructor role spec

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/admin/full-version/src/composables/usePermissions.ts` | create | Permission composable |
| `src/admin/full-version/src/pages/mentor/*.vue` | modify | Add `v-if` permission guards |
| `src/api/Cena.Admin.Api/MentorDashboardService.cs` | modify | Add instructor scope filtering |
| `src/admin/full-version/src/router/guards/instructorGuard.ts` | create | Route guard |
| `src/api/Cena.Admin.Api.Tests/InstructorScopeTests.cs` | create | 7 backend tests |
| `src/admin/full-version/src/composables/__tests__/usePermissions.spec.ts` | create | 2 composable tests |
