# TASK-TEN-P3b: Mentor Dashboard Vue Pages

**Phase**: 3
**Priority**: normal
**Effort**: 5--8d
**Depends on**: Phase 2 (TEN-P2f)
**Blocks**: TEN-P3c, TEN-P3e
**Queue ID**: `t_5882bcd92306`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Build the complete mentor dashboard as a Vue 3 application within the Vuexy admin template. The dashboard covers institute CRUD, program management, classroom management, student roster, and analytics. This is the primary admin surface for mentors and institute owners.

## Background

ADR-0001 defines the mentor as the single role for any adult guiding students. The mentor dashboard is the UI surface where mentors manage their institutes, programs, classrooms, and students. It leverages the existing Vuexy admin template at `src/admin/full-version/` and shares the same Firebase Auth infrastructure.

## Specification

### Page inventory

| Route | Component | Purpose |
|---|---|---|
| `/mentor/institutes` | `InstituteListPage.vue` | List mentor's institutes with create button |
| `/mentor/institutes/:id` | `InstituteDetailPage.vue` | Institute settings (name, type, country) |
| `/mentor/institutes/:id/programs` | `ProgramListPage.vue` | List programs for institute |
| `/mentor/programs/:id` | `ProgramDetailPage.vue` | Program settings (title, track, content pack version) |
| `/mentor/programs/:id/classrooms` | `ClassroomListPage.vue` | List classrooms for program |
| `/mentor/classrooms/:id` | `ClassroomDetailPage.vue` | Classroom settings (mode, join approval, dates) |
| `/mentor/classrooms/:id/roster` | `ClassroomRosterPage.vue` | Student list with enrollment status |
| `/mentor/classrooms/:id/assignments` | `ClassroomAssignmentsPage.vue` | Assignment list with create button |
| `/mentor/classrooms/:id/analytics` | `ClassroomAnalyticsPage.vue` | Mastery heatmap, completion rates |
| `/mentor/students/:enrollmentId` | `StudentDetailPage.vue` | Individual student progress view |
| `/mentor/join-requests` | `JoinRequestsPage.vue` | Pending join requests queue |

