# TASK-TEN-P2d: MentorNoteDocument + REST CRUD

**Phase**: 2
**Priority**: normal
**Effort**: 1--2d
**Depends on**: TEN-P2b
**Blocks**: nothing
**Queue ID**: `t_7f29b647f581`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Implement `MentorNoteDocument` for markdown notes that mentors can anchor to sessions or questions. Notes have two visibility levels: private-to-mentor and shared-with-student. Full REST CRUD for mentors, read-only access for students (shared notes only).

## Background

ADR-0001 defines `LeaveNotes` as a `MentorCapability` flag. Notes are lightweight markdown content anchored to a specific context (a session, a question, or an enrollment generally). Mentors use notes to record observations about student progress, leave encouragement, or flag areas of concern.

## Specification

### MentorNoteDocument

Create `src/shared/Cena.Infrastructure/Documents/MentorNoteDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public enum NoteVisibility { PrivateToMentor, SharedWithStudent }
public enum NoteAnchorType { Enrollment, Session, Question }

public class MentorNoteDocument
{
    public string Id { get; set; } = "";
    public string NoteId { get; set; } = "";
    public string EnrollmentId { get; set; } = "";
    public string MentorId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public NoteAnchorType AnchorType { get; set; } = NoteAnchorType.Enrollment;
    public string? AnchorId { get; set; }       // sessionId or questionId, null for enrollment-level
    public string ContentMarkdown { get; set; } = "";
    public NoteVisibility Visibility { get; set; } = NoteVisibility.PrivateToMentor;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

### Events

Add to `EnrollmentEvents.cs`:

```csharp
public record MentorNoteCreated_V1(
    string NoteId,
    string EnrollmentId,
    string MentorId,
    string StudentId,
    string AnchorType,      // "Enrollment" | "Session" | "Question"
    string? AnchorId,
    string Visibility,      // "PrivateToMentor" | "SharedWithStudent"
    DateTimeOffset CreatedAt
) : IDelegatedEvent;

public record MentorNoteUpdated_V1(
    string NoteId,
    string MentorId,
    string? NewVisibility,
    DateTimeOffset UpdatedAt
) : IDelegatedEvent;

public record MentorNoteDeleted_V1(
    string NoteId,
    string MentorId,
    DateTimeOffset DeletedAt
) : IDelegatedEvent;
```

### REST endpoints

**Mentor endpoints**:

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `POST` | `/api/mentor/notes` | Create note | Mentor with `LeaveNotes` capability |
| `GET` | `/api/mentor/notes?enrollmentId={id}` | List notes for enrollment | Mentor (own notes only) |
| `PUT` | `/api/mentor/notes/{id}` | Update note content/visibility | Mentor (own notes only) |
| `DELETE` | `/api/mentor/notes/{id}` | Delete note | Mentor (own notes only) |

**Student endpoints**:

| Method | Path | Purpose | Auth |
|---|---|---|---|
| `GET` | `/api/me/mentor-notes?enrollmentId={id}` | List shared notes for enrollment | Student (SharedWithStudent only) |

### Content constraints

- `ContentMarkdown` max length: 5000 characters.
- No HTML allowed -- markdown only. Sanitize on input.
- `AnchorId` is validated: if `AnchorType == Session`, the session must belong to the student. If `AnchorType == Question`, the question must exist.

## Implementation notes

- Gate note creation on `MentorCapability.LeaveNotes` flag on the enrollment. Return 403 if missing.
- Follow FIND-data-007 CQRS pattern: `POST /api/mentor/notes` appends `MentorNoteCreated_V1`, projection writes `MentorNoteDocument`.
- Follow FIND-sec-005: mentor can only see/modify their own notes. Student can only see notes marked `SharedWithStudent` for their own enrollments.
- Notes are soft-deletable via `MentorNoteDeleted_V1`. The document is marked but not physically removed (audit trail).
- Register `MentorNoteDocument` in MartenConfiguration.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-data-005 event naming convention. Follow FIND-data-007 CQRS purity. Follow FIND-sec-005 tenant scoping -- notes never leak across enrollments or between mentors.

## Tests required

**Test class**: `MentorNoteEndpointTests` in `src/api/Cena.Student.Api.Tests/MentorNoteEndpointTests.cs`

| Test method | Assertion |
|---|---|
| `CreateNote_ValidMentor_Returns201` | Mentor with `LeaveNotes` creates note, assert 201. |
| `CreateNote_NoLeaveNotesCapability_Returns403` | Mentor without `LeaveNotes`, assert 403. |
| `CreateNote_ExceedsMaxLength_Returns400` | ContentMarkdown > 5000 chars, assert 400. |
| `ListNotes_MentorSeesAllOwn` | Mentor sees both private and shared notes for their enrollment. |
| `ListNotes_StudentSeesSharedOnly` | Student sees only `SharedWithStudent` notes. |
| `UpdateNote_ChangeVisibility_Works` | Toggle `PrivateToMentor` -> `SharedWithStudent`, reload, assert updated. |
| `DeleteNote_SoftDeletes` | Delete note, query, assert not returned in list but event exists. |
| `Note_AnchoredToSession_ValidatesOwnership` | Create note anchored to another student's session, assert 400. |

## Definition of Done

- [ ] `MentorNoteDocument` created with full fields
- [ ] 3 new events registered in MartenConfiguration
- [ ] 4 mentor endpoints implemented
- [ ] 1 student endpoint implemented
- [ ] Capability gate on `LeaveNotes` enforced
- [ ] Content length validation (5000 char max)
- [ ] All 8 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- mentor note spec
2. `src/shared/Cena.Infrastructure/Documents/MentorCapabilityFlags.cs` -- `LeaveNotes` flag
3. `src/shared/Cena.Infrastructure/Documents/EnrollmentDocument.cs` -- enrollment reference
4. `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` -- event pattern

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/MentorNoteDocument.cs` | create | Note document + enums |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 3 note events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register document + events |
| `src/api/Cena.Admin.Api/MentorNoteService.cs` | create | Mentor CRUD endpoints |
| `src/api/Cena.Student.Api.Host/Endpoints/MentorNoteEndpoints.cs` | create | Student read-only endpoint |
| `src/api/Cena.Student.Api.Tests/MentorNoteEndpointTests.cs` | create | 8 tests |
