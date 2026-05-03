# TASK-TEN-P3e: Fork/Reference Platform Programs

**Phase**: 3
**Priority**: normal
**Effort**: 2--3d
**Depends on**: TEN-P3b
**Blocks**: nothing
**Queue ID**: `t_e4e50f990dc0`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Implement the fork/reference workflow for platform programs. Third-party institutes can reference a platform program (use as-is with automatic updates) or fork it (clone into their namespace for independent editing). Minor version bumps auto-push to references; major version bumps require a review gate.

## Background

ADR-0001 defines three program origins: `Platform` (authored by Cena), `Forked` (cloned from a platform program), and `Custom` (authored from scratch). The `ContentPackVersion` field on `ProgramDocument` drives the versioning. Reference institutes automatically receive minor updates but must review and accept major updates.

## Specification

### Program origin behavior

| Origin | Content editable | Receives updates | Version tracking |
|---|---|---|---|
| `Platform` | No (platform-authored) | Source of updates | `ContentPackVersion` increments |
| `Forked` | Yes (institute-owned) | No auto-updates | Own version, `ParentProgramId` preserved |
| `Custom` | Yes (institute-authored) | N/A | Own version |

### Reference relationship

A "reference" is NOT a separate `ProgramDocument`. It is a `ProgramDocument` with `Origin = Platform` and `ParentProgramId` pointing to the canonical platform program. The key difference: referenced programs are read-only copies whose `ContentPackVersion` auto-syncs on minor bumps.

Create `src/shared/Cena.Infrastructure/Documents/ProgramReferenceDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public enum ReferenceStatus { Active, ReviewPending, Accepted, Rejected }

public class ProgramReferenceDocument
{
    public string Id { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string InstituteId { get; set; } = "";
    public string PlatformProgramId { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string? PendingVersion { get; set; }
    public ReferenceStatus Status { get; set; } = ReferenceStatus.Active;
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
}
```

### Events

```csharp
public record ProgramReferenced_V1(
    string ReferenceId,
    string InstituteId,
    string PlatformProgramId,
    string Version,
    DateTimeOffset LinkedAt
) : IDelegatedEvent;

public record ProgramVersionBumped_V1(
    string ProgramId,
    string OldVersion,
    string NewVersion,
    bool IsMajor,
    DateTimeOffset BumpedAt
) : IDelegatedEvent;

public record ProgramReferenceUpdatePushed_V1(
    string ReferenceId,
    string NewVersion,
    bool IsMajor,
    DateTimeOffset PushedAt
) : IDelegatedEvent;

public record ProgramReferenceUpdateAccepted_V1(
    string ReferenceId,
    string AcceptedVersion,
    string AcceptedByMentorId,
    DateTimeOffset AcceptedAt
) : IDelegatedEvent;

public record ProgramReferenceUpdateRejected_V1(
    string ReferenceId,
    string RejectedVersion,
    string RejectedByMentorId,
    string? Reason,
    DateTimeOffset RejectedAt
) : IDelegatedEvent;
```

### Fork flow

1. Mentor selects "Fork" on a platform program from the mentor dashboard.
2. `ProgramForkedFromPlatform_V1` emits (already defined in TEN-P1c).
3. New `ProgramDocument` created with `Origin = Forked`, `ParentProgramId = platformProgramId`, `ContentPackVersion = "1.0.0"` (independent version).
4. All classrooms under the forked program are independent -- no auto-updates.

### Version bump flow

1. Platform admin bumps a program version: `ProgramVersionBumped_V1`.
2. System queries all `ProgramReferenceDocument` rows for the bumped program.
3. If `IsMajor == false`: auto-push `ProgramReferenceUpdatePushed_V1`, set `CurrentVersion = NewVersion`.
4. If `IsMajor == true`: push `ProgramReferenceUpdatePushed_V1` with `IsMajor = true`, set `PendingVersion = NewVersion`, `Status = ReviewPending`. Mentor sees a review gate in their dashboard.

### REST endpoints

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `POST` | `/api/mentor/programs/:id/fork` | Fork a platform program | Mentor (institute owner) |
| `POST` | `/api/mentor/programs/:id/reference` | Reference a platform program | Mentor (institute owner) |
| `GET` | `/api/mentor/program-updates` | List pending major version updates | Mentor |
| `POST` | `/api/mentor/program-updates/:id/accept` | Accept major version update | Mentor |
| `POST` | `/api/mentor/program-updates/:id/reject` | Reject major version update | Mentor |

### Semver parsing

`ContentPackVersion` follows semver-ish convention: `major.minor.patch`. Major = first number. Parse with a simple string split, not a full semver library.

## Implementation notes

- Follow FIND-data-007 CQRS pattern: fork/reference/accept/reject are commands that emit events. List/detail are queries.
- Follow FIND-sec-005: only institute owners can fork/reference. The platform program itself is read-only.
- Minor version auto-push happens asynchronously (background job or Marten async projection).
- Forked programs lose the reference relationship -- they are fully independent copies.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Version bumps must be tested with both minor and major scenarios. Follow FIND-data-005 event naming convention. Follow FIND-data-007 CQRS purity.

## Tests required

**Test class**: `ProgramForkReferenceTests` in `src/api/Cena.Admin.Api.Tests/ProgramForkReferenceTests.cs`

| Test method | Assertion |
|---|---|
| `ForkProgram_CreatesIndependentCopy` | Fork platform program, assert new program with `Origin == Forked`, `ParentProgramId` set. |
| `ForkProgram_DoesNotReceiveUpdates` | Bump platform version, assert forked program version unchanged. |
| `ReferenceProgram_CreatesLink` | Reference platform program, assert `ProgramReferenceDocument` created. |
| `MinorVersionBump_AutoPushesToReferences` | Bump minor version, assert all references auto-synced. |
| `MajorVersionBump_SetsReviewPending` | Bump major version, assert references have `Status == ReviewPending`. |
| `AcceptMajorUpdate_SyncsVersion` | Accept pending update, assert `CurrentVersion` matches new version. |
| `RejectMajorUpdate_KeepsOldVersion` | Reject pending update, assert `CurrentVersion` unchanged. |
| `ForkAfterReference_BreaksLink` | Reference then fork same program, assert reference removed. |

## Definition of Done

- [ ] `ProgramReferenceDocument` created
- [ ] 5 new events registered in MartenConfiguration
- [ ] Fork flow creates independent `ProgramDocument` copy
- [ ] Reference flow creates `ProgramReferenceDocument` link
- [ ] Minor version auto-push works
- [ ] Major version review gate works
- [ ] 5 REST endpoints implemented
- [ ] All 8 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- fork/reference spec
2. `src/shared/Cena.Infrastructure/Documents/ProgramDocument.cs` -- `Origin`, `ParentProgramId`
3. `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` -- `ProgramForkedFromPlatform_V1`
4. `src/api/Cena.Admin.Api/MentorDashboardService.cs` -- mentor endpoint patterns

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/ProgramReferenceDocument.cs` | create | Reference tracking document |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 5 versioning events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register document + events |
| `src/api/Cena.Admin.Api/ProgramVersionService.cs` | create | Fork/reference/version endpoints |
| `src/api/Cena.Admin.Api.Tests/ProgramForkReferenceTests.cs` | create | 8 tests |
