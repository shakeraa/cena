// =============================================================================
// Cena Platform — Enrollment Context Domain Events (TENANCY-P1c + P1e + DATA-READY-001 + BAGRUT-ALIGN-001)
// Eight enrollment events + TrackReadinessChanged + QuestionBagrutAlignmentSet
// =============================================================================

using Cena.Infrastructure.Documents;

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

// ── Track readiness (DATA-READY-001) ──

/// <summary>
/// Emitted when an admin changes a track's content readiness status.
/// </summary>
public record TrackReadinessChanged_V1(
    string TrackId,
    CurriculumTrackStatus OldStatus,
    CurriculumTrackStatus NewStatus,
    string ChangedBy,
    string? Reason,
    int QuestionsWithIrtCount,
    bool PassedQualityGate,
    DateTimeOffset ChangedAt
) : IDelegatedEvent;

// ── Bagrut alignment (BAGRUT-ALIGN-001) ──

/// <summary>
/// Emitted when a question's Bagrut structural alignment is set.
/// </summary>
public record QuestionBagrutAlignmentSet_V1(
    string QuestionId,
    string ExamCode,
    string Part,
    string? TypicalPosition,
    string TopicCluster,
    bool IsProofQuestion,
    int EstimatedMinutes,
    string SetBy,
    DateTimeOffset SetAt
) : IDelegatedEvent;
