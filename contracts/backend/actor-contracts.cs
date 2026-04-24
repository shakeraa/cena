// ═══════════════════════════════════════════════════════════════════════════════
// Cena Platform — Proto.Actor Message Contracts & Grain Interfaces
// Layer: Backend Contracts | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - All messages are C# records (immutable, structural equality).
//   - Validation via System.ComponentModel.DataAnnotations for command messages.
//   - Domain events are defined in marten-event-store.cs — these are COMMANDS.
//   - Proto.Actor grain interfaces use ClusterIdentity keyed by student/session ID.
//   - Messages follow CQRS: commands mutate state, queries read projections.
//   - All IDs are UUIDv7 (time-sortable). All timestamps are DateTimeOffset (UTC).
// ═══════════════════════════════════════════════════════════════════════════════

using System.ComponentModel.DataAnnotations;
using Proto;
using Proto.Cluster;

namespace Cena.Contracts.Actors;

// ─────────────────────────────────────────────────────────────────────────────
// 1. SHARED VALUE OBJECTS & ENUMS
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Supported teaching methodologies. Matches MCM graph values.
/// Keep in sync with: docs/system-overview.md methodology mapping table.
/// </summary>
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

/// <summary>
/// Error types classified by the LLM ACL (Kimi K2.5 tier).
/// Precedence order for methodology switching: Conceptual > Procedural > Motivational.
/// See: docs/system-overview.md §Stagnation Detection.
/// </summary>
public enum ErrorType
{
    None,
    Conceptual,
    Procedural,
    Motivational
}

/// <summary>
/// Reasons a session can end. Drives analytics and stagnation detection.
/// </summary>
public enum SessionEndReason
{
    Completed,
    Fatigue,
    Abandoned,
    Timeout,
    AppBackgrounded,
    StudentRequested
}

/// <summary>
/// Difficulty levels aligned with revised Bloom's taxonomy.
/// See: Anderson & Krathwohl (2001).
/// </summary>
public enum DifficultyLevel
{
    Recall,
    Comprehension,
    Application,
    Analysis
}

/// <summary>
/// Annotation types for student-created notes.
/// </summary>
public enum AnnotationType
{
    Note,
    Question,
    Insight,
    Confusion
}

/// <summary>
/// Outreach communication channels.
/// </summary>
public enum OutreachChannel
{
    WhatsApp,
    Telegram,
    Push,
    Voice
}

/// <summary>
/// Question format types matching assessment-specification.md Section 1.
/// </summary>
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

/// <summary>
/// Stagnation sub-signal snapshot. Carried in stagnation-related messages
/// so consumers have full context without querying back.
/// </summary>
public sealed record StagnationSignals(
    double AccuracyPlateau,
    double ResponseTimeDrift,
    double SessionAbandonment,
    double ErrorRepetition,
    double AnnotationSentiment
);

/// <summary>
/// Standard envelope for all actor command results. Follows the Result pattern
/// to avoid exceptions crossing actor boundaries (Proto.Actor best practice).
/// </summary>
public sealed record ActorResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

/// <summary>
/// Typed result envelope with a payload. Use for query responses.
/// </summary>
public sealed record ActorResult<T>(
    bool Success,
    T? Data = default,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

// ─────────────────────────────────────────────────────────────────────────────
// 2. STUDENT ACTOR MESSAGES
//    Actor type: Virtual (grain), event-sourced via Marten.
//    ClusterIdentity: kind = "student", identity = studentId (UUIDv7).
//    Lifecycle: auto-activated on first message, passivated after idle timeout.
//    See: architecture-design.md §4.2 Actor Hierarchy.
// ─────────────────────────────────────────────────────────────────────────────

// ── Commands ──

/// <summary>
/// Record a student's attempt at a concept exercise.
/// Triggers BKT update, stagnation signal update, XP award, and NATS publication.
/// This is the primary hot-path command — optimized for minimal allocation.
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

    /// <summary>Student's answer (plaintext for evaluation, hashed before persistence).</summary>
    [property: Required]
    string Answer,

    /// <summary>Client-measured response time in milliseconds.</summary>
    [property: Range(0, 600_000)]
    int ResponseTimeMs,

    /// <summary>Number of hints the student used before answering.</summary>
    [property: Range(0, 3)]
    int HintCountUsed,

    /// <summary>True if the student skipped without answering.</summary>
    bool WasSkipped,

    /// <summary>Behavioral: backspace/delete key count (uncertainty signal).</summary>
    [property: Range(0, int.MaxValue)]
    int BackspaceCount,

    /// <summary>Behavioral: how many times the answer was changed before submit.</summary>
    [property: Range(0, int.MaxValue)]
    int AnswerChangeCount,

    /// <summary>True if this attempt was queued offline and synced later.</summary>
    bool WasOffline
)
{
    /// <summary>Correlation ID for end-to-end tracing. Set by the SignalR hub.</summary>
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

    /// <summary>Optional: resume a specific concept. Null = system selects via knowledge graph overlay.</summary>
    string? ConceptId,

    [property: Required]
    string DeviceType,

    [property: Required]
    string AppVersion,

    /// <summary>Client-reported timestamp for offline sync reconciliation.</summary>
    DateTimeOffset ClientTimestamp,

    bool IsOffline
)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Returned to the caller after StartSession is processed.
/// </summary>
public sealed record StartSessionResponse(
    string SessionId,
    string StartingConceptId,
    string StartingConceptName,
    Methodology ActiveMethodology,
    int CurrentXp,
    int StreakDays
);

