// =============================================================================
// Cena Platform — Enrollment Events (TENANCY-P1c partial / P1e prerequisite)
// Domain events for student enrollment lifecycle.
//
// NOTE: This file currently contains only EnrollmentCreated_V1, which is the
// minimum needed for the P1e stream upcaster. Task P1c will add the remaining
// 7 enrollment event types (InstituteCreated, CurriculumTrackPublished, etc.)
// and register them all in MartenConfiguration.
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a student is enrolled in a curriculum track at an institute.
/// The P1e upcaster prepends a synthetic EnrollmentCreated_V1 for legacy
/// students who were onboarded before multi-institute tenancy existed.
/// </summary>
public record EnrollmentCreated_V1(
    string StudentId,
    string EnrollmentId,
    string InstituteId,
    string TrackId,
    string? ClassroomId,
    DateTimeOffset EnrolledAt
) : IDelegatedEvent;

/// <summary>
/// DATA-READY-001: Emitted when an admin changes a track's content readiness status.
/// Captures old/new status for audit trail. Quality gate check should be enforced
/// before allowing transition to Ready (QuestionsWithIrtCount >= MinQuestionsForReady).
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

/// <summary>
/// BAGRUT-ALIGN-001: Emitted when a question's Bagrut structural alignment
/// is set or updated by an admin or auto-tagging service.
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
