// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Learner Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// All events are immutable C# records, append-only, versioned.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Marker interface for domain events that child actors delegate to the parent StudentActor.
/// Enables compile-time exhaustive matching in DelegateEvent handlers.
/// </summary>
public interface IDelegatedEvent { }

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
    DateTimeOffset Timestamp,
    // Difficulty-aware tracing (added for stagnation root-cause analysis)
    float QuestionDifficulty = 0f,
    float DifficultyGap = 0f,           // question difficulty - prior mastery
    string? DifficultyFrame = null,     // Stretch/Challenge/Appropriate/Expected/Regression
    string? FocusState = null           // Strong/Stable/Declining/Degrading/Critical at time of attempt
) : IDelegatedEvent;

/// <summary>
/// V2 of ConceptAttempted — adds Duration field for time-on-task analytics.
/// Upcasted from V1 with Duration defaulting to TimeSpan.Zero when unknown.
///
/// DATA-009: Example of event schema evolution via upcasting.
/// Old V1 events in the store are transparently transformed to V2 on read.
/// </summary>
public record ConceptAttempted_V2(
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
    DateTimeOffset Timestamp,
    /// <summary>
    /// Wall-clock duration the student spent on this question.
    /// Defaults to TimeSpan.Zero for events upcasted from V1.
    /// </summary>
    TimeSpan Duration,
    float QuestionDifficulty = 0f,
    float DifficultyGap = 0f,
    string? DifficultyFrame = null,
    string? FocusState = null
) : IDelegatedEvent;

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
) : IDelegatedEvent;

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
/// ACT-028: Added Timestamp field for deterministic event-sourced replay.
/// Existing persisted events without this field deserialize with default (DateTimeOffset.MinValue).
public record MethodologySwitched_V1(
    string StudentId,
    string ConceptId,
    string PreviousMethodology,
    string NewMethodology,
    string Trigger,
    double StagnationScore,
    string DominantErrorType,
    double McmConfidence,
    DateTimeOffset Timestamp = default
) : IDelegatedEvent;

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
) : IDelegatedEvent;

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
) : IDelegatedEvent;

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

// =============================================================================
// HIERARCHICAL METHODOLOGY EVENTS
// =============================================================================

/// <summary>
/// Emitted when a methodology assignment at a hierarchy level crosses the confidence
/// threshold (N >= 30 for topic/concept, N >= 50 for subject). Signals to admin
/// that the level now has statistically meaningful data.
/// </summary>
public record MethodologyConfidenceReached_V1(
    string StudentId,
    string Level,       // "Subject", "Topic", "Concept"
    string LevelId,     // The subject/topic/concept ID
    string Methodology,
    float Confidence,
    int AttemptCount,
    float SuccessRate,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// Emitted when a methodology switch was recommended (by MCM or stagnation) but
/// deferred because the cooldown period is still active.
/// </summary>
public record MethodologySwitchDeferred_V1(
    string StudentId,
    string ConceptId,
    string RecommendedMethodology,
    string CurrentMethodology,
    string Reason,
    int CooldownSessionsRemaining,
    double CooldownHoursRemaining,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

/// <summary>
/// LCM-001: Emitted when account status changes (suspension, lock, freeze, deletion request).
/// Persisted in the student's Marten event stream for audit trail.
/// </summary>
public record AccountStatusChanged_V1(
    string StudentId,
    string NewStatus,
    string? Reason,
    string ChangedBy,
    DateTimeOffset Timestamp
);

/// <summary>
/// Emitted when a teacher/admin manually overrides the methodology at any
/// hierarchy level. Takes immediate effect, bypasses cooldown.
/// </summary>
public record TeacherMethodologyOverride_V1(
    string StudentId,
    string Level,       // "Subject", "Topic", "Concept"
    string LevelId,
    string FromMethodology,
    string ToMethodology,
    string TeacherId,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

// =============================================================================
// SESSION EVENTS (STB-01)
// =============================================================================

/// <summary>
/// Emitted when a student starts a new learning session.
/// </summary>
public record LearningSessionStarted_V1(
    string StudentId,
    string SessionId,
    string[] Subjects,
    string Mode,
    int DurationMinutes,
    DateTimeOffset StartedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when a student ends a learning session.
/// STB-01b: Wire this event to complete session lifecycle
/// </summary>
public record LearningSessionEnded_V1(
    string StudentId,
    string SessionId,
    DateTimeOffset EndedAt,
    int QuestionsAttempted,
    int QuestionsCorrect
) : IDelegatedEvent;