/// <summary>
/// End a learning session. The StudentActor tears down the child session actor.
/// </summary>
public sealed record EndSession(
    [property: Required]
    string StudentId,

    [property: Required]
    string SessionId,

    SessionEndReason Reason
)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Student-initiated methodology switch ("Change approach" button).
/// Trigger is always "student_requested". The new methodology is selected from
/// the student-friendly label mapping (see system-overview.md §Student Control).
/// </summary>
public sealed record SwitchMethodology(
    [property: Required]
    string StudentId,

    [property: Required]
    string ConceptId,

    /// <summary>
    /// The student-friendly label selected by the student.
    /// Maps to a Methodology enum internally. See system-overview.md.
    /// </summary>
    [property: Required]
    string StudentFriendlyLabel
)
{
    public string CorrelationId { get; init; } = "";
}

/// <summary>
/// Add a free-text annotation to a concept. Text is NLP-analyzed for sentiment
/// (via Kimi K2.5) and hashed before persistence. The plaintext is encrypted at rest.
/// </summary>
public sealed record AddAnnotation(
    [property: Required]
    string StudentId,

    [property: Required]
    string ConceptId,

    [property: Required]
    string SessionId,

    /// <summary>Free-text note. Supports Markdown. Max 5000 chars.</summary>
    [property: Required, StringLength(5000)]
    string Text,

    AnnotationType Kind
)
{
    public string CorrelationId { get; init; } = "";
}

// ── Queries (read from in-memory actor state, no event store round-trip) ──

/// <summary>
/// Query the student's current mastery overlay. Returns in-memory state — microsecond latency.
/// </summary>
public sealed record GetStudentProfile(
    [property: Required]
    string StudentId
);

/// <summary>
/// Response for GetStudentProfile. Snapshot of the actor's in-memory state.
/// </summary>
public sealed record StudentProfileResponse(
    string StudentId,
    IReadOnlyDictionary<string, double> MasteryMap,
    IReadOnlyDictionary<string, string> ActiveMethodologyMap,
    int TotalXp,
    int CurrentStreak,
    int LongestStreak,
    DateTimeOffset LastActivityDate,
    string? ExperimentCohort,
    int SessionCount
);

/// <summary>
/// Get the spaced repetition review schedule for a student.
/// </summary>
public sealed record GetReviewSchedule(
    [property: Required]
    string StudentId,

    /// <summary>Max items to return, ordered by urgency.</summary>
    [property: Range(1, 50)]
    int MaxItems = 10
);

/// <summary>
/// A single review item in the schedule.
/// </summary>
public sealed record ReviewItem(
    string ConceptId,
    string ConceptName,
    double PredictedRecall,
    double HalfLifeHours,
    string Priority,
    DateTimeOffset DueAt
);

// ─────────────────────────────────────────────────────────────────────────────
// 3. LEARNING SESSION ACTOR MESSAGES
//    Actor type: Classic (child of StudentActor), transactional, session-scoped.
//    Lifecycle: created by StudentActor on StartSession, stopped on EndSession.
//    See: architecture-design.md §4.2 — "classic actors for transactional work."
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Present the next exercise to the student. The session actor selects the question
/// based on the student's knowledge overlay and active methodology.
/// </summary>
public sealed record PresentExercise(
    string SessionId,
    string ConceptId,
    string QuestionId,
    QuestionType QuestionType,
    DifficultyLevel Difficulty,
    Methodology Methodology,
    /// <summary>The question content (may contain LaTeX/Markdown).</summary>
    string QuestionText,
    /// <summary>Optional diagram (inline SVG or URL). Null if none.</summary>
    string? DiagramUrl,
    /// <summary>Multiple-choice options. Null unless QuestionType = MultipleChoice.</summary>
    IReadOnlyList<ChoiceOption>? Options,
    /// <summary>True if this is a spaced-repetition review item.</summary>
    bool IsReview,
    /// <summary>Position in session sequence (1-based).</summary>
    int QuestionIndex
);

