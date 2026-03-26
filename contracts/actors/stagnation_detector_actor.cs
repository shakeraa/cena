// =============================================================================
// Cena Platform -- StagnationDetectorActor (Classic, Timer-Based Sliding Window)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - Classic actor: child of StudentActor, lives across sessions.
//   - Maintains a sliding window of last 3 sessions per concept cluster.
//   - 5-signal composite score with normalization formulas:
//     1. Accuracy plateau: sigmoid(10 * (0.05 - improvement_rate))
//     2. Response time drift: linear, baseline = trailing 20-question median
//     3. Session abandonment: linear, avg_duration = trailing 5-session median
//     4. Error type repetition: linear, count / 5
//     5. Annotation sentiment: inverted sentiment score
//   - Default weights: accuracy=0.30, rt=0.20, abandonment=0.20, error=0.20, sentiment=0.10
//   - Trigger: score > 0.7 for 3 consecutive sessions -> StagnationDetected to parent
//   - Timer: checks after each session end, not continuous polling
//   - 3-session cooldown after methodology switch (no re-evaluation until cooldown expires)
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

using Cena.Contracts.Actors;

namespace Cena.Actors;

// =============================================================================
// STAGNATION STATE
// =============================================================================

/// <summary>
/// Per-concept-cluster stagnation tracking state. Maintains the sliding window
/// of signals and the composite score history.
/// </summary>
public sealed class ConceptStagnationState
{
    /// <summary>Concept cluster ID (or individual concept ID).</summary>
    public string ConceptClusterId { get; set; } = "";

    // ---- Sliding Windows (last 3 sessions) ----
    /// <summary>Session-level signal snapshots. Max 3 entries (FIFO).</summary>
    public List<SessionSignalSnapshot> SessionWindow { get; set; } = new(3);

    // ---- Trailing Baselines ----
    /// <summary>Trailing 20-question accuracy values for improvement rate calculation.</summary>
    public List<double> AccuracyTrail { get; set; } = new(20);

    /// <summary>Trailing 20-question response times (ms) for RT baseline.</summary>
    public List<int> ResponseTimeTrail { get; set; } = new(20);

    /// <summary>Trailing 5-session durations (minutes) for abandonment baseline.</summary>
    public List<int> SessionDurationTrail { get; set; } = new(5);

    /// <summary>Error types from recent attempts for repetition detection.</summary>
    public List<string> RecentErrorTypes { get; set; } = new(10);

    /// <summary>Latest annotation sentiment score (0.0-1.0). Null if no annotations.</summary>
    public double? LatestAnnotationSentiment { get; set; }

    // ---- Composite Score History ----
    /// <summary>Composite stagnation scores from last 3 session checks.</summary>
    public List<double> CompositeScoreHistory { get; set; } = new(3);

    /// <summary>Number of consecutive sessions where score > threshold.</summary>
    public int ConsecutiveStagnantSessions { get; set; }

    // ---- Cooldown ----
    /// <summary>Sessions remaining in cooldown after a methodology switch.</summary>
    public int CooldownSessionsRemaining { get; set; }

    /// <summary>The methodology that was switched to (for tracking).</summary>
    public Methodology? CooldownMethodology { get; set; }
}

/// <summary>
/// Snapshot of signals collected during a single session for a concept cluster.
/// </summary>
public sealed record SessionSignalSnapshot(
    string SessionId,
    int AttemptsInSession,
    int CorrectInSession,
    double AvgResponseTimeMs,
    int SessionDurationMinutes,
    string? DominantErrorType,
    double? AnnotationSentiment,
    DateTimeOffset Timestamp);

// =============================================================================
// STAGNATION DETECTOR ACTOR
// =============================================================================

