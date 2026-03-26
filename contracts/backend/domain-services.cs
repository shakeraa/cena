// ═══════════════════════════════════════════════════════════════════════════════
// Cena Platform — Core Domain Service Interfaces
// Layer: Domain | Runtime: .NET 9
//
// DESIGN NOTES:
//   - Domain services encapsulate cross-aggregate or stateless domain logic.
//   - They are NOT actors — they are injected into actors via DI.
//   - All methods are async (I/O for graph lookups, LLM calls, or DB reads).
//   - Return types use Result<T> pattern — no exceptions across actor boundaries.
//   - Interfaces are testable: pure functions where possible, mockable I/O otherwise.
//   - Parameter objects are C# records for immutability and structural equality.
//
// REFERENCES:
//   - BKT: Corbett & Anderson (1994), "Knowledge Tracing: Modeling the
//     Acquisition of Procedural Knowledge," UMUAI 4(4):253-278.
//   - HLR: Settles & Meeder (2016), "A Trainable Spaced Repetition Model
//     for Language Learning," ACL. github.com/duolingo/halflife-regression
//   - MCM: Squirrel AI (2025), "The Squirrel AI Adaptive Learning System,"
//     Springer. Adapted for Cena's error-type-driven switching.
//   - Stagnation signals: See docs/intelligence-layer.md §Flywheel 5.
// ═══════════════════════════════════════════════════════════════════════════════

namespace Cena.Domain.Services;

// ─────────────────────────────────────────────────────────────────────────────
// 1. BKT SERVICE — Bayesian Knowledge Tracing
//    Tracks per-concept mastery using a two-state Hidden Markov Model.
//    Parameters (p_learn, p_slip, p_guess, p_forget) are per-concept, trained
//    offline with pyBKT and loaded at silo startup.
//    See: architecture-design.md §3.2.2, intelligence-layer.md §Flywheel 2.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Bayesian Knowledge Tracing service. Computes posterior mastery P(known)
/// after each student attempt. The core hot-path calculation — must be fast
/// (microsecond-scale, no I/O in the update path).
/// </summary>
public interface IBktService
{
    /// <summary>
    /// Update mastery estimate after a single attempt.
    /// This is the core BKT update: P(known|observed) using Bayes' theorem.
    /// Called on every AttemptConcept — must be allocation-free on the hot path.
    /// </summary>
    /// <param name="input">The attempt data and current BKT state.</param>
    /// <returns>Updated mastery estimate and whether the mastery threshold was crossed.</returns>
    BktUpdateResult Update(BktUpdateInput input);

    /// <summary>
    /// Get the BKT parameters for a concept. Parameters are loaded from the
    /// offline-trained model at silo startup and cached in memory.
    /// Returns default parameters if the concept has no trained model yet.
    /// </summary>
    /// <param name="conceptId">The concept to look up.</param>
    /// <returns>BKT parameters for this concept.</returns>
    BktParameters GetParameters(string conceptId);

    /// <summary>
    /// Batch update for offline-synced attempts. Processes a sequence of
    /// attempts in order and returns the final mastery state.
    /// </summary>
    /// <param name="conceptId">The concept being traced.</param>
    /// <param name="priorMastery">Starting P(known) before the batch.</param>
    /// <param name="attempts">Ordered sequence of attempt outcomes.</param>
    /// <returns>Final mastery after all attempts are processed.</returns>
    BktUpdateResult BatchUpdate(string conceptId, double priorMastery, IReadOnlyList<AttemptOutcome> attempts);