public sealed record ChoiceOption(string Id, string Text);

/// <summary>
/// Evaluate the student's answer. Calls the LLM ACL (Kimi K2.5) for structured
/// evaluation and error classification. Returns the evaluation result.
/// </summary>
public sealed record EvaluateAnswer(
    [property: Required]
    string SessionId,

    [property: Required]
    string QuestionId,

    /// <summary>Student's raw answer text.</summary>
    [property: Required]
    string Answer,

    /// <summary>Client-measured response time in milliseconds.</summary>
    [property: Range(0, 600_000)]
    int ResponseTimeMs,

    /// <summary>Optional student self-reported confidence (1-5 scale).</summary>
    [property: Range(1, 5)]
    int? Confidence,

    /// <summary>Behavioral signals for stagnation detection.</summary>
    int BackspaceCount,
    int AnswerChangeCount
);

/// <summary>
/// Result of answer evaluation. Pushed to client via SignalR.
/// </summary>
public sealed record EvaluateAnswerResponse(
    string QuestionId,
    bool IsCorrect,
    /// <summary>Partial credit score (0.0 - 1.0).</summary>
    double Score,
    /// <summary>LLM-generated explanation of the evaluation.</summary>
    string Explanation,
    ErrorType ClassifiedErrorType,
    /// <summary>Updated P(known) after BKT update.</summary>
    double UpdatedMastery,
    /// <summary>Hint for the next pedagogical move.</summary>
    string NextAction,
    /// <summary>XP earned for this answer.</summary>
    int XpEarned
);

/// <summary>
/// Request a hint for the current question. Calls the LLM ACL (Claude Sonnet)
/// for hint generation at the specified scaffolding level.
/// </summary>
public sealed record RequestHint(
    [property: Required]
    string SessionId,

    [property: Required]
    string QuestionId,

    /// <summary>Hint scaffolding level: 1=gentle nudge, 2=partial reveal, 3=near-answer.</summary>
    [property: Range(1, 3)]
    int HintLevel
);

/// <summary>
/// Hint response pushed to client.
/// </summary>
public sealed record HintResponse(
    string QuestionId,
    int HintLevel,
    /// <summary>LLM-generated hint text (may contain LaTeX/Markdown).</summary>
    string HintText
);

/// <summary>
/// Skip the current question. Records behavioral data for stagnation detection.
/// </summary>
public sealed record SkipQuestion(
    [property: Required]
    string SessionId,

    [property: Required]
    string QuestionId,

    /// <summary>Time spent viewing the question before skipping (ms).</summary>
    [property: Range(0, 600_000)]
    int TimeSpentBeforeSkipMs,

    /// <summary>Optional student-reported reason. Helps stagnation detector.</summary>
    string? Reason
);

// ─────────────────────────────────────────────────────────────────────────────
// 4. STAGNATION DETECTOR ACTOR MESSAGES
//    Actor type: Classic (child of StudentActor), timer-based sliding window.
//    Monitors: accuracy plateau, response time drift, session abandonment,
//              error repetition, annotation sentiment.
//    See: intelligence-layer.md §Flywheel 5 for signal weights and normalization.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Update stagnation signals after each concept attempt or session event.
/// Called by the parent StudentActor after processing AttemptConcept.
/// This is an internal message — not exposed via SignalR.
/// </summary>
public sealed record UpdateSignals(
    string StudentId,
    string ConceptId,
    string SessionId,

    /// <summary>Was this attempt correct?</summary>
    bool IsCorrect,

    /// <summary>Response time in milliseconds.</summary>
    int ResponseTimeMs,

    /// <summary>Classified error type for this attempt.</summary>
    ErrorType ClassifiedErrorType,

    /// <summary>Session duration at time of signal update (minutes).</summary>
    int SessionDurationMinutes,

    /// <summary>Latest annotation sentiment score (0.0-1.0). Null if no annotation.</summary>
    double? AnnotationSentiment,

    /// <summary>Student's trailing 20-question baseline accuracy.</summary>
    double BaselineAccuracy,

    /// <summary>Student's trailing 20-question baseline response time (ms).</summary>
    double BaselineResponseTimeMs
);

