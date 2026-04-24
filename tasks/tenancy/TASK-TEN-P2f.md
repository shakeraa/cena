# TASK-TEN-P2f: Enrollment Switcher UI

**Phase**: 2
**Priority**: high
**Effort**: 2--3d
**Depends on**: TEN-P2a, TEN-P2e
**Blocks**: Phase 3 (TEN-P3a through TEN-P3f)
**Queue ID**: `t_30fdeb58211e`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Add a top-bar enrollment switcher dropdown that lets students switch between their active enrollments. All pages scope data to the selected enrollment. Leaderboards NEVER cross enrollments. Add the `/api/me/enrollments` endpoint.

## Background

Once a student holds multiple enrollments (bagrut + SAT, for example), every page must know WHICH enrollment is active. The enrollment switcher is the UI affordance that controls this. It lives in the global app bar and drives a Pinia store that all pages consume.

## Specification

### REST endpoint

**`GET /api/me/enrollments`** -- returns all active enrollments for the authenticated student.

Response shape:

```typescript
interface EnrollmentSummary {
    enrollmentId: string;
    classroomId: string;
    programTitle: string;
    trackCode: string;
    trackTitle: string;
    instituteName: string;
    mode: 'SelfPaced' | 'InstructorLed' | 'PersonalMentorship';
    status: 'Active' | 'Paused' | 'Withdrawn' | 'Completed';
    enrolledAt: string;       // ISO 8601
    mentorName?: string;      // for PersonalMentorship
}
```

Backend joins `EnrollmentDocument` + `ClassroomDocument` + `ProgramDocument` + `CurriculumTrackDocument` + `InstituteDocument` to build this response. Only `Active` and `Paused` enrollments are returned (not `Withdrawn` or `Completed`).

### Pinia store

Create `src/student/full-version/src/stores/enrollment.ts`:

```typescript
interface EnrollmentState {
    enrollments: EnrollmentSummary[];
    activeEnrollmentId: string | null;
    loading: boolean;
}
```

- On app boot, fetch `/api/me/enrollments`.
- `activeEnrollmentId` defaults to the first enrollment or the one stored in `localStorage`.
- Expose `switchEnrollment(enrollmentId: string)` action.
- Expose `activeEnrollment` computed (derived from `activeEnrollmentId`).

### Top-bar dropdown

Add an enrollment switcher component to `src/student/full-version/src/layouts/components/`:

- Shows the active enrollment's `trackTitle` + `instituteName`.
- Dropdown lists all active enrollments with program title and track code.
- Selecting an enrollment calls `switchEnrollment` and triggers a page refresh of data-dependent components.
- If only one enrollment, show it as static text (no dropdown).

### Page scoping

All data-fetching composables must include `enrollmentId` as a query parameter:

- `GET /api/me/plan/today?enrollmentId={id}`
- `GET /api/gamification/leaderboard?enrollmentId={id}`
- `GET /api/analytics/*?enrollmentId={id}`
- etc.

### Leaderboard isolation

Leaderboards MUST filter by `ClassroomId` derived from the active enrollment. A student's bagrut leaderboard must never show SAT students and vice versa. This is a hard requirement, not a nice-to-have.

## Implementation notes

- The enrollment switcher is a global UI element -- it persists across route changes.
- `localStorage` key: `cena:activeEnrollmentId`. Clear on logout.
- Follow FIND-sec-005: the `/api/me/enrollments` endpoint must only return enrollments owned by the authenticated student. Use `ResourceOwnershipGuard.VerifyStudentAccess`.
- Follow FIND-data-007 CQRS pattern: the endpoint is a read query, not a command.
- The join query for `EnrollmentSummary` must fit within the 5s statement timeout (`cena_student` role). If it cannot, build an async projection.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Leaderboard isolation is a privacy requirement -- leaking cross-enrollment data is a bug. Follow FIND-sec-005 tenant scoping. Follow FIND-ux-006b patterns for dropdown UX.

## Tests required

**Test class**: `EnrollmentEndpointTests` in `src/api/Cena.Student.Api.Tests/EnrollmentEndpointTests.cs`

| Test method | Assertion |
|---|---|
| `GetEnrollments_ReturnsActiveOnly` | Student with 3 enrollments (1 Active, 1 Paused, 1 Withdrawn), assert response has 2 items. |
| `GetEnrollments_IncludesProgramAndTrackDetails` | Assert response includes `programTitle`, `trackCode`, `trackTitle`. |
| `GetEnrollments_ReturnsOnlyOwnEnrollments` | Student A's request does not include Student B's enrollments. |
| `GetEnrollments_PersonalMentorship_IncludesMentorName` | Enrollment with mentor, assert `mentorName` is populated. |
| `Leaderboard_FilteredByClassroomId` | Request leaderboard with `enrollmentId`, assert only students from the same classroom appear. |
| `Leaderboard_NoCrossEnrollmentLeakage` | Student enrolled in 2 classrooms, assert leaderboard for classroom A does not include classroom B students. |

**Test class**: `EnrollmentSwitcherComponentTests` (Vitest)

| Test method | Assertion |
|---|---|
| `SingleEnrollment_ShowsStaticText` | With 1 enrollment, assert no dropdown rendered, text shows track title. |
| `MultipleEnrollments_ShowsDropdown` | With 2+ enrollments, assert dropdown rendered with all options. |
| `SwitchEnrollment_UpdatesStore` | Click different enrollment, assert `activeEnrollmentId` updated in store. |

## Definition of Done

- [ ] `GET /api/me/enrollments` endpoint implemented with join query
- [ ] Response includes program, track, and institute details
- [ ] Pinia `enrollment` store created with `switchEnrollment` action
- [ ] Top-bar dropdown component renders enrollment list
- [ ] `localStorage` persistence for `activeEnrollmentId`
- [ ] All data-fetching composables accept `enrollmentId` parameter
- [ ] Leaderboards filter by `ClassroomId` (no cross-enrollment)
- [ ] All 6 backend tests pass
- [ ] All 3 component tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- enrollment switcher spec
2. `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` -- enrollment shape
3. `src/student/full-version/src/layouts/components/` -- existing top-bar components
4. `src/student/full-version/src/stores/` -- existing Pinia stores

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/api/Cena.Student.Api.Host/Endpoints/EnrollmentEndpoints.cs` | create | `GET /api/me/enrollments` |
| `src/student/full-version/src/stores/enrollment.ts` | create | Pinia enrollment store |
| `src/student/full-version/src/layouts/components/EnrollmentSwitcher.vue` | create | Top-bar dropdown |
| `src/student/full-version/src/layouts/DefaultLayout.vue` | modify | Add EnrollmentSwitcher to app bar |
| `src/api/Cena.Student.Api.Tests/EnrollmentEndpointTests.cs` | create | 6 backend tests |
| `src/student/full-version/src/layouts/components/__tests__/EnrollmentSwitcher.spec.ts` | create | 3 component tests |
