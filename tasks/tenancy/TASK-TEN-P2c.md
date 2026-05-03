# TASK-TEN-P2c: AssignmentDocument Aggregate + Events + REST Endpoints

**Phase**: 2
**Priority**: normal
**Effort**: 3--4d
**Depends on**: TEN-P2b
**Blocks**: nothing
**Queue ID**: `t_b8530ac8af0d`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Implement the `AssignmentDocument` aggregate with its full event lifecycle and REST endpoints for both mentors (create, manage) and students (view, start, complete). Assignments are the core mechanism for the `PushTasks` mentor capability.

## Background

ADR-0001 deferred assignments to Phase 2. An assignment is a set of questions pushed by a mentor to a student within a specific enrollment. The mentor selects questions from the program's question bank, sets a due date, and tracks completion. The student sees assignments in their dashboard and completes them inline.

## Specification

### AssignmentDocument

Create `src/shared/Cena.Infrastructure/Documents/AssignmentDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public enum AssignmentStatus { Created, Started, Completed, Withdrawn }

public class AssignmentDocument
{
    public string Id { get; set; } = "";
    public string AssignmentId { get; set; } = "";
    public string EnrollmentId { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string CreatedByMentorId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string[] QuestionIds { get; set; } = Array.Empty<string>();
    public int TotalQuestions { get; set; }
    public int CompletedQuestions { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Created;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

### Events

Add to `EnrollmentEvents.cs`:

```csharp
public record AssignmentCreated_V1(
    string AssignmentId,
    string EnrollmentId,
    string ClassroomId,
    string StudentId,
    string CreatedByMentorId,
    string Title,
    string[] QuestionIds,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt
) : IDelegatedEvent;

public record AssignmentStarted_V1(
    string AssignmentId,
    string StudentId,
    DateTimeOffset StartedAt
) : IDelegatedEvent;

public record AssignmentQuestionCompleted_V1(
    string AssignmentId,
    string QuestionId,
    string StudentId,
    bool IsCorrect,
    DateTimeOffset CompletedAt
) : IDelegatedEvent;

public record AssignmentCompleted_V1(
    string AssignmentId,
    string StudentId,
    int TotalQuestions,
    int CorrectAnswers,
    DateTimeOffset CompletedAt
) : IDelegatedEvent;

public record AssignmentWithdrawn_V1(
    string AssignmentId,
    string WithdrawnByMentorId,
    string? Reason,
    DateTimeOffset WithdrawnAt
) : IDelegatedEvent;

public record AssignmentDueDateChanged_V1(
    string AssignmentId,
    DateTimeOffset? OldDueDate,
    DateTimeOffset? NewDueDate,
    string ChangedByMentorId,
    DateTimeOffset ChangedAt
) : IDelegatedEvent;
```

### REST endpoints

**Mentor endpoints** (in `Cena.Admin.Api.Host` or a new `Cena.Mentor.Api` project):

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `POST` | `/api/assignments` | Create assignment | Mentor with `PushTasks` capability |
| `GET` | `/api/assignments?classroomId={id}` | List assignments for classroom | Mentor |
| `PUT` | `/api/assignments/{id}/due-date` | Change due date | Mentor |
| `DELETE` | `/api/assignments/{id}` | Withdraw assignment | Mentor |

**Student endpoints** (in `Cena.Student.Api.Host`):

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `GET` | `/api/me/assignments` | List student's assignments | Student |
| `GET` | `/api/me/assignments/{id}` | Get assignment detail | Student (owner only) |
| `POST` | `/api/me/assignments/{id}/start` | Start assignment | Student (owner only) |

Assignment completion happens implicitly through the existing `ConceptAttempted_V1` flow when the student answers assignment questions within a session.

## Implementation notes

- Gate assignment creation on `MentorCapability.PushTasks` flag on the enrollment. If the mentor does not have this capability, return 403.
- Follow FIND-data-007 CQRS pattern: `POST /api/assignments` appends `AssignmentCreated_V1` to the student's stream, then a projection writes the `AssignmentDocument`.
- Follow FIND-sec-005: mentor can only create assignments for students in their classrooms. Student can only view their own assignments.
- `QuestionIds` must reference real questions in the program's question bank. Validate existence before creation.
- Register `AssignmentDocument` in MartenConfiguration.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-data-005 event naming convention. Follow FIND-data-007 CQRS purity. Follow FIND-sec-005 tenant scoping -- assignments scoped to enrollment, never cross-enrollment.

## Tests required

**Test class**: `AssignmentEndpointTests` in `src/api/Cena.Student.Api.Tests/AssignmentEndpointTests.cs`

| Test method | Assertion |
|---|---|
| `CreateAssignment_ValidMentor_Returns201` | Mentor with `PushTasks` creates assignment, assert 201 + `AssignmentId` in response. |
| `CreateAssignment_NoPushTasksCapability_Returns403` | Mentor without `PushTasks`, assert 403. |
| `CreateAssignment_InvalidQuestionIds_Returns400` | QuestionIds referencing non-existent questions, assert 400. |
| `ListStudentAssignments_ReturnsOwnOnly` | Student A sees their assignments, not Student B's. |
| `StartAssignment_SetsStartedStatus` | `POST .../start`, reload, assert `Status == Started`. |
| `WithdrawAssignment_SetsWithdrawnStatus` | Mentor withdraws, assert `Status == Withdrawn`. |
| `DueDateChange_UpdatesDocument` | Change due date, reload, assert new date matches. |
| `AssignmentCompleted_AllQuestionsAnswered` | Complete all questions, assert `Status == Completed`. |

## Definition of Done

- [ ] `AssignmentDocument` created with full lifecycle fields
- [ ] 6 new events added and registered in MartenConfiguration
- [ ] 4 mentor endpoints implemented
- [ ] 3 student endpoints implemented
- [ ] Capability gate on `PushTasks` enforced
- [ ] All 8 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- AssignmentDocument spec
2. `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` -- `MentorCapabilities` field
3. `src/shared/Cena.Infrastructure/Documents/MentorCapabilityFlags.cs` -- `PushTasks` flag
4. `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` -- existing enrollment events

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/AssignmentDocument.cs` | create | Assignment document + status enum |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 6 assignment events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register document + events |
| `src/api/Cena.Student.Api.Host/Endpoints/AssignmentEndpoints.cs` | create | Student assignment endpoints |
| `src/api/Cena.Admin.Api/AssignmentAdminService.cs` | create | Mentor assignment endpoints |
| `src/api/Cena.Student.Api.Tests/AssignmentEndpointTests.cs` | create | 8 tests |