### REST endpoints (mentor-side)

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/mentor/institutes` | List mentor's institutes |
| `POST` | `/api/mentor/institutes` | Create institute |
| `PUT` | `/api/mentor/institutes/:id` | Update institute |
| `GET` | `/api/mentor/programs?instituteId={id}` | List programs |
| `POST` | `/api/mentor/programs` | Create program |
| `PUT` | `/api/mentor/programs/:id` | Update program |
| `GET` | `/api/mentor/classrooms?programId={id}` | List classrooms |
| `POST` | `/api/mentor/classrooms` | Create classroom |
| `PUT` | `/api/mentor/classrooms/:id` | Update classroom |
| `GET` | `/api/mentor/classrooms/:id/roster` | List enrolled students |
| `GET` | `/api/mentor/join-requests?classroomId={id}` | Pending requests |
| `POST` | `/api/mentor/join-requests/:id/approve` | Approve join request |
| `POST` | `/api/mentor/join-requests/:id/reject` | Reject join request |
| `GET` | `/api/mentor/analytics/classroom/:id` | Classroom-level analytics |

### Vuexy integration

- Place all mentor pages under `src/admin/full-version/src/pages/mentor/`.
- Add a "Mentor" section to the admin sidebar navigation.
- Use existing Vuexy components: `VDataTable`, `VCard`, `VDialog`, `VForm`.
- Follow the existing admin page patterns in `src/admin/full-version/src/pages/`.

### Institute CRUD

- Create: name, type (dropdown: School, PrivateTutor, CramSchool, NGO), country (dropdown).
- Update: name, country (type is immutable after creation).
- Delete: soft-delete (archive) -- set status to `Archived` on all classrooms.

### Program management

- Create: title, description, select track (from available curriculum tracks), origin (Platform/Custom).
- Platform programs are read-only (mentors see them but cannot edit).
- Forked programs show their parent program reference.

### Classroom management

- Create: name, mode (dropdown), join approval mode, start/end dates, mentors.
- The join code is auto-generated on creation.
- Roster shows students with enrollment status, mastery progress bar, last active date.

## Implementation notes

- Follow the existing Vuexy admin page pattern: each page is a single-file component with a composable for data fetching.
- The mentor dashboard runs on the admin port (5174) with the existing Firebase Auth.
- Follow FIND-sec-005: all mentor API endpoints must verify the caller owns the institute via `TenantScope.GetInstituteFilter`.
- Follow FIND-data-007 CQRS pattern: create/update endpoints emit events, list/detail endpoints are queries.
- Pagination on all list endpoints: `{ items, page, pageSize, total }`.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-sec-005 tenant scoping -- a mentor must never see another mentor's institutes. Follow FIND-data-007 CQRS purity for all write operations.

## Tests required

**Test class**: `MentorDashboardEndpointTests` in `src/api/Cena.Admin.Api.Tests/MentorDashboardEndpointTests.cs`

| Test method | Assertion |
|---|---|
| `ListInstitutes_ReturnsOnlyOwned` | Mentor A sees their institutes, not Mentor B's. |
| `CreateInstitute_Returns201_WithId` | Valid create, assert 201 + `InstituteId` in response. |
| `CreateProgram_WithValidTrack_Returns201` | Program with existing track, assert 201. |
| `CreateClassroom_WithMode_SetsDefaults` | Create with `InstructorLed`, assert defaults correct. |
| `GetRoster_ReturnStudentsWithProgress` | Roster includes mastery progress for enrolled students. |
| `ApproveJoinRequest_CreatesEnrollment` | Approve pending request, assert enrollment created. |
| `RejectJoinRequest_SetsRejectedStatus` | Reject request, assert status + reason persisted. |
| `Analytics_ReturnsClassroomMetrics` | Assert mastery heatmap and completion data. |
| `CrossInstitute_Access_Returns403` | Mentor A tries to access Mentor B's institute, assert 403. |

**Test class**: `MentorDashboardComponentTests` (Vitest)

| Test method | Assertion |
|---|---|
| `InstituteListPage_RendersTable` | Mount with mock data, assert table rows match institute count. |
| `ClassroomRosterPage_ShowsProgressBars` | Mount with roster data, assert progress bars rendered. |
| `JoinRequestsPage_ApproveButton_EmitsEvent` | Click approve, assert API call emitted. |

## Definition of Done

- [ ] 11 Vue pages created under `src/admin/full-version/src/pages/mentor/`
- [ ] 14 REST endpoints implemented
- [ ] Sidebar navigation updated with Mentor section
- [ ] Institute CRUD with soft-delete
- [ ] Program management with platform read-only enforcement
- [ ] Classroom management with auto-generated join codes
- [ ] Roster with mastery progress
- [ ] Join request approval/rejection flow
- [ ] All 9 backend tests pass
- [ ] All 3 component tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- mentor dashboard spec
2. `src/admin/full-version/src/pages/` -- existing admin page patterns
3. `src/admin/full-version/src/navigation/` -- sidebar navigation config
4. `src/api/Cena.Admin.Api/` -- existing admin service patterns

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/admin/full-version/src/pages/mentor/InstituteListPage.vue` | create | Institute list |
| `src/admin/full-version/src/pages/mentor/InstituteDetailPage.vue` | create | Institute detail |
| `src/admin/full-version/src/pages/mentor/ProgramListPage.vue` | create | Program list |
| `src/admin/full-version/src/pages/mentor/ProgramDetailPage.vue` | create | Program detail |
| `src/admin/full-version/src/pages/mentor/ClassroomListPage.vue` | create | Classroom list |
| `src/admin/full-version/src/pages/mentor/ClassroomDetailPage.vue` | create | Classroom detail |
| `src/admin/full-version/src/pages/mentor/ClassroomRosterPage.vue` | create | Student roster |
| `src/admin/full-version/src/pages/mentor/ClassroomAssignmentsPage.vue` | create | Assignment list |
| `src/admin/full-version/src/pages/mentor/ClassroomAnalyticsPage.vue` | create | Analytics |
| `src/admin/full-version/src/pages/mentor/StudentDetailPage.vue` | create | Student detail |
| `src/admin/full-version/src/pages/mentor/JoinRequestsPage.vue` | create | Join requests queue |
| `src/api/Cena.Admin.Api/MentorDashboardService.cs` | create | All mentor REST endpoints |
| `src/admin/full-version/src/navigation/mentor.ts` | create | Sidebar nav config |
| `src/api/Cena.Admin.Api.Tests/MentorDashboardEndpointTests.cs` | create | 9 backend tests |
