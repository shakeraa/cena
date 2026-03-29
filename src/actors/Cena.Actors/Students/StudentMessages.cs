// =============================================================================
// Cena Platform -- Student Actor Message Types
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Cena.Actors.Mastery;

namespace Cena.Actors.Students;

// =============================================================================
// ENUMS
// =============================================================================

public enum Methodology
{
    Socratic,
    SpacedRepetition,
    Feynman,
    ProjectBased,
    BloomsProgression,
    WorkedExample,
    Analogy,
    RetrievalPractice,
    DrillAndPractice
}


public enum SessionEndReason
{
    Completed,
    Fatigue,
    Abandoned,
    Timeout,
    AppBackgrounded,
    StudentRequested
}

public enum QuestionType
{
    MultipleChoice,
    Numeric,
    Expression,
    TrueFalseJustification,
    Ordering,
    FillBlank,
    DiagramLabeling,
    FreeText
}

public enum AnnotationType
{
    Note,
    Question,
    Insight,
    Confusion
}

public enum OutreachChannel
{
    WhatsApp,
    Telegram,
    Push,
    Voice
}

// =============================================================================
// RESULT ENVELOPES
// =============================================================================

/// <summary>
/// Standard envelope for all actor command results. Follows the Result pattern
/// to avoid exceptions crossing actor boundaries.
/// </summary>
public sealed record ActorResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Typed result envelope with a payload. Used for query responses.
/// </summary>
public sealed record ActorResult<T>(
    bool Success,
    T? Data = default,
    string? ErrorCode = null,
    string? ErrorMessage = null);

// =============================================================================
// COMMANDS -- Student Actor
// =============================================================================

/// <summary>
/// Record a student's attempt at a concept exercise.
/// Triggers BKT update, stagnation signal update, XP award, and NATS publication.
/// This is the primary hot-path command.
/// </summary>
public sealed record AttemptConcept(
    [property: Required, StringLength(36, MinimumLength = 36)]
    string StudentId,

    [property: Required, StringLength(36, MinimumLength = 36)]
    string SessionId,

    [property: Required]
    string ConceptId,

    [property: Required]
    string QuestionId,

    QuestionType QuestionType,

    [property: Required]
    string Answer,

    [property: Range(0, 600_000)]
    int ResponseTimeMs,

    [property: Range(0, 3)]
    int HintCountUsed,

    bool WasSkipped,

    [property: Range(0, int.MaxValue)]
    int BackspaceCount,

    [property: Range(0, int.MaxValue)]
    int AnswerChangeCount,

    bool WasOffline)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Begin a new learning session. Creates a child LearningSessionActor.
/// </summary>
public sealed record StartSession(
    [property: Required, StringLength(36, MinimumLength = 36)]
    string StudentId,

    [property: Required]
    string SubjectId,

    string? ConceptId,

    [property: Required]
    string DeviceType,

    [property: Required]
    string AppVersion,

    DateTimeOffset ClientTimestamp,

    bool IsOffline,

    string? SchoolId = null) // REV-014: tenant context
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Response returned after StartSession is processed.
/// </summary>
public sealed record StartSessionResponse(
    string SessionId,
    string StartingConceptId,
    string StartingConceptName,
    Methodology ActiveMethodology,
    int CurrentXp,
    int StreakDays);

/// <summary>
/// End a learning session.
/// </summary>
public sealed record EndSession(
    [property: Required]
    string StudentId,

    [property: Required]
    string SessionId,

    SessionEndReason Reason)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Student-initiated methodology switch.
/// </summary>
public sealed record SwitchMethodology(
    [property: Required]
    string StudentId,

    [property: Required]
    string ConceptId,

    [property: Required]
    string StudentFriendlyLabel)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Add a free-text annotation to a concept.
/// </summary>
public sealed record AddAnnotation(
    [property: Required]
    string StudentId,

    [property: Required]
    string ConceptId,

    [property: Required]
    string SessionId,

    [property: Required, StringLength(5000)]
    string Text,

    AnnotationType Kind)
{
    public string CorrelationId { get; init; } = "";
}

// =============================================================================
// QUERIES
// =============================================================================

/// <summary>
/// Query the student's current mastery overlay. Returns in-memory state.
/// </summary>
public sealed record GetStudentProfile(
    [property: Required]
    string StudentId);

