# TASK-TEN-P2e: Student Onboarding V2 (Enrollment-Aware)

**Phase**: 2
**Priority**: high
**Effort**: 3--4d
**Depends on**: Phase 1 (TEN-P1f)
**Blocks**: TEN-P2f
**Queue ID**: `t_fb7fe86b1d13`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Replace the current onboarding flow with an enrollment-aware V2 that asks students whether they have a class code, an invite link, or want to self-learn. Self-learners pick from the 5 canonical platform programs. The result is a real `EnrollmentCreated_V1` event instead of the synthetic one from the upcaster.

## Background

The current onboarding (`OnboardingCompleted_V1`) captures role, locale, subjects, and daily time goal but knows nothing about institutes, tracks, or enrollments. ADR-0001 adds a "pick your path" step that channels students into the multi-institute model from their first interaction.

## Specification

### New event

Add to `LearnerEvents.cs`:

```csharp
public record StudentEnrolled_V1(
    string StudentId,
    string EnrollmentId,
    string ClassroomId,
    string ProgramId,
    string TrackCode,
    string OnboardingPath,  // "class-code" | "invite-link" | "self-learner"
    DateTimeOffset EnrolledAt
) : IDelegatedEvent;
```

Register in MartenConfiguration as `student_enrolled_v1`.

### Vue onboarding flow

Modify the existing onboarding wizard in `src/student/full-version/src/pages/onboarding/`:

**Step 1 (new)**: "How are you joining Cena?"
- Option A: "I have a class code" -> text input for 6-char code
- Option B: "I have an invite link" -> auto-detected from URL params
- Option C: "I'm learning on my own" -> go to program catalog

**Step 2a (class code path)**: Validate code against `POST /api/classrooms/join`. On success, show classroom name + program + track. Confirm enrollment.

**Step 2b (invite link path)**: Decode invite JWT, show classroom name + mentor name. Confirm enrollment.

**Step 2c (self-learner path)**: Show catalog of 5 platform programs:
- Bagrut Math 3-unit
- Bagrut Math 4-unit
- Bagrut Math 5-unit
- SAT Math
- Psychometry Quantitative

Student picks one (or more). Each selection creates a separate enrollment.

**Step 3**: Existing onboarding fields (daily time goal, etc.) -- unchanged.

**Step 4**: `POST /api/me/onboarding` now includes `enrollments[]` array in the request body.

### API changes

Extend `POST /api/me/onboarding` request body:

```typescript
interface OnboardingRequest {
    role: string;
    locale: string;
    subjects: string[];       // derived from selected track(s)
    dailyTimeGoalMinutes: number;
    enrollments: {
        classroomId: string;
        onboardingPath: 'class-code' | 'invite-link' | 'self-learner';
    }[];
}
```

The backend handler emits `OnboardingCompleted_V1` (existing) + one `StudentEnrolled_V1` per enrollment entry. The `subjects` field is auto-populated from the selected track(s) -- the student no longer picks subjects manually when a track is selected.

### Subject read-only behavior

When a student selects a track-based enrollment, the `Subjects[]` field becomes read-only (derived from the track's `Subject` field). The onboarding UI disables the subject picker and shows the track's subject as pre-selected.

## Implementation notes

- The existing `POST /api/me/onboarding` handler in `MeEndpoints` must be extended, not replaced. Keep backward compatibility for the mobile app (which may not send `enrollments[]` yet).
- If `enrollments` array is empty or missing, fall back to the Phase 1 behavior (synthetic enrollment via upcaster).
- Follow FIND-data-007 CQRS pattern: each enrollment emits its own event.
- Follow FIND-sec-005: validate that the classroom exists and accepts the student's join mode before creating the enrollment.
- The class code validation must check `ClassroomDocument.JoinApprovalMode` -- if `ManualApprove`, create a `ClassroomJoinRequestDocument` instead of an enrollment.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. The onboarding flow must handle all three paths end-to-end. Follow FIND-data-005 event naming convention. Follow FIND-ux-006b rate limiting on class code validation to prevent brute-force.

## Tests required

**Test class**: `OnboardingV2Tests` in `src/api/Cena.Student.Api.Tests/OnboardingV2Tests.cs`

| Test method | Assertion |
|---|---|
| `Onboarding_WithClassCode_CreatesEnrollment` | Valid class code, assert `EnrollmentDocument` created with correct `ClassroomId`. |
| `Onboarding_WithInvalidClassCode_Returns400` | Invalid code, assert 400 with message. |
| `Onboarding_SelfLearner_CreatesPlatformEnrollment` | Select platform program, assert enrollment with `classroomId` matching platform self-paced classroom. |
| `Onboarding_SelfLearner_MultiplePrograms_MultipleEnrollments` | Select 2 programs, assert 2 enrollment events emitted. |
| `Onboarding_WithoutEnrollments_FallsBackToPhase1` | Empty `enrollments[]`, assert synthetic enrollment from upcaster still works. |
| `Onboarding_ManualApproveClassroom_CreatesJoinRequest` | Class code for `ManualApprove` classroom, assert `ClassroomJoinRequestDocument` created, no enrollment yet. |
| `Onboarding_SubjectsReadOnly_WhenTrackSelected` | After track-based enrollment, assert `Subjects` derived from track, not user-selected. |
| `StudentEnrolled_V1_EmittedWithCorrectFields` | Verify all fields of `StudentEnrolled_V1` match the onboarding input. |

## Definition of Done

- [ ] `StudentEnrolled_V1` event defined and registered
- [ ] `POST /api/me/onboarding` extended with `enrollments[]` array
- [ ] Vue onboarding wizard updated with 3-path flow
- [ ] Platform catalog picker shows 5 programs
- [ ] Class code validation checks `JoinApprovalMode`
- [ ] Subjects auto-derived from track when track selected
- [ ] Backward compatible (missing `enrollments[]` falls back)
- [ ] All 8 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- onboarding V2 spec
2. `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` -- current onboarding handler
3. `src/student/full-version/src/pages/onboarding/` -- existing Vue wizard
4. `src/shared/Cena.Infrastructure/Seed/PlatformProgramSeedData.cs` -- 5 platform programs

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/actors/Cena.Actors/Events/LearnerEvents.cs` | modify | Add `StudentEnrolled_V1` |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register event |
| `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` | modify | Extend onboarding handler |
| `src/student/full-version/src/pages/onboarding/OnboardingWizard.vue` | modify | 3-path flow |
| `src/student/full-version/src/pages/onboarding/ProgramCatalog.vue` | create | Platform catalog picker |
| `src/api/Cena.Student.Api.Tests/OnboardingV2Tests.cs` | create | 8 tests |