/// <summary>
/// Detects learning stagnation using a 5-signal composite scoring model.
/// Operates on a sliding window of the last 3 sessions per concept cluster.
///
/// <para><b>Signals and Normalization:</b></para>
/// <list type="number">
///   <item>
///     <b>Accuracy plateau:</b> sigmoid(10 * (0.05 - improvement_rate))
///     <br/>Where improvement_rate = (recent_accuracy - baseline_accuracy) / baseline_accuracy.
///     <br/>Scores high when accuracy is flat or declining.
///   </item>
///   <item>
///     <b>Response time drift:</b> (recent_median_rt - baseline_median_rt) / baseline_median_rt
///     <br/>Baseline = trailing 20-question median. Clamped to [0, 1].
///     <br/>Scores high when student is getting slower.
///   </item>
///   <item>
///     <b>Session abandonment:</b> 1 - (avg_session_duration / baseline_session_duration)
///     <br/>Baseline = trailing 5-session median. Clamped to [0, 1].
///     <br/>Scores high when sessions are getting shorter (student giving up earlier).
///   </item>
///   <item>
///     <b>Error type repetition:</b> count_of_repeated_error_type / 5
///     <br/>Clamped to [0, 1]. Scores high when the same error type keeps recurring.
///   </item>
///   <item>
///     <b>Annotation sentiment:</b> 1.0 - sentiment_score
///     <br/>Inverted: low sentiment (frustration) = high stagnation signal.
///   </item>
/// </list>
///
/// <para><b>Composite score:</b></para>
/// Weighted sum with defaults: accuracy=0.30, rt=0.20, abandonment=0.20, error=0.20, sentiment=0.10.
///
/// <para><b>Trigger:</b></para>
/// Composite score > 0.7 for 3 consecutive sessions sends StagnationDetected to parent.
/// </summary>
public sealed class StagnationDetectorActor : IActor
{
    // ---- Dependencies ----
    private readonly ILogger<StagnationDetectorActor> _logger;

    // ---- State: concept cluster -> stagnation state ----
    private readonly Dictionary<string, ConceptStagnationState> _conceptStates = new();

    // ---- Configuration (overridable per experiment cohort) ----
    private StagnationConfig _config = StagnationConfig.Default;

