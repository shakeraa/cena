# TASK-TEN-P1e: Student Stream Upcaster + Snapshot Defaults

**Phase**: 1
**Priority**: high
**Effort**: 2--3d
**Depends on**: TEN-P1c, TEN-P1d
**Blocks**: TEN-P1f
**Queue ID**: `t_89d9c909b4cd`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Ensure every existing student event stream has at least one `EnrollmentCreated_V1` event by prepending a synthetic enrollment on first replay. Update `StudentProfileSnapshot` with `DefaultInstituteId` and `DefaultEnrollmentId` fields and the corresponding `Apply` handler. This is the backward-compatibility bridge that lets all existing students participate in the multi-institute model without data loss.

## Background

Before tenancy, students have no enrollment. ADR-0001 requires that every student stream starts with a synthetic `EnrollmentCreated_V1` binding them to the platform institute's default classroom. The upcaster must be idempotent: replaying twice must not produce two synthetic events. `StudentProfileSnapshot` gains two new fields and a first-wins `Apply(EnrollmentCreated_V1)` handler.

## Specification

### 1. Marten event upcaster

Create `src/actors/Cena.Actors/Events/Upcasters/EnrollmentUpcaster.cs`:

The upcaster must implement Marten's `IEventUpcaster` (or the appropriate Marten v8 upcasting interface). On first read of a student stream that lacks an `EnrollmentCreated_V1`:

- Generate a deterministic `EnrollmentId` from the `StudentId`: `$"enroll-default-{studentId}"`.
- Set `ClassroomId` to the best-match platform classroom based on the student's `Subjects[]` from their `OnboardingCompleted_V1` or `SessionStarted_V1` event. Default to `"class-selfpaced-bagrut-5unit"` if ambiguous.
- Set `EnrolledAt` to the timestamp of the student's first event in the stream.

**Idempotency contract**: if the stream already contains an `EnrollmentCreated_V1` event, the upcaster does nothing. Two replays produce exactly one synthetic event.

### 2. StudentProfileSnapshot additions

Add to `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs`:

```csharp
/// <summary>
/// Default institute ID for Phase 1 single-institute compatibility.
/// Set by the first EnrollmentCreated_V1 event (synthetic or real).
/// </summary>
public string? DefaultInstituteId { get; set; }

/// <summary>
/// Default enrollment ID for Phase 1 single-institute compatibility.
/// Set by the first EnrollmentCreated_V1 event (synthetic or real).
/// </summary>
public string? DefaultEnrollmentId { get; set; }
```

### 3. Apply handler

```csharp
public void Apply(EnrollmentCreated_V1 e)
{
    // First-wins: only set defaults if not already set
    DefaultEnrollmentId ??= e.EnrollmentId;
    // Derive InstituteId from the classroom's InstituteId via a lookup
    // or store it directly if available. For Phase 1, use "cena-platform".
    DefaultInstituteId ??= "cena-platform";
}
```

### 4. Subject-to-classroom mapping

Create a static helper `PlatformClassroomMapper` in `src/shared/Cena.Infrastructure/Tenancy/PlatformClassroomMapper.cs`:

```csharp
public static class PlatformClassroomMapper
{
    public static string MapSubjectsToClassroomId(string[] subjects)
    {
        // Returns the best-match platform classroom ID
        // Default: "class-selfpaced-bagrut-5unit"
    }
}
```

## Implementation notes

- Marten v8 upcasters run at read time, not write time. The synthetic event is injected into the stream projection, not persisted to the event store. This means no schema migration is needed -- the upcaster runs transparently on every stream load.
- The first-wins pattern (`??=`) matches the existing `SchoolId ??= e.SchoolId` pattern in `Apply(SessionStarted_V1)` at line 153 of `StudentProfileSnapshot.cs`.
- The deterministic enrollment ID (`enroll-default-{studentId}`) ensures that repeated replays of the same stream produce the same synthetic event with the same ID.
- Log the mapping decision at `Information` level: `"Student {StudentId} mapped to classroom {ClassroomId} (subjects: {Subjects})"`.
- Follow FIND-data-007 CQRS pattern: the upcaster modifies the projection read path, not the write path.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. The upcaster is the most migration-sensitive piece in Phase 1 -- it MUST be idempotent and MUST handle streams that already have real enrollments. Follow FIND-data-005 event naming convention.

