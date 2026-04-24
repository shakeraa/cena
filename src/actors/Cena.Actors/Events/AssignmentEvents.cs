// =============================================================================
// Cena Platform — Assignment Events (TENANCY-P2c)
// =============================================================================

namespace Cena.Actors.Events;

public record AssignmentCreated_V1(
    string AssignmentId, string ClassroomId, string MentorId,
    string? StudentId, string Title, string[] QuestionIds,
    DateTimeOffset? DueAt, DateTimeOffset CreatedAt) : IDelegatedEvent;

public record AssignmentStarted_V1(
    string AssignmentId, string StudentId, DateTimeOffset StartedAt) : IDelegatedEvent;

public record AssignmentQuestionCompleted_V1(
    string AssignmentId, string StudentId, string QuestionId,
    bool IsCorrect, DateTimeOffset CompletedAt) : IDelegatedEvent;

public record AssignmentCompleted_V1(
    string AssignmentId, string StudentId, int QuestionsCorrect,
    int TotalQuestions, DateTimeOffset CompletedAt) : IDelegatedEvent;

public record AssignmentWithdrawn_V1(
    string AssignmentId, string WithdrawnBy, string? Reason,
    DateTimeOffset WithdrawnAt) : IDelegatedEvent;

public record AssignmentDueDateChanged_V1(
    string AssignmentId, DateTimeOffset? OldDueAt, DateTimeOffset? NewDueAt,
    string ChangedBy, DateTimeOffset ChangedAt) : IDelegatedEvent;