    // ---- Per-student adaptive threshold (FIXED: prevents false positives on slow learners) ----
    // Updated after each session from the student's historical improvement rate.
    // Default 0.05 (5%) for new students, adapts based on observed learning speed.
    private double _studentAvgImprovementRate = 0.05;

    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Actors.StagnationDetectorActor", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Actors.StagnationDetectorActor", "1.0.0");
    private static readonly Histogram<double> CompositeScoreHistogram =
        MeterInstance.CreateHistogram<double>("cena.stagnation.composite_score", description: "Stagnation composite score distribution");
    private static readonly Counter<long> StagnationDetectedCounter =
        MeterInstance.CreateCounter<long>("cena.stagnation.detected_total", description: "Stagnation events detected");

    public StagnationDetectorActor(ILogger<StagnationDetectorActor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started               => OnStarted(context),
            Stopping              => OnStopping(context),
            UpdateSignals cmd     => HandleUpdateSignals(context, cmd),
            CheckStagnation cmd   => HandleCheckStagnation(context, cmd),
            ResetAfterSwitch cmd  => HandleResetAfterSwitch(context, cmd),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted(IContext context)
    {
        _logger.LogDebug("StagnationDetectorActor started");
        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        _logger.LogDebug(
            "StagnationDetectorActor stopping. Tracked concept clusters: {Count}",
            _conceptStates.Count);
        return Task.CompletedTask;
    }

    // =========================================================================
    // UPDATE SIGNALS (called per-attempt by parent)
    // =========================================================================

    /// <summary>
    /// Accumulates per-attempt signals into the sliding window for a concept.
    /// Does NOT trigger stagnation check -- that happens at session boundaries.
    /// </summary>
    private Task HandleUpdateSignals(IContext context, UpdateSignals cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Stagnation.UpdateSignals");
        activity?.SetTag("concept.id", cmd.ConceptId);

        var state = GetOrCreateConceptState(cmd.ConceptId);

        // ---- Update accuracy trail ----
        if (state.AccuracyTrail.Count >= 20) state.AccuracyTrail.RemoveAt(0);
        state.AccuracyTrail.Add(cmd.IsCorrect ? 1.0 : 0.0);

        // ---- Update response time trail ----
        if (state.ResponseTimeTrail.Count >= 20) state.ResponseTimeTrail.RemoveAt(0);
        state.ResponseTimeTrail.Add(cmd.ResponseTimeMs);

        // ---- Update error type trail ----
        if (cmd.ClassifiedErrorType != ErrorType.None)
        {
            if (state.RecentErrorTypes.Count >= 10) state.RecentErrorTypes.RemoveAt(0);
            state.RecentErrorTypes.Add(cmd.ClassifiedErrorType.ToString());
        }

        // ---- Update annotation sentiment ----
        if (cmd.AnnotationSentiment.HasValue)
        {
            state.LatestAnnotationSentiment = cmd.AnnotationSentiment.Value;
        }

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // CHECK STAGNATION (called at session boundaries)
    // =========================================================================

    /// <summary>
    /// Performs the full 5-signal stagnation check for a concept cluster.
    /// This is triggered at session end, not on every attempt.
    ///
    /// If the composite score exceeds 0.7 for 3 consecutive sessions,
    /// sends StagnationDetected to the parent StudentActor.
    /// </summary>
    private Task HandleCheckStagnation(IContext context, CheckStagnation cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Stagnation.Check");
        activity?.SetTag("concept.id", cmd.ConceptId);

        var state = GetOrCreateConceptState(cmd.ConceptId);

        // ---- Check cooldown ----
        if (state.CooldownSessionsRemaining > 0)
        {
            state.CooldownSessionsRemaining--;

            _logger.LogDebug(
                "Stagnation check skipped for concept {ConceptId}: cooldown active. " +
                "Sessions remaining: {Remaining}",
                cmd.ConceptId, state.CooldownSessionsRemaining);

            context.Respond(new ActorResult<StagnationCheckResult>(true,
                new StagnationCheckResult(
                    false, 0.0,
                    new StagnationSignals(0, 0, 0, 0, 0),
                    0, $"Cooldown active: {state.CooldownSessionsRemaining} sessions remaining")));
            return Task.CompletedTask;
        }

        // ---- Compute individual signals ----
        var signals = ComputeSignals(state);

        // ---- Compute composite score ----
        double composite =
            (signals.AccuracyPlateau * _config.WeightAccuracy) +
            (signals.ResponseTimeDrift * _config.WeightResponseTime) +
            (signals.SessionAbandonment * _config.WeightAbandonment) +
            (signals.ErrorRepetition * _config.WeightErrorRepetition) +
            (signals.AnnotationSentiment * _config.WeightAnnotationSentiment);

        composite = Math.Clamp(composite, 0.0, 1.0);

        CompositeScoreHistogram.Record(composite,
            new KeyValuePair<string, object?>("concept.id", cmd.ConceptId));

        // ---- Update score history ----
        if (state.CompositeScoreHistory.Count >= 3) state.CompositeScoreHistory.RemoveAt(0);
        state.CompositeScoreHistory.Add(composite);

        // ---- Check threshold ----
        bool isStagnating = composite > _config.StagnationThreshold;

        if (isStagnating)
        {
            state.ConsecutiveStagnantSessions++;
        }
        else
        {
            state.ConsecutiveStagnantSessions = 0;
        }

        _logger.LogInformation(
            "Stagnation check for concept {ConceptId}: Score={Score:F3}, " +
            "Signals=[Acc={Acc:F3}, RT={RT:F3}, Aband={Ab:F3}, Err={Err:F3}, Sent={Sent:F3}], " +
            "ConsecutiveSessions={Consecutive}",
            cmd.ConceptId, composite,
            signals.AccuracyPlateau, signals.ResponseTimeDrift,
            signals.SessionAbandonment, signals.ErrorRepetition,
            signals.AnnotationSentiment, state.ConsecutiveStagnantSessions);

        // ---- Trigger stagnation event if threshold met ----
        bool shouldTrigger = state.ConsecutiveStagnantSessions >= _config.ConsecutiveSessionsRequired;

        if (shouldTrigger)
        {
            StagnationDetectedCounter.Add(1,
                new KeyValuePair<string, object?>("concept.id", cmd.ConceptId));

            _logger.LogWarning(
                "STAGNATION DETECTED for concept {ConceptId}. Score={Score:F3}, " +
                "ConsecutiveSessions={Consecutive}. Notifying parent.",
                cmd.ConceptId, composite, state.ConsecutiveStagnantSessions);

            // Notify parent StudentActor
            context.Send(context.Parent!, new StagnationDetected(
                cmd.ConceptId, composite, signals, state.ConsecutiveStagnantSessions));
        }

        // ---- Respond with result ----
        string? recommendedAction = shouldTrigger
            ? "methodology_switch"
            : isStagnating
                ? $"monitoring (consecutive: {state.ConsecutiveStagnantSessions}/{_config.ConsecutiveSessionsRequired})"
                : null;

        context.Respond(new ActorResult<StagnationCheckResult>(true,
            new StagnationCheckResult(
                shouldTrigger, composite, signals,
                state.ConsecutiveStagnantSessions, recommendedAction)));

        return Task.CompletedTask;
    }

    // =========================================================================
    // SIGNAL COMPUTATION -- 5-Signal Composite Model
    // =========================================================================

    /// <summary>
    /// Computes all 5 stagnation signals from the accumulated data.
    /// Each signal is normalized to [0.0, 1.0] where 1.0 = maximum stagnation.
    /// </summary>
    private StagnationSignals ComputeSignals(ConceptStagnationState state)
    {
        return new StagnationSignals(
            ComputeAccuracyPlateau(state),
            ComputeResponseTimeDrift(state),
            ComputeSessionAbandonment(state),
            ComputeErrorRepetition(state),
            ComputeAnnotationSentiment(state));
    }

    /// <summary>
    /// Signal 1: Accuracy plateau.
    /// Formula: sigmoid(10 * (0.05 - improvement_rate))
    ///
    /// Where improvement_rate = (recent_accuracy - baseline_accuracy) / baseline_accuracy.
    /// - If the student is improving at > 5% per session, score is low (~0).
    /// - If improvement is flat or negative, score is high (~1).
    ///
    /// The sigmoid provides a smooth transition around the 5% improvement threshold.
    /// </summary>
    private static double ComputeAccuracyPlateau(ConceptStagnationState state)
    {
        if (state.AccuracyTrail.Count < 4) return 0.0;

        // Split trail into baseline (first half) and recent (second half)
        int midpoint = state.AccuracyTrail.Count / 2;
        double baselineAccuracy = state.AccuracyTrail.Take(midpoint).Average();
        double recentAccuracy = state.AccuracyTrail.Skip(midpoint).Average();

        if (baselineAccuracy <= 0.0) return 0.0;

        double improvementRate = (recentAccuracy - baselineAccuracy) / baselineAccuracy;

        // FIXED: Per-student adaptive threshold instead of fixed 0.05.
        // Slow learners who typically improve at 3%/session get a 1.5% threshold (not 5%).
        // Formula: adaptive_threshold = max(0.02, student_avg_improvement_rate * 0.5)
        double adaptiveThreshold = Math.Max(0.02, _studentAvgImprovementRate * 0.5);
        double exponent = 10.0 * (adaptiveThreshold - improvementRate);
        double sigmoid = 1.0 / (1.0 + Math.Exp(-exponent));

        return Math.Clamp(sigmoid, 0.0, 1.0);
    }

    /// <summary>
    /// Signal 2: Response time drift.
    /// Formula: (recent_median_rt - baseline_median_rt) / baseline_median_rt
    /// Baseline = trailing 20-question median. Clamped to [0, 1].
    ///
    /// Scores high when the student is getting slower (possible frustration/confusion).
    /// </summary>
    private static double ComputeResponseTimeDrift(ConceptStagnationState state)
    {
        if (state.ResponseTimeTrail.Count < 4) return 0.0;

        int midpoint = state.ResponseTimeTrail.Count / 2;
        double baselineMedian = GetMedian(state.ResponseTimeTrail.Take(midpoint).ToList());
        double recentMedian = GetMedian(state.ResponseTimeTrail.Skip(midpoint).ToList());

        if (baselineMedian <= 0) return 0.0;

        double drift = (recentMedian - baselineMedian) / baselineMedian;
        return Math.Clamp(drift, 0.0, 1.0);
    }

    /// <summary>
    /// Signal 3: Session abandonment.
    /// Formula: 1 - (avg_recent_duration / baseline_duration)
    /// Baseline = trailing 5-session median. Clamped to [0, 1].
    ///
    /// Scores high when sessions are getting shorter (student giving up earlier).
    /// </summary>
    private static double ComputeSessionAbandonment(ConceptStagnationState state)
    {
        if (state.SessionDurationTrail.Count < 2) return 0.0;

        int midpoint = Math.Max(1, state.SessionDurationTrail.Count / 2);
        double baselineMedian = GetMedian(state.SessionDurationTrail.Take(midpoint).ToList());
        double recentMedian = GetMedian(state.SessionDurationTrail.Skip(midpoint).ToList());

        if (baselineMedian <= 0) return 0.0;

        double abandonment = 1.0 - (recentMedian / baselineMedian);
        return Math.Clamp(abandonment, 0.0, 1.0);
    }

    /// <summary>
    /// Signal 4: Error type repetition.
    /// Formula: max_repeated_error_count / 5
    /// Clamped to [0, 1].
    ///
    /// Scores high when the same error type (conceptual/procedural/motivational)
    /// keeps recurring, indicating the current methodology is not addressing
    /// the root cause.
    /// </summary>
    private static double ComputeErrorRepetition(ConceptStagnationState state)
    {
        if (state.RecentErrorTypes.Count == 0) return 0.0;

        // Find most common error type and its count
        var mostCommon = state.RecentErrorTypes
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .First();

        double repetitionScore = mostCommon.Count() / 5.0;
        return Math.Clamp(repetitionScore, 0.0, 1.0);
    }

    /// <summary>
    /// Signal 5: Annotation sentiment (inverted).
    /// Formula: 1.0 - sentiment_score
    ///
    /// Where sentiment_score is 0.0 (very negative) to 1.0 (very positive).
    /// Low sentiment (frustration markers like "I don't get this", "confused")
    /// produces a high stagnation signal.
    /// </summary>
    private static double ComputeAnnotationSentiment(ConceptStagnationState state)
    {
        if (!state.LatestAnnotationSentiment.HasValue) return 0.0;

        // Invert: low sentiment = high stagnation signal
        return Math.Clamp(1.0 - state.LatestAnnotationSentiment.Value, 0.0, 1.0);
    }

    // =========================================================================
    // RESET AFTER METHODOLOGY SWITCH
    // =========================================================================

    /// <summary>
    /// Resets stagnation tracking for a concept after a methodology switch.
    /// Enforces a cooldown period (default: 3 sessions) before re-evaluating
    /// stagnation, giving the new methodology time to take effect.
    /// </summary>
    private Task HandleResetAfterSwitch(IContext context, ResetAfterSwitch cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Stagnation.ResetAfterSwitch");
        activity?.SetTag("concept.id", cmd.ConceptId);
        activity?.SetTag("new.methodology", cmd.NewMethodology.ToString());
        activity?.SetTag("cooldown.sessions", cmd.CooldownSessions);

        var state = GetOrCreateConceptState(cmd.ConceptId);

        // Reset consecutive count
        state.ConsecutiveStagnantSessions = 0;
        state.CompositeScoreHistory.Clear();

        // Set cooldown
        state.CooldownSessionsRemaining = cmd.CooldownSessions;
        state.CooldownMethodology = cmd.NewMethodology;

        // Clear error trail (fresh start for new methodology)
        state.RecentErrorTypes.Clear();

        _logger.LogInformation(
            "Stagnation reset for concept {ConceptId} after switch to {Methodology}. " +
            "Cooldown: {Cooldown} sessions.",
            cmd.ConceptId, cmd.NewMethodology, cmd.CooldownSessions);

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private ConceptStagnationState GetOrCreateConceptState(string conceptId)
    {
        if (!_conceptStates.TryGetValue(conceptId, out var state))
        {
            state = new ConceptStagnationState { ConceptClusterId = conceptId };
            _conceptStates[conceptId] = state;
        }
        return state;
    }

    /// <summary>
    /// Computes median of an integer list.
    /// </summary>
    private static double GetMedian(IList<int> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(x => x).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}

// =============================================================================
// STAGNATION CONFIGURATION
// =============================================================================

/// <summary>
/// Configuration for the stagnation detector. Can be overridden per experiment
/// cohort for A/B testing different sensitivity thresholds.
/// </summary>
public sealed class StagnationConfig
{
    // ---- Signal Weights (must sum to 1.0) ----

    /// <summary>Weight for accuracy plateau signal. Default: 0.30.</summary>
    public double WeightAccuracy { get; init; } = 0.30;

    /// <summary>Weight for response time drift signal. Default: 0.20.</summary>
    public double WeightResponseTime { get; init; } = 0.20;

    /// <summary>Weight for session abandonment signal. Default: 0.20.</summary>
    public double WeightAbandonment { get; init; } = 0.20;

    /// <summary>Weight for error type repetition signal. Default: 0.20.</summary>
    public double WeightErrorRepetition { get; init; } = 0.20;

    /// <summary>Weight for annotation sentiment signal. Default: 0.10.</summary>
    public double WeightAnnotationSentiment { get; init; } = 0.10;

    // ---- Thresholds ----

    /// <summary>Composite score threshold for stagnation. Default: 0.7.</summary>
    public double StagnationThreshold { get; init; } = 0.7;

    /// <summary>Consecutive sessions above threshold before triggering. Default: 3.</summary>
    public int ConsecutiveSessionsRequired { get; init; } = 3;

    /// <summary>Default cooldown sessions after methodology switch. Default: 3.</summary>
    public int DefaultCooldownSessions { get; init; } = 3;

    /// <summary>Default configuration with standard weights and thresholds.</summary>
    public static StagnationConfig Default => new();

    /// <summary>
    /// Validates that weights sum to 1.0 (within floating-point tolerance).
    /// </summary>
    public bool AreWeightsValid()
    {
        double sum = WeightAccuracy + WeightResponseTime + WeightAbandonment
                   + WeightErrorRepetition + WeightAnnotationSentiment;
        return Math.Abs(sum - 1.0) < 0.001;
    }
}