/// <summary>
/// Snapshot of the student actor's in-memory state.
/// </summary>
public sealed record StudentProfileDto(
    string StudentId,
    IReadOnlyDictionary<string, double> MasteryMap,
    IReadOnlyDictionary<string, string> ActiveMethodologyMap,
    int TotalXp,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset LastActivityDate,
    string? ExperimentCohort,
    int SessionCount);

/// <summary>
/// Get the spaced repetition review schedule for a student.
/// </summary>
public sealed record GetReviewSchedule(
    [property: Required]
    string StudentId,

    [property: Range(1, 50)]
    int MaxItems = 10);

/// <summary>
/// MST-017: Query the rich mastery overlay for API consumption.
/// Returns the full MasteryOverlay dictionary from the StudentActor.
/// </summary>
public sealed record GetMasteryOverlayQuery(
    [property: Required]
    string StudentId,
    string? SubjectFilter = null);

/// <summary>
/// MST-017: Response containing the rich mastery overlay.
/// </summary>
public sealed record MasteryOverlayResponse(
    IReadOnlyDictionary<string, Mastery.ConceptMasteryState> Overlay);

/// <summary>
/// A single review item in the schedule.
/// </summary>
public sealed record ReviewItem(
    string ConceptId,
    string ConceptName,
    double PredictedRecall,
    double HalfLifeHours,
    string Priority,
    DateTimeOffset DueAt);

// =============================================================================
// SYNC OFFLINE EVENTS
// =============================================================================

/// <summary>
/// Sync offline events command. Contains a batch of events queued on client device.
/// </summary>
public sealed record SyncOfflineEvents(
    string StudentId,
    IReadOnlyList<OfflineEvent> Events,
    string CorrelationId = "");

/// <summary>Base for offline events. Each offline event carries an idempotency key.</summary>
public abstract record OfflineEvent(
    DateTimeOffset ClientTimestamp,
    string IdempotencyKey);

/// <summary>Offline attempt event.</summary>
public sealed record OfflineAttemptEvent(
    DateTimeOffset ClientTimestamp,
    string IdempotencyKey,
    string SessionId,
    string ConceptId,
    string QuestionId,
    QuestionType QuestionType,
    string Answer,
    int ResponseTimeMs,
    int HintCountUsed,
    bool WasSkipped,
    int BackspaceCount,
    int AnswerChangeCount) : OfflineEvent(ClientTimestamp, IdempotencyKey);

// =============================================================================
// STAGNATION SIGNALS (value objects)
// =============================================================================

public sealed record StagnationSignals(
    double AccuracyPlateau,
    double ResponseTimeDrift,
    double SessionAbandonment,
    double ErrorRepetition,
    double AnnotationSentiment);

// =============================================================================
// INTERNAL MESSAGES (actor-to-actor, not part of public contract)
// =============================================================================

/// <summary>Timer tick for periodic memory budget checks.</summary>
internal sealed record MemoryCheckTick;

/// <summary>Internal message sent from stagnation detector to parent.</summary>
internal sealed record StagnationDetected(
    string ConceptId,
    double CompositeScore,
    StagnationSignals Signals,
    int ConsecutiveStagnantSessions);

// ConceptMasteredNotification: use Cena.Actors.Outreach.ConceptMasteredNotification
// Streak updates: use Cena.Actors.Outreach.UpdateActivity

/// <summary>Request session summary before teardown.</summary>
internal sealed record GetSessionSummary;

/// <summary>Session summary response from LearningSessionActor.</summary>
internal sealed record SessionSummary(
    int DurationMinutes,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double AvgResponseTimeMs,
    double FatigueScore,
    string? LastConceptId);

// UpdateSignals: use Cena.Actors.Stagnation.UpdateStagnationSignals
// CheckStagnation: use Cena.Actors.Stagnation.CheckStagnation
// ResetAfterSwitch: use Cena.Actors.Stagnation.ResetAfterSwitch

// =============================================================================
// EVALUATE ANSWER (forwarded to session actor)
// =============================================================================

public sealed record EvaluateAnswer(
    string SessionId,
    string QuestionId,
    string Answer,
    int ResponseTimeMs,
    int? Confidence,
    int BackspaceCount,
    int AnswerChangeCount);

public sealed record EvaluateAnswerResponse(
    string QuestionId,
    bool IsCorrect,
    double Score,
    string Explanation,
    ErrorType ClassifiedErrorType,
    double UpdatedMastery,
    string NextAction,
    int XpEarned);
