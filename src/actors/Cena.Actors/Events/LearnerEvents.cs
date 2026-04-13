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

// ═══════════════════════════════════════════════════════════════════════════
// TENANCY-P2a: Enrollment-scoped mastery events
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// V3 of ConceptAttempted — adds EnrollmentId for per-track mastery keying.
/// Upcasted from V2 with EnrollmentId defaulting to "default" for legacy events.
/// </summary>
public record ConceptAttempted_V3(
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
    TimeSpan Duration,
    string EnrollmentId,
    float QuestionDifficulty = 0f,
    float DifficultyGap = 0f,
    string? DifficultyFrame = null,
    string? FocusState = null
) : IDelegatedEvent;

/// <summary>
/// V2 of ConceptMastered — adds EnrollmentId for per-track mastery keying.
/// Upcasted from V1 with EnrollmentId defaulting to "default" for legacy events.
/// </summary>
public record ConceptMastered_V2(
    string StudentId,
    string ConceptId,
    string SessionId,
    double MasteryLevel,
    int TotalAttempts,
    int TotalSessions,
    string MethodologyAtMastery,
    double InitialHalfLifeHours,
    DateTimeOffset Timestamp,
    string EnrollmentId
) : IDelegatedEvent;

/// <summary>
/// TENANCY-P2a: Emitted when mastery state seeps from a source enrollment
/// to a newly created target enrollment. One-time, auditable, never inflates.
/// </summary>
public record MasterySeepageApplied_V1(
    string StudentId,
    string SourceEnrollmentId,
    string TargetEnrollmentId,
    string ConceptId,
    double SeepageFactor,
    double SourcePKnown,
    double SeededPKnown,
    DateTimeOffset AppliedAt
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
// ONBOARDING EVENTS (STB-00)
// =============================================================================

/// <summary>
/// Emitted when a student completes the onboarding wizard.
/// Captures initial preferences and learning goals.
/// </summary>
public record OnboardingCompleted_V1(
    string StudentId,
    string Role,                              // 'student' | 'self-learner' | 'test-prep' | 'homeschool'
    string Locale,                            // 'en' | 'ar' | 'he'
    string[] Subjects,
    int DailyTimeGoalMinutes,
    DateTimeOffset CompletedAt
) : IDelegatedEvent;

/// <summary>
/// FIND-data-007b: emitted when a student updates their profile
/// (display name, bio, favorite subjects, visibility). Nullable fields
/// signal "do not change" so MeEndpoints.UpdateProfile can patch
/// selectively. The inline StudentProfileSnapshot projection applies
/// this through the event stream instead of racing the snapshot.
/// </summary>
/// <summary>
/// FIND-pedagogy-009 (enriched): emitted whenever a student answer triggers a
/// dual Elo rating update. Appended to the STUDENT event stream so the inline
/// StudentProfileSnapshot projection picks up the new rating on replay, and the
/// QuestionDocument write (non-event-sourced) rides on the same IDocumentSession
/// inside SaveChangesAsync — no separate LightweightSession, no race with the
/// caller's own transaction (the same CQRS lesson that FIND-data-007 taught).
///
/// Citations:
///   Elo, A. E. (1978). The Rating of Chessplayers, Past and Present. ISBN 0-668-04721-6.
///   Wilson, R. C. et al. (2019). The Eighty Five Percent Rule for optimal learning.
///   Nature Communications 10, 4646. DOI: 10.1038/s41467-019-12552-4 — target
///   expected success probability is ≈0.85 for noisy binary classifiers (the
///   exact threshold derived in the paper is 0.847).
/// </summary>
public record StudentEloRatingUpdated_V1(
    string StudentId,
    string QuestionId,
    double OldStudentElo,
    double NewStudentElo,
    double OldQuestionElo,
    double NewQuestionElo,
    bool IsCorrect,
    double ExpectedCorrectness,
    int StudentAttemptCountAfter,
    DateTimeOffset Timestamp
) : IDelegatedEvent;

public record ProfileUpdated_V1(
    string StudentId,
    string? DisplayName,
    string? Bio,
    string[]? Subjects,
    string? Visibility,                       // 'class-only' | 'friends' | 'public'
    DateTimeOffset UpdatedAt
) : IDelegatedEvent;

// =============================================================================
// AGE GATE & PARENTAL CONSENT EVENTS (FIND-privacy-001)
// =============================================================================

/// <summary>
/// FIND-privacy-001: Emitted when a student's age and consent status are
/// recorded during registration. Captures date of birth, computed age at
/// registration, consent tier, and parent/guardian email where applicable.
///
/// Consent tiers:
///   - "adult" (age >= 16): no parental consent needed (GDPR Art 8 default)
///   - "teen" (13-15): parental consent required (ICO/GDPR-K)
///   - "child" (&lt; 13): parental consent required (COPPA §312.5)
///
/// This event is appended to the student's event stream immediately after
/// account creation, before the onboarding wizard begins. The
/// StudentProfileSnapshot projection picks up the fields on replay.
/// </summary>
public record AgeAndConsentRecorded_V1(
    string StudentId,
    DateOnly DateOfBirth,
    int AgeAtRegistration,
    string ConsentTier,                // "adult" | "teen" | "child"
    string? ParentEmail,               // required when ConsentTier != "adult"
    bool ParentalConsentGiven,         // true if parent completed challenge; false = pending
    string? ParentalConsentToken,      // opaque token sent to parent for verification
    string ConsentStatus,              // "verified" | "pending_parent" | "not_required"
    DateTimeOffset RecordedAt
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

// =============================================================================
// RIGHT TO ERASURE EVENTS (GDPR Article 17 / CCPA)
// =============================================================================

/// <summary>
/// Represents a single erasure action taken against a specific data store.
/// Part of the ErasureManifest for audit trail purposes.
/// </summary>
public record ErasureManifestItem(
    string StoreName,              // e.g., "Marten.Events", "AgentDB.Vectors", "S3.BlobStorage"
    string ActionTaken,            // e.g., "deleted", "anonymized", "retained_legal_hold"
    int RowsAffected,              // Number of records/rows affected in this store
    string? Details = null         // Additional context (e.g., "3 events soft-deleted")
);

/// <summary>
/// Complete manifest of all erasure actions taken across all data stores.
/// Provides full audit trail for compliance verification.
/// </summary>
public record ErasureManifest(
    string RequestId,
    string StudentId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ErasureManifestItem> StoreActions,
    int TotalRowsAffected,
    bool IsComplete              // false if any store failed or requires manual review
);

/// <summary>
/// Emitted when a student or authorized parent requests data erasure under GDPR Article 17 or CCPA.
/// The erasure is scheduled for processing after a mandatory cooling-off period (30 days by default)
/// to allow for cancellation of the request.
///
/// This event is appended to the student's event stream for audit purposes.
/// </summary>
public record StudentErasureRequested_V1(
    string StudentId,
    Guid RequestId,
    DateTimeOffset RequestedAt,
    string RequestedBy,                // "student:self" | "parent:<email>" | "guardian:<id>"
    DateTimeOffset ScheduledProcessingAt // RequestedAt + 30 days cooling-off period
) : IDelegatedEvent;

/// <summary>
/// Emitted when the erasure process has been completed across all data stores.
/// Includes a complete manifest of actions taken per store for compliance audit.
///
/// This event is appended to the student's event stream for audit purposes.
/// </summary>
public record StudentErasureCompleted_V1(
    string StudentId,
    Guid RequestId,
    DateTimeOffset CompletedAt,
    ErasureManifest Manifest,          // Detailed record of all erasure actions per store
    int RowsAffected                   // Total rows affected across all stores (convenience field)
) : IDelegatedEvent;