    /// <summary>
    /// Reload BKT parameters from the retrained model (called after Flywheel 2 retraining).
    /// Thread-safe: uses copy-on-write semantics on the parameter cache.
    /// </summary>
    Task ReloadParametersAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Input for a single BKT update.
/// </summary>
public sealed record BktUpdateInput(
    string ConceptId,
    /// <summary>Current P(known) before this attempt.</summary>
    double PriorMastery,
    /// <summary>Was the student's answer correct?</summary>
    bool IsCorrect,
    /// <summary>Number of hints used (degrades the "correct" signal — more hints = more like guessing).</summary>
    int HintCountUsed,
    /// <summary>Was the question skipped? Treated as incorrect with high confidence.</summary>
    bool WasSkipped
);

/// <summary>
/// Result of a BKT update.
/// </summary>
public sealed record BktUpdateResult(
    /// <summary>Updated P(known) after this attempt.</summary>
    double PosteriorMastery,
    /// <summary>True if P(known) crossed the mastery threshold (0.85) upward.</summary>
    bool MasteryThresholdCrossed,
    /// <summary>True if P(known) dropped below the "at-risk" threshold (0.3).</summary>
    bool AtRiskThresholdCrossed,
    /// <summary>The mastery delta (posterior - prior). Positive = learning, negative = forgetting.</summary>
    double MasteryDelta
);

/// <summary>
/// BKT parameters for a single concept. Trained offline by pyBKT.
/// See: Corbett & Anderson (1994) for parameter definitions.
/// </summary>
public sealed record BktParameters(
    string ConceptId,
    /// <summary>P(learn): probability of transitioning from unknown to known on a single opportunity.</summary>
    double PLearning,
    /// <summary>P(slip): probability of an incorrect answer given the concept is known.</summary>
    double PSlip,
    /// <summary>P(guess): probability of a correct answer given the concept is unknown.</summary>
    double PGuess,
    /// <summary>P(forget): probability of transitioning from known to unknown. Often 0 in classic BKT.</summary>
    double PForget,
    /// <summary>Initial P(known) for new students on this concept.</summary>
    double PInitial,
    /// <summary>Mastery threshold. Default: 0.85.</summary>
    double MasteryThreshold = 0.85
);

/// <summary>
/// Minimal attempt outcome for batch processing.
/// </summary>
public sealed record AttemptOutcome(bool IsCorrect, int HintCountUsed, bool WasSkipped);

// ─────────────────────────────────────────────────────────────────────────────
// 2. HALF-LIFE REGRESSION SERVICE — Spaced Repetition Scheduling
//    Computes predicted recall and optimal review timing per concept.
//    Model: p(t) = 2^(-delta/h) where delta = time elapsed, h = half-life.
//    Half-life is personalized per student-concept pair based on practice history.
//    See: architecture-design.md §4.4, intelligence-layer.md §Flywheel 4.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Half-Life Regression service for spaced repetition scheduling.
/// Runs inside the OutreachSchedulerActor to compute review timers.
/// </summary>
public interface IHalfLifeRegressionService
{
    /// <summary>
    /// Compute predicted recall probability at a given time since last review.
    /// Formula: p(t) = 2^(-delta/h)
    /// </summary>
    /// <param name="halfLifeHours">Current half-life for this student-concept pair.</param>
    /// <param name="hoursSinceLastReview">Time elapsed since the last review.</param>
    /// <returns>Predicted recall probability (0.0-1.0).</returns>
    double PredictRecall(double halfLifeHours, double hoursSinceLastReview);

    /// <summary>
    /// Compute the time (hours from now) when predicted recall will drop below the threshold.
    /// Used to schedule the next review timer in the OutreachSchedulerActor.
    /// Formula: delta = h * log2(1/threshold) — rearranged from p(t) = 2^(-delta/h).
    /// </summary>
    /// <param name="halfLifeHours">Current half-life for this student-concept pair.</param>
    /// <param name="recallThreshold">Target recall threshold (default: 0.85).</param>
    /// <returns>Hours until recall drops below threshold.</returns>
    double ComputeTimeToThreshold(double halfLifeHours, double recallThreshold = 0.85);

    /// <summary>
    /// Update the half-life after a review attempt. The half-life increases on
    /// successful recall (strengthening) and decreases on failed recall (weakening).
    /// </summary>
    /// <param name="input">Review outcome and current half-life state.</param>
    /// <returns>Updated half-life and next review schedule.</returns>
    HalfLifeUpdateResult UpdateHalfLife(HalfLifeUpdateInput input);

    /// <summary>
    /// Compute the initial half-life when a concept is first mastered.
    /// Based on: concept difficulty, time to mastery, error patterns during learning.
    /// </summary>
    /// <param name="input">Mastery context for initial half-life estimation.</param>
    /// <returns>Initial half-life in hours.</returns>
    double ComputeInitialHalfLife(InitialHalfLifeInput input);

