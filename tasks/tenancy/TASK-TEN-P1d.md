# TASK-TEN-P1d: Platform Seed Data (5 Canonical Programs)

**Phase**: 1
**Priority**: high
**Effort**: 1--2d
**Depends on**: TEN-P1a, TEN-P1b, TEN-P1c
**Blocks**: TEN-P1e
**Queue ID**: `t_d497a446f333`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Seed the Cena Platform institute, its 5 canonical curriculum tracks, 5 programs, and 5 self-paced classrooms. This data is the foundation for every self-learner enrollment and the reference point for third-party institutes that fork or reference platform content.

## Background

ADR-0001 (session 2) commits to a "Cena Platform" institute of type `Platform` that ships 5 math programs on day 1: Bagrut 3/4/5 unit, SAT Math, and Psychometry Quantitative. Each program has exactly one `SelfPaced` classroom with `AutoApprove` join mode. All seed data is idempotent (two runs = one row).

## Specification

### Seed IDs (deterministic, stable across environments)

| Entity | ID | Notes |
|---|---|---|
| Institute | `cena-platform` | Type: `Platform`, MentorIds: `[]`, Country: `IL` |
| Track: Bagrut 3 | `track-math-bagrut-3unit` | Code: `MATH-BAGRUT-3UNIT` |
| Track: Bagrut 4 | `track-math-bagrut-4unit` | Code: `MATH-BAGRUT-4UNIT` |
| Track: Bagrut 5 | `track-math-bagrut-5unit` | Code: `MATH-BAGRUT-5UNIT` |
| Track: SAT | `track-math-sat-700` | Code: `MATH-SAT-700` |
| Track: Psychometry | `track-math-psychometry-quant` | Code: `MATH-PSYCHOMETRY-QUANTITATIVE` |
| Program: Bagrut 3 | `prog-math-bagrut-3unit` | Origin: `Platform`, TrackId: above |
| Program: Bagrut 4 | `prog-math-bagrut-4unit` | Origin: `Platform` |
| Program: Bagrut 5 | `prog-math-bagrut-5unit` | Origin: `Platform` |
| Program: SAT | `prog-math-sat-700` | Origin: `Platform` |
| Program: Psychometry | `prog-math-psychometry-quant` | Origin: `Platform` |
| Classroom: Bagrut 3 | `class-selfpaced-bagrut-3unit` | Mode: `SelfPaced`, JoinApproval: `AutoApprove` |
| Classroom: Bagrut 4 | `class-selfpaced-bagrut-4unit` | Mode: `SelfPaced` |
| Classroom: Bagrut 5 | `class-selfpaced-bagrut-5unit` | Mode: `SelfPaced` |
| Classroom: SAT | `class-selfpaced-sat-700` | Mode: `SelfPaced` |
| Classroom: Psychometry | `class-selfpaced-psychometry-quant` | Mode: `SelfPaced` |

### File 1: `PlatformInstituteSeedData.cs`

```csharp
// File: src/shared/Cena.Infrastructure/Seed/PlatformInstituteSeedData.cs
namespace Cena.Infrastructure.Seed;

public static class PlatformInstituteSeedData
{
    public const string PlatformInstituteId = "cena-platform";

    public static async Task SeedAsync(IDocumentStore store, ILogger logger)
    {
        // Upsert InstituteDocument with Id = PlatformInstituteId
        // Idempotent: check existence first via session.LoadAsync
    }
}
```

### File 2: `PlatformProgramSeedData.cs`

```csharp
// File: src/shared/Cena.Infrastructure/Seed/PlatformProgramSeedData.cs
namespace Cena.Infrastructure.Seed;

public static class PlatformProgramSeedData
{
    public static async Task SeedAsync(IDocumentStore store, ILogger logger)
    {
        // 1. Upsert 5 CurriculumTrackDocuments
        // 2. Upsert 5 ProgramDocuments (Origin = Platform, ParentProgramId = null)
        // 3. Upsert 5 ClassroomDocuments with new tenancy fields:
        //    InstituteId, ProgramId, Mode = SelfPaced, MentorIds = [],
        //    JoinApprovalMode = AutoApprove, Status = Active
        //    Plus legacy fields: TeacherId = "", SchoolId = PlatformInstituteId
    }
}
```

