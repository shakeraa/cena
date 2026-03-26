// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Pedagogy Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a learning session begins. Captures device context and experiment cohort.
/// </summary>
public record SessionStarted_V1(
    string StudentId,
    string SessionId,
    string DeviceType,
    string AppVersion,
    string Methodology,
    string? ExperimentCohort,
    bool IsOffline,
    DateTimeOffset ClientTimestamp
);

/// <summary>
/// Emitted when a session ends (completed, fatigue, abandoned, timeout, app_backgrounded).
/// </summary>
public record SessionEnded_V1(
    string StudentId,
    string SessionId,
    string EndReason,
    int DurationMinutes,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double AvgResponseTimeMs,
    double FatigueScoreAtEnd
);

/// <summary>
/// Emitted when an exercise/question is presented to the student.
/// </summary>
public record ExercisePresented_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    string QuestionType,
    string DifficultyLevel,
    string Methodology
);

/// <summary>
/// Emitted when a student requests a hint during an exercise.
/// HintLevel: 1=nudge, 2=scaffolded, 3=near-answer.
/// </summary>
public record HintRequested_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    int HintLevel
);

/// <summary>
/// Emitted when a student skips a question without answering.
/// </summary>
public record QuestionSkipped_V1(
    string StudentId,
    string SessionId,
    string ConceptId,
    string QuestionId,
    int TimeSpentBeforeSkipMs
);