## Tests required

**Test class**: `EnrollmentUpcasterTests` in `src/actors/Cena.Actors.Tests/Events/Upcasters/EnrollmentUpcasterTests.cs`

| Test method | Assertion |
|---|---|
| `StreamWithoutEnrollment_GetsSyntheticEvent` | Replay a stream with only `SessionStarted_V1`, verify `EnrollmentCreated_V1` appears in the projected events. |
| `StreamWithExistingEnrollment_NoSyntheticEvent` | Replay a stream that already has `EnrollmentCreated_V1`, verify exactly one enrollment event (no duplicate). |
| `TwoReplays_ProduceIdenticalState` | Replay the same stream twice, assert `DefaultEnrollmentId` and `DefaultInstituteId` are identical both times. |
| `SyntheticEnrollmentId_IsDeterministic` | Assert `EnrollmentId == $"enroll-default-{studentId}"`. |
| `SubjectMapping_MathDefault_BagrutFive` | Student with `Subjects = ["math"]`, assert classroom = `"class-selfpaced-bagrut-5unit"`. |
| `SubjectMapping_EmptySubjects_DefaultsToBagrutFive` | Student with empty `Subjects[]`, assert default classroom. |

**Test class**: `StudentProfileSnapshotEnrollmentTests` in `src/actors/Cena.Actors.Tests/Events/StudentProfileSnapshotEnrollmentTests.cs`

| Test method | Assertion |
|---|---|
| `Apply_EnrollmentCreated_SetsDefaults` | Apply `EnrollmentCreated_V1`, assert `DefaultEnrollmentId` and `DefaultInstituteId` are set. |
| `Apply_EnrollmentCreated_FirstWins` | Apply two `EnrollmentCreated_V1` events with different IDs, assert defaults match the FIRST. |
| `ExistingFields_UntouchedByEnrollmentApply` | Apply `EnrollmentCreated_V1`, assert `SchoolId`, `EloRating`, `ConceptMasteryMap` unchanged. |

## Definition of Done

- [ ] `EnrollmentUpcaster.cs` created and registered in `MartenConfiguration.RegisterUpcasters`
- [ ] `PlatformClassroomMapper.cs` created with subject-to-classroom mapping
- [ ] `StudentProfileSnapshot` has `DefaultInstituteId` and `DefaultEnrollmentId` fields
- [ ] `Apply(EnrollmentCreated_V1)` handler uses first-wins (`??=`) pattern
- [ ] All 9 tests pass
- [ ] Upcaster is idempotent (verified by `TwoReplays_ProduceIdenticalState`)
- [ ] Existing `StudentProfileSnapshot` tests still pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` -- current snapshot + Apply pattern
2. `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` -- `RegisterUpcasters` method
3. `src/actors/Cena.Actors/Events/LearnerEvents.cs` -- event record conventions
4. `src/shared/Cena.Infrastructure/Seed/PlatformProgramSeedData.cs` -- classroom IDs (from TEN-P1d)

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/actors/Cena.Actors/Events/Upcasters/EnrollmentUpcaster.cs` | create | Synthetic enrollment upcaster |
| `src/shared/Cena.Infrastructure/Tenancy/PlatformClassroomMapper.cs` | create | Subject-to-classroom mapping |
| `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` | modify | Add 2 fields + Apply handler |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register upcaster |
| `src/actors/Cena.Actors.Tests/Events/Upcasters/EnrollmentUpcasterTests.cs` | create | 6 upcaster tests |
| `src/actors/Cena.Actors.Tests/Events/StudentProfileSnapshotEnrollmentTests.cs` | create | 3 snapshot tests |