### Wire into `DatabaseSeeder.SeedAllAsync`

Add calls after learning objectives and before simulated students:

```csharp
// 4d. Platform institute + canonical programs (TEN-P1d)
await PlatformInstituteSeedData.SeedAsync(store, logger);
await PlatformProgramSeedData.SeedAsync(store, logger);
```

### CurriculumTrack LearningObjectiveIds

Each track must reference real learning objective IDs from `LearningObjectiveSeedData`. Map existing LO IDs to tracks by subject+topic. If exact mappings are not available, use empty arrays and log a warning -- do NOT invent fake LO IDs.

## Implementation notes

- Follow the idempotent pattern from `ClassroomSeedData.cs`: load by ID first, skip if exists, insert if not.
- Use `session.Store()` for documents (not event-sourced) -- this is seed data, not user actions.
- The 5 self-paced classrooms need unique `JoinCode` values. Use deterministic codes like `BAGRUT3`, `BAGRUT4`, `BAGRUT5`, `SAT700`, `PSYCHO` (6-char max).
- `ContentPackVersion` on all platform programs: `"1.0.0"`.
- `CreatedByMentorId` on platform programs: `""` (no mentor -- platform-authored).
- Follow FIND-sec-005: seed data must be tenant-scoped correctly. All platform seed data has `InstituteId = "cena-platform"`.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Seed data must be genuinely idempotent -- running twice produces identical state. All IDs must be deterministic (no GUIDs). Follow FIND-data-005 naming convention.

## Tests required

**Test class**: `PlatformSeedDataTests` in `src/shared/Cena.Infrastructure.Tests/Seed/PlatformSeedDataTests.cs`

| Test method | Assertion |
|---|---|
| `SeedPlatformInstitute_CreatesDocument` | After `SeedAsync`, load `"cena-platform"`, assert `Type == Platform`, `Country == "IL"`. |
| `SeedPlatformInstitute_IsIdempotent` | Run `SeedAsync` twice, assert exactly 1 row with `Id == "cena-platform"`. |
| `SeedPlatformPrograms_Creates5Tracks` | After seed, query all `CurriculumTrackDocument`, assert count >= 5 with expected codes. |
| `SeedPlatformPrograms_Creates5Programs` | After seed, query all `ProgramDocument` where `InstituteId == "cena-platform"`, assert 5 programs. |
| `SeedPlatformPrograms_Creates5SelfPacedClassrooms` | After seed, query `ClassroomDocument` where `Mode == SelfPaced && InstituteId == "cena-platform"`, assert 5. |
| `SeedPlatformPrograms_IsIdempotent` | Run twice, assert exactly 5 programs (not 10). |
| `SeedPlatformPrograms_ClassroomHasJoinCode` | Each self-paced classroom has a non-empty `JoinCode`. |

## Definition of Done

- [ ] `PlatformInstituteSeedData.cs` created and idempotent
- [ ] `PlatformProgramSeedData.cs` created with 5 tracks + 5 programs + 5 classrooms
- [ ] `DatabaseSeeder.SeedAllAsync` calls both new seed methods
- [ ] All 7 seed tests pass
- [ ] Running seeds twice produces no duplicates (idempotent)
- [ ] All IDs are deterministic strings (no GUIDs)
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/shared/Cena.Infrastructure/Seed/ClassroomSeedData.cs` -- idempotent seed pattern
2. `src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs` -- wiring point
3. `src/shared/Cena.Infrastructure/Seed/LearningObjectiveSeedData.cs` -- LO IDs to reference
4. `docs/adr/0001-multi-institute-enrollment.md` -- canonical seed set

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Seed/PlatformInstituteSeedData.cs` | create | Platform institute seed |
| `src/shared/Cena.Infrastructure/Seed/PlatformProgramSeedData.cs` | create | 5 tracks + 5 programs + 5 classrooms |
| `src/shared/Cena.Infrastructure/Seed/DatabaseSeeder.cs` | modify | Add 2 new seed calls |
| `src/shared/Cena.Infrastructure.Tests/Seed/PlatformSeedDataTests.cs` | create | 7 idempotency + correctness tests |