    /// <summary>
    /// Get the full review schedule for a student: all concepts with predicted recall
    /// and next review due time. Used by GetReviewSchedule query on the StudentActor.
    /// </summary>
    /// <param name="halfLifeMap">ConceptId -> current half-life in hours.</param>
    /// <param name="lastReviewMap">ConceptId -> DateTimeOffset of last review.</param>
    /// <param name="maxItems">Maximum items to return, sorted by urgency.</param>
    /// <returns>Ordered list of review items, most urgent first.</returns>
    IReadOnlyList<ReviewScheduleItem> ComputeReviewSchedule(
        IReadOnlyDictionary<string, double> halfLifeMap,
        IReadOnlyDictionary<string, DateTimeOffset> lastReviewMap,
        int maxItems = 10
    );
}

public sealed record HalfLifeUpdateInput(
    string ConceptId,
    /// <summary>Current half-life in hours before this review.</summary>
    double CurrentHalfLifeHours,
    /// <summary>Was the review attempt successful?</summary>
    bool RecallSuccessful,
    /// <summary>Response time in milliseconds (fast recall = stronger memory).</summary>
    int ResponseTimeMs,
    /// <summary>Hours since the last review.</summary>
    double HoursSinceLastReview
);

public sealed record HalfLifeUpdateResult(
    /// <summary>Updated half-life in hours.</summary>
    double NewHalfLifeHours,
    /// <summary>Hours until the next review should occur.</summary>
    double HoursUntilNextReview,
    /// <summary>Next review timestamp (UTC).</summary>
    DateTimeOffset NextReviewAt,
    /// <summary>Predicted recall at the scheduled review time.</summary>
    double PredictedRecallAtReview
);

public sealed record InitialHalfLifeInput(
    string ConceptId,
    /// <summary>Concept category (affects base half-life: procedural decays faster than conceptual).</summary>
    string ConceptCategory,
    /// <summary>Total attempts to reach mastery.</summary>
    int AttemptsToMastery,
    /// <summary>Total sessions that included this concept.</summary>
    int SessionsToMastery,
    /// <summary>Dominant error type during learning.</summary>
    string DominantErrorType,
    /// <summary>Final mastery level at the point of mastery.</summary>
    double FinalMasteryLevel
);

public sealed record ReviewScheduleItem(
    string ConceptId,
    string ConceptName,
    /// <summary>Current predicted recall (0.0-1.0).</summary>
    double PredictedRecall,
    /// <summary>Current half-life in hours.</summary>
    double HalfLifeHours,
    /// <summary>When the review is due.</summary>
    DateTimeOffset DueAt,
    /// <summary>"urgent" (recall < 0.5), "standard" (0.5-0.85), "low" (> 0.85 but scheduled).</summary>
    string Priority
);

// ─────────────────────────────────────────────────────────────────────────────
// 3. METHODOLOGY SWITCH SERVICE — MCM Lookup + Cycling Prevention
//    Reads the MCM graph (ErrorType x ConceptCategory -> Methodology[]) and
//    the student's method attempt history to recommend the next methodology.
//    See: architecture-design.md §3.2.1 MCM Graph, system-overview.md §Methodology.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Determines when and how to switch teaching methodologies for a student.
/// Consults the MCM graph, filters exhausted methods, enforces cooldown periods,
/// and handles escalation when all methods are exhausted.
/// </summary>
public interface IMethodologySwitchService
{
    /// <summary>
    /// Decide whether to switch methodology and which methodology to switch to.
    /// This is called by the StudentActor when stagnation is detected.
    ///
    /// Algorithm (from system-overview.md):
    /// 1. MCM_LOOKUP(dominant_error_type, concept_category) -> candidates sorted by confidence desc
    /// 2. Filter: remove any methodology in method_attempt_history for this concept cluster
    /// 3. Select: first remaining with confidence > 0.5; else first remaining regardless
    /// 4. Fallback: if no MCM entry, use error-type-only defaults
    /// 5. Escalation: if all exhausted, flag as "mentor-resistant"
    /// </summary>
    Task<MethodologySwitchDecision> DecideSwitchAsync(MethodologySwitchInput input, CancellationToken ct = default);

    /// <summary>
    /// Classify the dominant error type from recent session error logs.
    /// Uses precedence: conceptual > procedural > motivational.
    /// If multiple types are present, the highest-precedence type wins.
    /// </summary>
    /// <param name="recentErrors">Error types from the last 3 sessions.</param>
    /// <returns>The dominant error type.</returns>
    string ClassifyDominantErrorType(IReadOnlyList<string> recentErrors);

