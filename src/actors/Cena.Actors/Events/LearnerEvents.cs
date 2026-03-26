// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Learner Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// All events are immutable C# records, append-only, versioned.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Emitted on every student answer attempt. Core event for BKT updates.
/// 18 fields including explicit Timestamp for deterministic event-sourced replay.
/// </summary>
public record ConceptAttempted_V1(
    string StudentId,
    string ConceptId,
    string SessionId,
    bool IsCorrect,
    int ResponseTimeMs,
    string QuestionId,
    string QuestionType,
    string MethodologyActive,
    string ErrorType,
    double PriorMastery,
    double PosteriorMastery,
    int HintCountUsed,
    bool WasSkipped,
    string AnswerHash,
    int BackspaceCount,
    int AnswerChangeCount,
    bool WasOffline,
    DateTimeOffset Timestamp
);

/// <summary>
/// Emitted when a concept crosses the mastery threshold (default 0.85).
/// Includes InitialHalfLifeHours for HLR-based spaced repetition scheduling.
/// </summary>
public record ConceptMastered_V1(
    string StudentId,
    string ConceptId,
    string SessionId,
    double MasteryLevel,
    int TotalAttempts,
    int TotalSessions,
    string MethodologyAtMastery,
    double InitialHalfLifeHours,
    DateTimeOffset Timestamp
);

/// <summary>
/// Emitted when predicted recall drops below threshold via HLR decay check.
/// </summary>
public record MasteryDecayed_V1(
    string StudentId,
    string ConceptId,
    double PredictedRecall,
    double HalfLifeHours,
    double HoursSinceLastReview
);

/// <summary>
/// Emitted when the active pedagogy methodology changes for a concept.
/// </summary>
public record MethodologySwitched_V1(
    string StudentId,
    string ConceptId,
    string PreviousMethodology,
    string NewMethodology,
    string Trigger,
    double StagnationScore,
    string DominantErrorType,
    double McmConfidence
);

/// <summary>
/// Emitted when the stagnation detector identifies a learning plateau.
/// Composite score aggregates multiple plateau signals.
/// </summary>
public record StagnationDetected_V1(
    string StudentId,
    string ConceptId,
    double CompositeScore,
    double AccuracyPlateau,
    double ResponseTimeDrift,
    double SessionAbandonment,
    double ErrorRepetition,
    double AnnotationSentiment,
    int ConsecutiveStagnantSessions
);

/// <summary>
/// Emitted when a student adds a text annotation (note, question, insight, confusion).
/// Content stored as hash for privacy; sentiment from NLP pipeline.
/// </summary>
public record AnnotationAdded_V1(
    string StudentId,
    string ConceptId,
    string AnnotationId,
    string ContentHash,
    double SentimentScore,
    string AnnotationType
);

/// <summary>
/// Emitted when a cognitive load cooldown period completes.
/// The student was paused due to fatigue detection.
/// </summary>
public record CognitiveLoadCooldownComplete_V1(
    string StudentId,
    string SessionId,
    double FatigueScoreAtEnd,
    int MinutesCooldown,
    int QuestionsCompleted
);