/// <summary>
/// Explicitly check stagnation state for a concept cluster.
/// Typically fired by a Proto.Actor timer at session boundaries.
/// </summary>
public sealed record CheckStagnation(
    string StudentId,
    string ConceptId
);

/// <summary>
/// Result of stagnation check.
/// </summary>
public sealed record StagnationCheckResult(
    bool IsStagnating,

    /// <summary>Composite stagnation score (0.0-1.0). Threshold: 0.7 for 3 consecutive sessions.</summary>
    double CompositeScore,

    /// <summary>Breakdown of individual signal contributions.</summary>
    StagnationSignals Signals,

    /// <summary>How many consecutive sessions this concept has been stagnating.</summary>
    int ConsecutiveStagnantSessions,

    /// <summary>Recommended action if stagnating.</summary>
    string? RecommendedAction
);

/// <summary>
/// Reset stagnation tracking for a concept after a methodology switch.
/// Enforces the 3-session cooldown before re-evaluating stagnation.
/// See: system-overview.md §Stagnation Detection point 5.
/// </summary>
public sealed record ResetAfterSwitch(
    string StudentId,
    string ConceptId,
    Methodology NewMethodology,

    /// <summary>
    /// Minimum sessions before stagnation re-evaluation.
    /// Default: 3 (from system-overview.md). Configurable per experiment cohort.
    /// </summary>
    [property: Range(1, 10)]
    int CooldownSessions = 3
);

// ─────────────────────────────────────────────────────────────────────────────
// 5. OUTREACH SCHEDULER ACTOR MESSAGES
//    Actor type: Classic (child of StudentActor), timer-based.
//    Manages: streak reminders, spaced repetition reviews, re-engagement nudges.
//    The actor IS the scheduler — no external cron jobs.
//    See: architecture-design.md §4.4 Spaced Repetition as Actor Timers.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Schedule an outreach reminder. The OutreachSchedulerActor sets a Proto.Actor
/// timer that fires at the scheduled time, publishing to NATS for the Outreach Context.
/// </summary>
public sealed record ScheduleReminder(
    [property: Required]
    string StudentId,

    /// <summary>Unique ID for this reminder (for cancellation).</summary>
    [property: Required]
    string ReminderId,

    /// <summary>Why this reminder is being sent.</summary>
    [property: Required]
    string TriggerType,

    /// <summary>When to fire the reminder (UTC). Must be in the future.</summary>
    DateTimeOffset ScheduledAt,

    /// <summary>
    /// Preferred channel. The Outreach Context may override based on delivery rules
    /// (e.g., quiet hours, channel availability).
    /// </summary>
    OutreachChannel PreferredChannel,

    /// <summary>Optional concept ID for review reminders.</summary>
    string? ConceptId,

    /// <summary>Optional priority override. Default: standard.</summary>
    string Priority = "standard"
);

/// <summary>
/// Cancel a previously scheduled reminder. Idempotent — no error if already fired or cancelled.
/// </summary>
public sealed record CancelReminder(
    [property: Required]
    string StudentId,

    [property: Required]
    string ReminderId
);

/// <summary>
/// Update per-student contact preferences. Forwarded from the Outreach Context
/// when the student changes notification settings.
/// </summary>
public sealed record UpdateContactPreferences(
    [property: Required]
    string StudentId,

    /// <summary>Ordered list of preferred channels (most preferred first).</summary>
    IReadOnlyList<OutreachChannel> ChannelPreference,

    /// <summary>
    /// Quiet hours window (UTC). No outreach during this window.
    /// Null = no restriction.
    /// </summary>
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,

    /// <summary>IANA timezone for the student (e.g., "Asia/Jerusalem").</summary>
    [property: Required]
    string Timezone,

    /// <summary>Content language preference for outreach messages.</summary>
    string ContentLanguage = "he"
);

// ─────────────────────────────────────────────────────────────────────────────
// 6. METHODOLOGY SWITCH SERVICE MESSAGES
//    This is a domain service (not an actor), called by the StudentActor when
//    stagnation is detected. It reads across sessions + profile state to decide
//    the optimal methodology switch.
//    See: architecture-design.md §3.2.3, system-overview.md §Methodology Selection.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request a methodology switch decision. The service consults the MCM graph,
/// filters out previously attempted methods, and returns the best candidate.
/// </summary>
public sealed record DecideSwitchRequest(
    [property: Required]
    string StudentId,

    [property: Required]
    string ConceptId,

    /// <summary>The concept's category in the curriculum graph (e.g., "algebra", "calculus").</summary>
    [property: Required]
    string ConceptCategory,

    /// <summary>The dominant error type from the last 3 sessions.</summary>
    ErrorType DominantErrorType,

    /// <summary>Current methodology that is failing.</summary>
    Methodology CurrentMethodology,

    /// <summary>
    /// Methodologies already attempted for this concept cluster.
    /// These will be excluded from candidates. See cycling prevention in system-overview.md.
    /// </summary>
    IReadOnlyList<string> MethodAttemptHistory,

    /// <summary>Stagnation composite score that triggered this decision.</summary>
    double StagnationScore,

    /// <summary>Number of consecutive stagnant sessions.</summary>
    int ConsecutiveStagnantSessions
);

