# TASK-TEN-P1b: Extend ClassroomDocument Additively

**Phase**: 1
**Priority**: high
**Effort**: 0.5d
**Depends on**: TEN-P1a
**Blocks**: TEN-P1d
**Queue ID**: `t_b67c64eb08fa`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Add multi-institute fields to the existing `ClassroomDocument` without breaking any existing data or tests. Every existing field stays untouched. New fields are nullable or have safe defaults so pre-existing rows deserialize cleanly.

## Background

`ClassroomDocument` today is a flat row with `TeacherId`, `SchoolId`, `JoinCode`, `Subjects[]`, etc. ADR-0001 adds an `InstituteId`, `ProgramId`, `Mode`, `MentorIds[]`, join approval, and lifecycle status. The existing `TeacherId` and `SchoolId` fields remain for backward compatibility -- they are not removed until Phase 3.

## Specification

Add the following fields to `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs`:

```csharp
// --- Multi-institute fields (TEN-P1b) ---

/// <summary>Institute this classroom belongs to. Null for pre-tenancy rows.</summary>
public string? InstituteId { get; set; }

/// <summary>Program this classroom delivers. Null for pre-tenancy rows.</summary>
public string? ProgramId { get; set; }

/// <summary>Classroom delivery mode. Default InstructorLed matches existing behavior.</summary>
public ClassroomMode Mode { get; set; } = ClassroomMode.InstructorLed;

/// <summary>Mentors assigned to this classroom. Empty for pre-tenancy rows.</summary>
public string[] MentorIds { get; set; } = Array.Empty<string>();

/// <summary>How students join this classroom.</summary>
public ClassroomJoinApproval JoinApprovalMode { get; set; } = ClassroomJoinApproval.AutoApprove;

/// <summary>Classroom lifecycle status.</summary>
public ClassroomStatus Status { get; set; } = ClassroomStatus.Active;

/// <summary>Scheduled start date for the classroom cohort. Null = no schedule.</summary>
public DateTimeOffset? StartDate { get; set; }

/// <summary>Scheduled end date for the classroom cohort. Null = no end date.</summary>
public DateTimeOffset? EndDate { get; set; }
```

### New enums (in the same file or a shared enums file)

```csharp
public enum ClassroomMode { SelfPaced, InstructorLed, PersonalMentorship }
public enum ClassroomJoinApproval { AutoApprove, ManualApprove, InviteOnly }
public enum ClassroomStatus { Active, Archived, Completed }
```

### Fields NOT changed

- `Id`, `ClassroomId`, `JoinCode`, `Name`, `TeacherId`, `TeacherName`, `Subjects`, `Grade`, `SchoolId`, `CreatedAt`, `IsActive` -- all unchanged.

## Implementation notes

- Place the three new enums inside `ClassroomDocument.cs` (same namespace `Cena.Infrastructure.Documents`). They will be consumed by events in TEN-P1c.
- `Mode` defaults to `InstructorLed` so existing classrooms keep current behavior without any migration.
- `JoinApprovalMode` defaults to `AutoApprove` -- existing join-code flow works unchanged.
- `Status` defaults to `Active`. The existing `IsActive` bool remains as-is; the new `Status` enum is the replacement but coexists during the transition.
- `MentorIds` defaults to empty, not null. Existing `TeacherId` is left in place -- do NOT rename or remove it.
- Follow FIND-sec-005: any new field that could affect tenant scoping (`InstituteId`) must be tested for null handling.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Existing `ClassroomDocument` rows with none of the new fields must deserialize without error. Follow FIND-data-007 CQRS pattern -- these are read-model fields, not aggregate state. Use FIND-data-005 event naming convention for enum values (PascalCase).

## Tests required

**Test class**: `ClassroomDocumentExtensionTests` in `src/shared/Cena.Infrastructure.Tests/Documents/ClassroomDocumentExtensionTests.cs`

| Test method | Assertion |
|---|---|
| `ExistingClassroom_DeserializesWithNewDefaults` | Serialize a `ClassroomDocument` with ONLY pre-TEN-P1b fields, deserialize, verify `Mode == InstructorLed`, `JoinApprovalMode == AutoApprove`, `Status == Active`, `InstituteId == null`, `MentorIds.Length == 0`. |
| `NewClassroom_WithAllFields_RoundTrips` | Set all new fields to non-default values, store + load, assert all preserved. |
| `ClassroomMode_SerializesAsString` | Store with `Mode = SelfPaced`, load, verify the raw JSON contains `"selfPaced"` (camelCase STJ). |
| `ExistingFields_Untouched_AfterExtension` | Create a classroom with `TeacherId`, `SchoolId`, `JoinCode`, `Subjects`, store + load, assert all original fields unchanged. |

## Definition of Done

- [ ] 8 new properties added to `ClassroomDocument`
- [ ] 3 new enums (`ClassroomMode`, `ClassroomJoinApproval`, `ClassroomStatus`) defined
- [ ] All new fields have safe defaults (nullable or enum default)
- [ ] All existing `ClassroomDocument` tests still pass
- [ ] 4 new extension tests pass
- [ ] `ClassroomSeedData.cs` unchanged (existing seed rows gain defaults automatically)
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs` -- current shape
2. `src/shared/Cena.Infrastructure/Seed/ClassroomSeedData.cs` -- existing seed data
3. `docs/adr/0001-multi-institute-enrollment.md` -- field definitions (session 2 section)

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs` | modify | Add 8 properties + 3 enums |
| `src/shared/Cena.Infrastructure.Tests/Documents/ClassroomDocumentExtensionTests.cs` | create | 4 backward-compat tests |
