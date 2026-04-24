# TASK-TEN-P1a: Four New Marten Document Types

**Phase**: 1
**Priority**: high
**Effort**: 1--2d
**Depends on**: none
**Blocks**: TEN-P1b, TEN-P1c
**Queue ID**: `t_2efbdd5b49a4`
**Assignee**: unassigned
**Status**: ready

---

## Goal

Create the four foundational Marten document types that represent the multi-institute entity hierarchy: Institute, CurriculumTrack, Program, and Enrollment. These are pure schema -- no endpoints, no events, no behavior change.

## Background

ADR-0001 introduces a four-level hierarchy (Institute > CurriculumTrack > Program > Classroom > Enrollment). The current codebase has none of these entities. This task adds them as Marten documents following the same conventions as `QuestionDocument` (Id alias, public setters for STJ serialization, nullable fields for backward compatibility).

## Specification

### 1. `InstituteDocument`

```csharp
// File: src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs
namespace Cena.Infrastructure.Documents;

public enum InstituteType { Platform, School, PrivateTutor, CramSchool, NGO }

public class InstituteDocument
{
    public string Id { get; set; } = "";
    public string InstituteId { get; set; } = "";
    public string Name { get; set; } = "";
    public InstituteType Type { get; set; } = InstituteType.School;
    public string Country { get; set; } = "";
    public string[] MentorIds { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
}
```

### 2. `CurriculumTrackDocument`

```csharp
// File: src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs
namespace Cena.Infrastructure.Documents;

public class CurriculumTrackDocument
{
    public string Id { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Code { get; set; } = "";        // e.g. "MATH-BAGRUT-5UNIT"
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? TargetExam { get; set; }
    public string[] LearningObjectiveIds { get; set; } = Array.Empty<string>();
    public string[] StandardMappings { get; set; } = Array.Empty<string>();
}
```

### 3. `ProgramDocument`

```csharp
// File: src/shared/Cena.Infrastructure/Documents/ProgramDocument.cs
namespace Cena.Infrastructure.Documents;

public enum ProgramOrigin { Platform, Forked, Custom }

public class ProgramDocument
{
    public string Id { get; set; } = "";
    public string ProgramId { get; set; } = "";
    public string InstituteId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public ProgramOrigin Origin { get; set; } = ProgramOrigin.Custom;
    public string? ParentProgramId { get; set; }
    public string ContentPackVersion { get; set; } = "1.0.0";
    public string CreatedByMentorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
```

### 4. `EnrollmentDocument`

```csharp
// File: src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs
namespace Cena.Infrastructure.Documents;

public enum EnrollmentStatus { Active, Paused, Withdrawn, Completed }

public class EnrollmentDocument
{
    public string Id { get; set; } = "";
    public string EnrollmentId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
```

### Marten registration

Add all four document types to `MartenConfiguration.ConfigureCommon` with the Id alias convention:

```csharp
opts.Schema.For<InstituteDocument>().Identity(x => x.Id);
opts.Schema.For<CurriculumTrackDocument>().Identity(x => x.Id);
opts.Schema.For<ProgramDocument>().Identity(x => x.Id);
opts.Schema.For<EnrollmentDocument>().Identity(x => x.Id);
```

## Implementation notes

- Follow the `QuestionDocument` convention: `Id` as the Marten-managed identity, a separate `{Entity}Id` field for domain identity. Both always equal.
- Enums serialize as strings (already configured in `MartenConfiguration.ConfigureCommon` via `EnumStorage.AsString`).
- All string arrays use `Array.Empty<string>()` default (not `null`) for safe iteration.
- Do NOT add events in this task -- that is TEN-P1c.
- Do NOT modify `ClassroomDocument` -- that is TEN-P1b.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-data-005 event naming convention for enum values (PascalCase). Follow FIND-data-007 CQRS pattern -- these are read-model documents, not aggregates. Ensure all properties have explicit defaults so existing empty rows deserialize cleanly.

## Tests required

**Test class**: `DocumentSchemaTests` in `src/shared/Cena.Infrastructure.Tests/Documents/DocumentSchemaTests.cs`

| Test method | Assertion |
|---|---|
| `InstituteDocument_RoundTrip_Succeeds` | Store + load an `InstituteDocument` via Marten; all fields survive serialization. |
| `InstituteDocument_DefaultValues_AreValid` | `new InstituteDocument()` has non-null defaults for all non-nullable fields. |
| `CurriculumTrackDocument_RoundTrip_Succeeds` | Store + load; verify `LearningObjectiveIds` survives as `string[]`. |
| `ProgramDocument_RoundTrip_Succeeds` | Store + load; verify `Origin` enum serializes as string. |
| `ProgramDocument_NullableParentProgramId_Deserializes` | Store with `ParentProgramId = null`, load, assert null. |
| `EnrollmentDocument_RoundTrip_Succeeds` | Store + load; verify `Status` enum and `EndedAt` nullable. |
| `EnrollmentDocument_DefaultStatus_IsActive` | `new EnrollmentDocument().Status == EnrollmentStatus.Active`. |

## Definition of Done

- [ ] 4 new `.cs` files created under `src/shared/Cena.Infrastructure/Documents/`
- [ ] All 4 document types registered in `MartenConfiguration.ConfigureCommon`
- [ ] All enums (`InstituteType`, `ProgramOrigin`, `EnrollmentStatus`) defined in their respective document files
- [ ] All 7 round-trip and default-value tests pass
- [ ] `dotnet build` succeeds with zero warnings in `Cena.Infrastructure`
- [ ] Existing tests remain green (no regressions)
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs` -- Id alias convention
2. `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs` -- existing document pattern
3. `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` -- registration pattern
4. `docs/adr/0001-multi-institute-enrollment.md` -- field definitions

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs` | create | New document + `InstituteType` enum |
| `src/shared/Cena.Infrastructure/Documents/CurriculumTrackDocument.cs` | create | New document |
| `src/shared/Cena.Infrastructure/Documents/ProgramDocument.cs` | create | New document + `ProgramOrigin` enum |
| `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` | create | New document + `EnrollmentStatus` enum |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register 4 new document types |
| `src/shared/Cena.Infrastructure.Tests/Documents/DocumentSchemaTests.cs` | create | 7 round-trip tests |
