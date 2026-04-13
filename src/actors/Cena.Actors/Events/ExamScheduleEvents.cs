// =============================================================================
// Cena Platform — Exam Schedule Events (SEC-ASSESS-004)
// =============================================================================

namespace Cena.Actors.Events;

public record ExamPeriodConfigured_V1(
    string PeriodId,
    string InstituteId,
    string ExamCode,
    string ExamName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool DisableShowAnswer,
    bool StepSolverOnlyMode,
    string ConfiguredBy,
    DateTimeOffset ConfiguredAt
) : IDelegatedEvent;

public record SuspiciousUploadFlagged_V1(
    string StudentId,
    string InstituteId,
    string? ExamPeriodId,
    string UploadType,
    double SimilarityScore,
    string? MatchedExamPaperId,
    string Reason,
    DateTimeOffset FlaggedAt
) : IDelegatedEvent;
