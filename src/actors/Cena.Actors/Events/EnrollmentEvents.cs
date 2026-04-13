// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Enrollment Context Domain Events (TENANCY-P1c)
// Layer: Domain Events | Runtime: .NET 9
// Eight immutable events covering institute, curriculum track, program,
// classroom, and enrollment lifecycles.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

// ── Institute lifecycle ──

public record InstituteCreated_V1(
    string InstituteId,
    string Type,
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
    string Origin,
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
    string Mode,
    string[] MentorIds,
    string JoinApprovalMode
) : IDelegatedEvent;

public record ClassroomStatusChanged_V1(
    string ClassroomId,
    string NewStatus,
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
    string NewStatus,
    DateTimeOffset ChangedAt,
    string? Reason
) : IDelegatedEvent;
