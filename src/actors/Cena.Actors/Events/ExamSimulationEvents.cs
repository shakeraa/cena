// =============================================================================
// Cena Platform — Exam Simulation Events (SEC-ASSESS-002)
// =============================================================================

namespace Cena.Actors.Events;

public record ExamSimulationStarted_V1(
    string StudentId,
    string SimulationId,
    string ExamCode,
    int TimeLimitMinutes,
    int PartACount,
    int PartBCount,
    int VariantSeed,
    DateTimeOffset StartedAt
) : IDelegatedEvent;

public record ExamSimulationSubmitted_V1(
    string StudentId,
    string SimulationId,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double ScorePercent,
    TimeSpan TimeTaken,
    int VisibilityWarnings,
    double ReadinessLowerBound,
    double ReadinessUpperBound,
    DateTimeOffset SubmittedAt
) : IDelegatedEvent;

public record ExamVisibilityWarning_V1(
    string StudentId,
    string SimulationId,
    string VisibilityState,
    TimeSpan DurationAway,
    DateTimeOffset DetectedAt
) : IDelegatedEvent;