    /// <summary>
    /// Map a student-friendly label to an internal methodology.
    /// Used when the student manually requests a methodology switch.
    /// See: system-overview.md §Student Control.
    /// </summary>
    /// <param name="studentFriendlyLabel">The label the student selected.</param>
    /// <returns>The internal methodology, or null if the label is unrecognized.</returns>
    string? MapStudentLabelToMethodology(string studentFriendlyLabel);

    /// <summary>
    /// Get the MCM graph candidates for a given error type and concept category.
    /// For diagnostic/admin use — exposes the raw MCM lookup.
    /// </summary>
    Task<IReadOnlyList<McmCandidate>> GetMcmCandidatesAsync(
        string errorType,
        string conceptCategory,
        CancellationToken ct = default
    );
}

public sealed record MethodologySwitchInput(
    string StudentId,
    string ConceptId,
    /// <summary>Concept category from the curriculum graph (e.g., "algebra", "trigonometry").</summary>
    string ConceptCategory,
    /// <summary>Dominant error type from the last 3 sessions.</summary>
    string DominantErrorType,
    /// <summary>Currently active methodology.</summary>
    string CurrentMethodology,
    /// <summary>
    /// All methodologies previously attempted for this concept cluster.
    /// Used for cycling prevention — these are excluded from candidates.
    /// </summary>
    IReadOnlyList<string> MethodAttemptHistory,
    /// <summary>Composite stagnation score that triggered this evaluation.</summary>
    double StagnationScore,
    /// <summary>Number of consecutive stagnant sessions.</summary>
    int ConsecutiveStagnantSessions
);

public sealed record MethodologySwitchDecision(
    /// <summary>True if a methodology switch is recommended.</summary>
    bool ShouldSwitch,
    /// <summary>The recommended methodology. Null if escalation is needed.</summary>
    string? RecommendedMethodology,
    /// <summary>MCM confidence in this recommendation (0.0-1.0).</summary>
    double Confidence,
    /// <summary>
    /// True when all methodologies have been exhausted.
    /// Triggers the escalation path: flag as "mentor-resistant", suggest human tutor.
    /// See: system-overview.md §Escalation.
    /// </summary>
    bool AllMethodologiesExhausted,
    /// <summary>Escalation action if all methods exhausted.</summary>
    string? EscalationAction,
    /// <summary>Full reasoning trace for observability logging.</summary>
    string DecisionTrace
);

public sealed record McmCandidate(
    string Methodology,
    double Confidence,
    /// <summary>Whether this methodology has been attempted for this concept cluster.</summary>
    bool AlreadyAttempted
);

// ─────────────────────────────────────────────────────────────────────────────
// 4. COGNITIVE LOAD SERVICE — Fatigue Score Calculation
//    Estimates cognitive fatigue during a session to trigger breaks and
//    adjust difficulty. Feeds into session end decisions and the
//    CognitiveLoadCooldownComplete event.
//    See: architecture-design.md §3.2.3, event-schemas.md.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Computes real-time cognitive load / fatigue scores during a learning session.
/// Used by the LearningSessionActor to decide when to suggest breaks,
/// reduce difficulty, or end the session proactively.
/// </summary>
public interface ICognitiveLoadService
{
    /// <summary>
    /// Update the fatigue score after a question attempt.
    /// The score accumulates over the session based on multiple signals:
    /// response time increase, error rate increase, hint dependency, session duration.
    /// </summary>
    /// <param name="input">Session context and attempt data.</param>
    /// <returns>Updated fatigue assessment.</returns>
    FatigueAssessment UpdateFatigue(FatigueInput input);

    /// <summary>
    /// Compute the recommended cooldown duration based on the session's fatigue trajectory.
    /// Used to schedule the CognitiveLoadCooldownComplete timer in the OutreachSchedulerActor.
    /// </summary>
    /// <param name="fatigueScoreAtEnd">Fatigue score when the session ended.</param>
    /// <param name="sessionDurationMinutes">How long the session lasted.</param>
    /// <param name="questionsAttempted">Number of questions attempted in the session.</param>
    /// <returns>Recommended cooldown in minutes before the next session.</returns>
    int ComputeCooldownMinutes(double fatigueScoreAtEnd, int sessionDurationMinutes, int questionsAttempted);