/// <summary>
/// Methodology switch decision result.
/// </summary>
public sealed record DecideSwitchResponse(
    bool ShouldSwitch,

    /// <summary>The recommended methodology. Null if escalation is needed.</summary>
    Methodology? RecommendedMethodology,

    /// <summary>MCM graph confidence in this recommendation (0.0-1.0).</summary>
    double Confidence,

    /// <summary>
    /// True when all methodologies have been exhausted for this concept cluster.
    /// The StudentActor should flag the concept as "mentor-resistant."
    /// See: system-overview.md §Escalation.
    /// </summary>
    bool AllMethodologiesExhausted,

    /// <summary>If escalating, the recommended escalation action.</summary>
    string? EscalationAction,

    /// <summary>Reasoning trace for observability. Logged, not sent to client.</summary>
    string DecisionTrace
);

// ─────────────────────────────────────────────────────────────────────────────
// 7. PROTO.ACTOR GRAIN INTERFACES
//    Virtual actors (grains) auto-activate on first message and passivate on idle.
//    ClusterIdentity key pattern: "{kind}/{identity}" e.g., "student/{studentId}".
//    Grain interfaces define the contract between the cluster client and the grain.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Student grain — the event-sourced aggregate root for the Learner bounded context.
/// One instance per student in the cluster. Owns: mastery overlay, methodology history,
/// spaced repetition schedule, cognitive load profile, experiment cohort.
///
/// IMPORTANT: This grain holds the StudentProfileSnapshot in memory. All queries
/// against student state are served from this in-memory snapshot — no DB round-trip.
/// State mutations produce domain events persisted to Marten and published to NATS.
/// </summary>
public interface IStudentGrain
{
    // ── Commands (mutate state, produce events) ──
    Task<ActorResult<EvaluateAnswerResponse>> AttemptConcept(AttemptConcept command);
    Task<ActorResult<StartSessionResponse>> StartSession(StartSession command);
    Task<ActorResult> EndSession(EndSession command);
    Task<ActorResult> SwitchMethodology(SwitchMethodology command);
    Task<ActorResult> AddAnnotation(AddAnnotation command);

    // ── Queries (read in-memory state) ──
    Task<ActorResult<StudentProfileResponse>> GetProfile(GetStudentProfile query);
    Task<ActorResult<IReadOnlyList<ReviewItem>>> GetReviewSchedule(GetReviewSchedule query);
}

/// <summary>
/// Learning session grain interface. Although sessions use classic actors (children
/// of StudentActor), this interface documents the message protocol.
/// In practice, the StudentActor forwards messages to its child session actor.
/// External callers always go through the StudentGrain.
/// </summary>
public interface ILearningSessionGrain
{
    Task<ActorResult<PresentExercise>> PresentNextExercise();
    Task<ActorResult<EvaluateAnswerResponse>> EvaluateAnswer(EvaluateAnswer command);
    Task<ActorResult<HintResponse>> RequestHint(RequestHint command);
    Task<ActorResult> SkipQuestion(SkipQuestion command);
}

/// <summary>
/// Stagnation detector — child actor interface for documentation purposes.
/// The StudentActor manages the lifecycle of this actor internally.
/// </summary>
public interface IStagnationDetectorGrain
{
    Task<ActorResult> UpdateSignals(UpdateSignals command);
    Task<ActorResult<StagnationCheckResult>> CheckStagnation(CheckStagnation query);
    Task<ActorResult> ResetAfterSwitch(ResetAfterSwitch command);
}

/// <summary>
/// Outreach scheduler — child actor interface for documentation purposes.
/// Timers are internal to the actor; external callers schedule via messages.
/// </summary>
public interface IOutreachSchedulerGrain
{
    Task<ActorResult> ScheduleReminder(ScheduleReminder command);
    Task<ActorResult> CancelReminder(CancelReminder command);
    Task<ActorResult> UpdateContactPreferences(UpdateContactPreferences command);
}
