# TASK-TEN-P1c: Eight New Event Types + MartenConfiguration Registration

**Phase**: 1
**Priority**: high
**Effort**: 1d
**Depends on**: TEN-P1a
**Blocks**: TEN-P1d, TEN-P1e
**Queue ID**: `t_c4865abd14d0`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Define the eight domain events that drive the multi-institute entity lifecycle and register them in MartenConfiguration with snake_case `_v1` naming. These events are append-only, immutable records implementing `IDelegatedEvent`.

## Background

ADR-0001 specifies 8 new events covering institute creation, curriculum track publishing, program creation/forking, classroom creation/status changes, and enrollment creation/status changes. All follow the existing event conventions in `LearnerEvents.cs` (immutable records, `IDelegatedEvent` marker, explicit `DateTimeOffset Timestamp`).

## Specification

Create `src/actors/Cena.Actors/Events/EnrollmentEvents.cs`:

```csharp
namespace Cena.Actors.Events;

// ── Institute lifecycle ──

public record InstituteCreated_V1(
    string InstituteId,
    string Type,        // "Platform" | "School" | "PrivateTutor" | "CramSchool" | "NGO"
    string Name,
    string Country,
    string MentorId,
    DateTimeOffset CreatedAt
) : IDelegatedEvent;

// ── Curriculum track ──

public record CurriculumTrackPublished_V1(
    string TrackId,
    string Code,
    string Title,
    string Subject,
    string? TargetExam,
    string[] LearningObjectiveIds
) : IDelegatedEvent;

// ── Program lifecycle ──

public record ProgramCreated_V1(
    string ProgramId,
    string InstituteId,
    string TrackId,
    string Title,
    string Origin,          // "Platform" | "Forked" | "Custom"
    string? ParentProgramId,
    string ContentPackVersion,
    string CreatedByMentorId
) : IDelegatedEvent;

public record ProgramForkedFromPlatform_V1(
    string NewProgramId,
    string ParentProgramId,
    string InstituteId,
    string ForkedByMentorId
) : IDelegatedEvent;

// ── Classroom lifecycle ──

public record ClassroomCreated_V1(
    string ClassroomId,
    string InstituteId,
    string ProgramId,
    string Mode,            // "SelfPaced" | "InstructorLed" | "PersonalMentorship"
    string[] MentorIds,
    string JoinApprovalMode // "AutoApprove" | "ManualApprove" | "InviteOnly"
) : IDelegatedEvent;

public record ClassroomStatusChanged_V1(
    string ClassroomId,
    string NewStatus,       // "Active" | "Archived" | "Completed"
    DateTimeOffset ChangedAt,
    string? Reason
) : IDelegatedEvent;

// ── Enrollment lifecycle ──

public record EnrollmentCreated_V1(
    string EnrollmentId,
    string StudentId,
    string ClassroomId,
    DateTimeOffset EnrolledAt
) : IDelegatedEvent;

public record EnrollmentStatusChanged_V1(
    string EnrollmentId,
    string NewStatus,       // "Active" | "Paused" | "Withdrawn" | "Completed"
    DateTimeOffset ChangedAt,
    string? Reason
) : IDelegatedEvent;
```

### MartenConfiguration registration

Add a new `RegisterEnrollmentEvents` method in `MartenConfiguration.cs`:

```csharp
private static void RegisterEnrollmentEvents(StoreOptions opts)
{
    opts.Events.MapEventType<InstituteCreated_V1>("institute_created_v1");
    opts.Events.MapEventType<CurriculumTrackPublished_V1>("curriculum_track_published_v1");
    opts.Events.MapEventType<ProgramCreated_V1>("program_created_v1");
    opts.Events.MapEventType<ProgramForkedFromPlatform_V1>("program_forked_from_platform_v1");
    opts.Events.MapEventType<ClassroomCreated_V1>("classroom_created_v1");
    opts.Events.MapEventType<ClassroomStatusChanged_V1>("classroom_status_changed_v1");
    opts.Events.MapEventType<EnrollmentCreated_V1>("enrollment_created_v1");
    opts.Events.MapEventType<EnrollmentStatusChanged_V1>("enrollment_status_changed_v1");
}
```

Call `RegisterEnrollmentEvents(opts);` from `ConfigureCommon` alongside the existing `RegisterLearnerEvents`, etc.

## Implementation notes

- Follow the exact naming convention from FIND-data-005: `snake_case_v1` for event type names in the Marten registration.
- All event records are immutable C# records with positional constructors (same pattern as `ConceptAttempted_V1` in `LearnerEvents.cs`).
- Enum-like fields (`Type`, `Origin`, `Mode`, `NewStatus`) are `string` in events, not enum types. Events are serialized to the event store and must remain forward-compatible. Enum types live on documents only.
- `IDelegatedEvent` marker enables compile-time matching in `DelegateEvent` handlers.
- Do NOT add projections or Apply handlers in this task -- that is TEN-P1e.

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. Follow FIND-data-005 event naming convention strictly. Follow FIND-data-007 CQRS pattern -- events are the write model, documents are the read model. Every event must be independently serializable/deserializable via STJ.

## Tests required

**Test class**: `EnrollmentEventRegistrationTests` in `src/actors/Cena.Actors.Tests/Events/EnrollmentEventRegistrationTests.cs`

| Test method | Assertion |
|---|---|
| `AllEightEvents_AreRegistered_InMarten` | Build a `DocumentStore` with `ConfigureCenaEventStore`, assert all 8 event type names resolve via `opts.Events.EventMappingFor<T>()`. |
| `InstituteCreated_V1_Serializes_RoundTrip` | Create, serialize to JSON via STJ, deserialize, assert all fields match. |
| `CurriculumTrackPublished_V1_Serializes_RoundTrip` | Same round-trip test. |
| `ProgramCreated_V1_Serializes_RoundTrip` | Same round-trip test. |
| `ProgramForkedFromPlatform_V1_Serializes_RoundTrip` | Same round-trip test. |
| `ClassroomCreated_V1_Serializes_RoundTrip` | Same round-trip test. |
| `ClassroomStatusChanged_V1_NullReason_Serializes` | Verify `Reason = null` round-trips cleanly. |
| `EnrollmentCreated_V1_Serializes_RoundTrip` | Same round-trip test. |
| `EnrollmentStatusChanged_V1_Serializes_RoundTrip` | Same round-trip test. |
| `EventNames_AreSnakeCaseV1` | Assert all 8 registered names match `^[a-z_]+_v1$` regex. |

## Definition of Done

- [ ] `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` created with 8 event records
- [ ] All 8 events implement `IDelegatedEvent`
- [ ] `RegisterEnrollmentEvents` method added to `MartenConfiguration.cs`
- [ ] `RegisterEnrollmentEvents` called from `ConfigureCommon`
- [ ] All 10 tests pass
- [ ] Event type names are snake_case_v1 (grep-verifiable: `grep "Events.MapEventType" MartenConfiguration.cs`)
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments

## Files to read first

1. `src/actors/Cena.Actors/Events/LearnerEvents.cs` -- event record conventions
2. `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` -- registration pattern
3. `docs/adr/0001-multi-institute-enrollment.md` -- event field definitions

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | create | 8 event records |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Add `RegisterEnrollmentEvents` method + call |
| `src/actors/Cena.Actors.Tests/Events/EnrollmentEventRegistrationTests.cs` | create | 10 registration + serialization tests |