    /// <summary>
    /// Determine if the current cognitive load warrants a difficulty adjustment.
    /// Returns a recommendation: reduce difficulty, maintain, or increase.
    /// </summary>
    /// <param name="currentFatigue">Current fatigue assessment.</param>
    /// <returns>Difficulty adjustment recommendation.</returns>
    DifficultyAdjustment RecommendDifficultyAdjustment(FatigueAssessment currentFatigue);
}

public sealed record FatigueInput(
    /// <summary>Session duration so far in minutes.</summary>
    int SessionDurationMinutes,
    /// <summary>Number of questions attempted in this session.</summary>
    int QuestionsAttempted,
    /// <summary>Number of correct answers in this session.</summary>
    int QuestionsCorrect,
    /// <summary>Response time for the most recent question (ms).</summary>
    int LatestResponseTimeMs,
    /// <summary>Rolling average response time for this session (ms).</summary>
    double SessionAvgResponseTimeMs,
    /// <summary>Student's baseline response time (trailing 20-question median, ms).</summary>
    double BaselineResponseTimeMs,
    /// <summary>Hints used in this session so far.</summary>
    int TotalHintsUsed,
    /// <summary>Questions skipped in this session so far.</summary>
    int QuestionsSkipped,
    /// <summary>Previous fatigue score (0.0 at session start).</summary>
    double PreviousFatigueScore
);

public sealed record FatigueAssessment(
    /// <summary>Composite fatigue score (0.0-1.0). 0 = fresh, 1 = exhausted.</summary>
    double FatigueScore,
    /// <summary>Primary fatigue driver: "duration", "error_rate", "response_slowdown", "hint_dependency".</summary>
    string PrimaryDriver,
    /// <summary>Should the system suggest a break?</summary>
    bool SuggestBreak,
    /// <summary>Should the system proactively end the session?</summary>
    bool SuggestEndSession,
    /// <summary>Estimated productive minutes remaining at current trajectory.</summary>
    int EstimatedProductiveMinutesRemaining
);

public sealed record DifficultyAdjustment(
    /// <summary>"reduce", "maintain", or "increase".</summary>
    string Recommendation,
    /// <summary>How many levels to adjust (-2 to +2). 0 = maintain.</summary>
    int LevelDelta,
    /// <summary>Reasoning for the adjustment.</summary>
    string Reason
);

// ─────────────────────────────────────────────────────────────────────────────
// 5. STAGNATION DETECTION SERVICE — Composite Score Calculation
//    Computes the composite stagnation score from five weighted sub-signals.
//    The StagnationDetectorActor maintains the sliding window state; this
//    service provides the stateless calculation logic.
//    See: intelligence-layer.md §Flywheel 5 for weights and normalization.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stateless calculation service for stagnation detection.
/// The StagnationDetectorActor calls this service with its sliding window data
/// to compute the composite stagnation score.
/// </summary>
public interface IStagnationDetectionService
{
    /// <summary>
    /// Compute the composite stagnation score from individual sub-signals.
    ///
    /// Formula: stagnation_score = sum(weight_i * normalized_signal_i)
    ///
    /// Default weights (from intelligence-layer.md):
    ///   Accuracy plateau:     0.30
    ///   Response time drift:  0.20
    ///   Session abandonment:  0.20
    ///   Error type repetition: 0.20
    ///   Annotation sentiment:  0.10
    ///
    /// Trigger: score > 0.7 for 3 consecutive sessions.
    /// </summary>
    StagnationScore ComputeCompositeScore(StagnationInput input);

    /// <summary>
    /// Normalize the accuracy plateau signal to [0, 1].
    /// Formula: sigmoid(10 * (0.05 - improvement_rate))
    /// where improvement_rate = (accuracy_last_10 - accuracy_prev_10) / accuracy_prev_10
    /// </summary>
    double NormalizeAccuracyPlateau(double accuracyLast10, double accuracyPrev10);

    /// <summary>
    /// Normalize the response time drift signal to [0, 1].
    /// Formula: min(1.0, max(0, (rolling_rt_5 - baseline_rt) / baseline_rt) / 0.4)
    /// </summary>
    double NormalizeResponseTimeDrift(double rollingResponseTime5, double baselineResponseTime);

