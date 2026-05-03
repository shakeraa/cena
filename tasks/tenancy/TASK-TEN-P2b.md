# TASK-TEN-P2b: PersonalMentorship Classroom Mode

**Phase**: 2
**Priority**: normal
**Effort**: 2--3d
**Depends on**: Phase 1 (TEN-P1f)
**Blocks**: TEN-P2c, TEN-P2d
**Queue ID**: `t_6f5b0e4467b4`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Implement the `PersonalMentorship` classroom mode where a mentor attaches a guidance layer to a student's existing platform enrollment. This mode does not create new curriculum -- it adds tasks, notes, and progress visibility on top of the student's self-paced learning path.

## Background

ADR-0001 defines three classroom modes. `SelfPaced` and `InstructorLed` exist at the schema level after Phase 1. `PersonalMentorship` is a new delivery mode with `InviteOnly` default, 1-on-1 mentor-student relationships, and capability flags per enrollment. The mentor sees the student's progress, can push assignments, and can leave notes, but does not author curriculum content.

## Specification

### MentorCapability flags

Define in `src/shared/Cena.Infrastructure/Documents/MentorCapabilityFlags.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

[Flags]
public enum MentorCapability
{
    None          = 0,
    PushTasks     = 1 << 0,
    LeaveNotes    = 1 << 1,
    Chat          = 1 << 2,
    ViewProgress  = 1 << 3,
    SendReminders = 1 << 4
}
```

### Default bundles per mode

| Mode | Default capabilities |
|---|---|
| `SelfPaced` | `None` |
| `InstructorLed` | `PushTasks \| LeaveNotes \| Chat \| ViewProgress \| SendReminders` |
| `PersonalMentorship` | `PushTasks \| LeaveNotes \| ViewProgress` |

### EnrollmentDocument extension

Add to `EnrollmentDocument.cs`:

```csharp
/// <summary>Mentor capability flags for this enrollment. Default None.</summary>
public MentorCapability MentorCapabilities { get; set; } = MentorCapability.None;

/// <summary>Mentor ID if this enrollment has personal mentorship. Null otherwise.</summary>
public string? MentorId { get; set; }
```

### ClassroomJoinRequestDocument

Create `src/shared/Cena.Infrastructure/Documents/ClassroomJoinRequestDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public enum JoinRequestStatus { Pending, Approved, Rejected, Expired }

public class ClassroomJoinRequestDocument
{
    public string Id { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string? MentorId { get; set; }
    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedByMentorId { get; set; }
    public string? RejectionReason { get; set; }
}
```

### New events

Add to `EnrollmentEvents.cs`:

```csharp
public record MentorAttachedToEnrollment_V1(
    string EnrollmentId,
    string MentorId,
    MentorCapability Capabilities,
    DateTimeOffset AttachedAt
) : IDelegatedEvent;

public record ClassroomJoinRequested_V1(
    string RequestId,
    string ClassroomId,
    string StudentId,
    DateTimeOffset RequestedAt
) : IDelegatedEvent;

public record ClassroomJoinApproved_V1(
    string RequestId,
    string ClassroomId,
    string StudentId,
    string ApprovedByMentorId,
    DateTimeOffset ApprovedAt
) : IDelegatedEvent;

public record ClassroomJoinRejected_V1(
    string RequestId,
    string ClassroomId,
    string RejectedByMentorId,
    string Reason,
    DateTimeOffset RejectedAt
) : IDelegatedEvent;
```

### PersonalMentorship enrollment flow

1. Mentor creates a `PersonalMentorship` classroom (JoinApproval = `InviteOnly`).
2. Mentor sends invite to student (via invite link -- TEN-P3f, or manually).
3. Student accepts invite -- `ClassroomJoinRequested_V1` is emitted (even for InviteOnly, for audit trail).
4. System auto-approves (invite is pre-authorized) -- `ClassroomJoinApproved_V1` + `EnrollmentCreated_V1`.
5. `MentorAttachedToEnrollment_V1` fires, setting capability flags.

## Implementation notes

- `PersonalMentorship` classrooms default to `InviteOnly` join approval. Enforce this at creation time: if `Mode == PersonalMentorship && JoinApprovalMode != InviteOnly`, reject with a validation error.
- Mentor attaches to the student's EXISTING platform enrollment -- the student keeps their `SelfPaced` enrollment and gains a parallel `PersonalMentorship` enrollment in the mentor's classroom.
- Follow FIND-data-007 CQRS pattern: state changes through events, documents are projections.
- Follow FIND-sec-005: mentor can only view students in their own enrollments.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. The join request flow must handle race conditions (two mentors approving the same request). Follow FIND-data-005 event naming convention. Follow FIND-data-007 CQRS purity.

## Tests required

**Test class**: `PersonalMentorshipTests` in `src/actors/Cena.Actors.Tests/Enrollment/PersonalMentorshipTests.cs`

| Test method | Assertion |
|---|---|
| `PersonalMentorship_DefaultsToInviteOnly` | Create classroom with `Mode = PersonalMentorship`, assert `JoinApprovalMode == InviteOnly`. |
| `PersonalMentorship_RejectsNonInviteOnly` | Create with `Mode = PersonalMentorship, JoinApprovalMode = AutoApprove`, assert validation error. |
| `MentorAttached_SetsCapabilities` | After `MentorAttachedToEnrollment_V1`, assert enrollment has `PushTasks \| LeaveNotes \| ViewProgress`. |
| `JoinRequest_PendingThenApproved_CreatesEnrollment` | Full flow: request -> approve -> verify enrollment exists. |
| `JoinRequest_Rejected_NoEnrollment` | Request -> reject -> verify no enrollment created. |
| `JoinRequest_DuplicateApproval_Idempotent` | Approve same request twice, assert exactly one enrollment. |
| `MentorCapability_FlagsEnum_RoundTrips` | Store `MentorCapability.PushTasks \| MentorCapability.Chat`, load, assert flags preserved. |

## Definition of Done

- [ ] `MentorCapability` flags enum created
- [ ] `EnrollmentDocument` extended with `MentorCapabilities` and `MentorId`
- [ ] `ClassroomJoinRequestDocument` created with status lifecycle
- [ ] 4 new events registered in MartenConfiguration
- [ ] PersonalMentorship enforces InviteOnly at creation time
- [ ] All 7 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- PersonalMentorship mode spec
2. `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` -- from TEN-P1a
3. `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` -- from TEN-P1c
4. `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs` -- from TEN-P1b

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/MentorCapabilityFlags.cs` | create | `MentorCapability` flags enum |
| `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` | modify | Add `MentorCapabilities`, `MentorId` |
| `src/shared/Cena.Infrastructure/Documents/ClassroomJoinRequestDocument.cs` | create | Join request document |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 4 new events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register 4 new events |
| `src/actors/Cena.Actors.Tests/Enrollment/PersonalMentorshipTests.cs` | create | 7 tests |