    /// <summary>
    /// Normalize the session abandonment signal to [0, 1].
    /// Formula: min(1.0, max(0, (avg_duration - session_duration) / avg_duration) / 0.6)
    /// </summary>
    double NormalizeSessionAbandonment(double avgSessionDuration, double currentSessionDuration);

    /// <summary>
    /// Normalize the error type repetition signal to [0, 1].
    /// Formula: min(1.0, repeat_count / 5)
    /// </summary>
    double NormalizeErrorRepetition(int repeatCount);

    /// <summary>
    /// Normalize the annotation sentiment signal to [0, 1].
    /// Formula: 1.0 - sentiment_score (inverted: high frustration = high signal).
    /// Returns 0.5 if no annotations exist.
    /// </summary>
    double NormalizeAnnotationSentiment(double? sentimentScore);

    /// <summary>
    /// Get the current signal weights. May differ per experiment cohort.
    /// </summary>
    /// <param name="experimentCohort">The student's A/B test cohort (null = default weights).</param>
    /// <returns>Current weight configuration.</returns>
    StagnationWeights GetWeights(string? experimentCohort = null);

    /// <summary>
    /// Reload stagnation weights from the retrained model (called after Flywheel 5 retraining).
    /// Thread-safe: uses copy-on-write semantics.
    /// </summary>
    Task ReloadWeightsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine the recommended action based on the stagnation assessment.
    /// Actions: "switch_methodology", "reduce_difficulty", "offer_break", "review_prerequisites".
    /// </summary>
    string RecommendAction(StagnationScore score, string dominantErrorType, int consecutiveStagnantSessions);
}

public sealed record StagnationInput(
    /// <summary>Accuracy of the last 10 attempts on the concept cluster.</summary>
    double AccuracyLast10,
    /// <summary>Accuracy of the 10 attempts before that (for improvement rate).</summary>
    double AccuracyPrev10,
    /// <summary>Rolling average response time of the last 5 attempts (ms).</summary>
    double RollingResponseTime5,
    /// <summary>Student's trailing 20-question baseline response time (ms).</summary>
    double BaselineResponseTime,
    /// <summary>Student's trailing 5-session average duration (minutes).</summary>
    double AvgSessionDurationMinutes,
    /// <summary>Current session duration (minutes).</summary>
    double CurrentSessionDurationMinutes,
    /// <summary>Count of repeated same-error-type occurrences across the last 3 sessions.</summary>
    int ErrorRepeatCount,
    /// <summary>Latest annotation sentiment score (0.0-1.0 from NLP). Null if no annotations.</summary>
    double? AnnotationSentimentScore,
    /// <summary>Student's experiment cohort for weight lookup. Null = default weights.</summary>
    string? ExperimentCohort
);

public sealed record StagnationScore(
    /// <summary>Composite score (0.0-1.0). Stagnation threshold: 0.7.</summary>
    double CompositeScore,
    /// <summary>Whether the score exceeds the stagnation threshold.</summary>
    bool IsStagnating,
    /// <summary>Individual normalized signal values for observability.</summary>
    double AccuracyPlateauNormalized,
    double ResponseTimeDriftNormalized,
    double SessionAbandonmentNormalized,
    double ErrorRepetitionNormalized,
    double AnnotationSentimentNormalized,
    /// <summary>Which signal contributed the most to the composite score.</summary>
    string DominantSignal,
    /// <summary>Weights used for this calculation.</summary>
    StagnationWeights WeightsUsed
);

public sealed record StagnationWeights(
    double AccuracyPlateau,
    double ResponseTimeDrift,
    double SessionAbandonment,
    double ErrorRepetition,
    double AnnotationSentiment
)
{
    /// <summary>
    /// Default weights from intelligence-layer.md §Flywheel 5.
    /// Used until first retraining (requires 500+ stagnation events).
    /// </summary>
    public static readonly StagnationWeights Default = new(
        AccuracyPlateau: 0.30,
        ResponseTimeDrift: 0.20,
        SessionAbandonment: 0.20,
        ErrorRepetition: 0.20,
        AnnotationSentiment: 0.10
    );
}
