// =============================================================================
// Cena Platform -- LearningSessionActor (Classic, Session-Scoped)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - Classic actor: created by StudentActor on StartSession, destroyed on
//     EndSession. NOT a virtual actor -- no cluster identity, no passivation.
//   - Transactional: all state is session-scoped and discarded on teardown.
//   - BKT update logic is INLINE (not a service call) -- this is the hot path.
//   - Cognitive load monitoring: fatigue score computed per question, early
//     termination when fatigue exceeds threshold.
//   - Item selection: next question based on KST prerequisite graph + BKT priority.
//   - Session timeout: 45 minutes max.
//   - All state mutations are published to the parent StudentActor which
//     persists them as domain events.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

using Cena.Contracts.Actors;

namespace Cena.Actors;

// =============================================================================
// SESSION STATE -- transient, discarded on session end
// =============================================================================

/// <summary>
/// Transient state for an active learning session. Created fresh on each
/// session start and discarded when the session ends. Not persisted --
/// the parent StudentActor is responsible for event persistence.
/// </summary>
public sealed class LearningSessionState
{
    // ---- Identity ----
    public string SessionId { get; set; } = "";
    public string StudentId { get; set; } = "";

    // ---- Session Configuration ----
    public Methodology ActiveMethodology { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    // ---- Current Question ----
    public string? CurrentQuestionId { get; set; }
    public string? CurrentConceptId { get; set; }
    public DifficultyLevel CurrentDifficulty { get; set; }
    public int CurrentHintLevel { get; set; }
    public DateTimeOffset? QuestionPresentedAt { get; set; }

    // ---- Session Progress ----
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public int QuestionsSkipped { get; set; }
    public int TotalHintsUsed { get; set; }
    public int QuestionIndex { get; set; }

    // ---- Response Time Tracking ----
    public List<int> ResponseTimesMs { get; set; } = new();

    // ---- Fatigue / Cognitive Load ----
    /// <summary>
    /// Running fatigue score (0.0-1.0). Computed after each question.
    /// When this exceeds 0.8, the session recommends termination.
    /// </summary>
    public double FatigueScore { get; set; }

    /// <summary>Per-question fatigue contributions for the sliding window.</summary>
    public List<FatigueDataPoint> FatigueWindow { get; set; } = new();

    // ---- BKT Parameters (per concept, session-scoped cache) ----
    /// <summary>
    /// Session-local BKT parameter cache. Concept ID -> BKT params.
    /// Initialized from the student's mastery map on session start.
    /// </summary>
    public Dictionary<string, BktParameters> BktCache { get; set; } = new();

    // ---- Concept Queue ----
    /// <summary>
    /// Ordered queue of concepts to present, based on KST + BKT priority.
    /// Refreshed after every 5 questions or on methodology switch.
    /// </summary>
    public Queue<ConceptQueueItem> ConceptQueue { get; set; } = new();

    // ---- Constants ----
    public const int MaxSessionDurationMinutes = 45;
    public const double FatigueTerminationThreshold = 0.8;
    public const int ConceptQueueRefreshInterval = 5;
}

/// <summary>
/// Data point for fatigue score calculation. One per question attempted.
/// </summary>
public sealed record FatigueDataPoint(
    int ResponseTimeMs,
    bool IsCorrect,
    int HintsUsed,
    bool WasSkipped,
    int BackspaceCount,
    int AnswerChangeCount,
    DateTimeOffset Timestamp);

/// <summary>
/// BKT parameters for a single concept. Standard 4-parameter model.
/// See: Corbett & Anderson (1994).
/// </summary>
public sealed class BktParameters
{
    /// <summary>P(L0): Prior probability of knowing the concept.</summary>
    public double PriorKnown { get; set; } = 0.3;

    /// <summary>P(T): Probability of learning the concept on each attempt.</summary>
    public double PLearn { get; set; } = 0.1;

    /// <summary>P(G): Probability of guessing correctly when not known.</summary>
    public double PGuess { get; set; } = 0.25;

    /// <summary>P(S): Probability of slipping (wrong answer when known).</summary>
    public double PSlip { get; set; } = 0.1;

    /// <summary>Current posterior P(known) after all observations.</summary>
    public double PKnown { get; set; } = 0.3;
}

/// <summary>
/// An item in the concept selection queue.
/// </summary>
public sealed record ConceptQueueItem(
    string ConceptId,
    string? QuestionId,
    DifficultyLevel Difficulty,
    double Priority,
    bool IsReview);

// =============================================================================
// LEARNING SESSION ACTOR
// =============================================================================

/// <summary>
/// Manages a single active learning session for a student. This is a classic
/// (non-virtual) actor created as a child of the StudentActor.
///
/// <para><b>Responsibilities:</b></para>
/// <list type="bullet">
///   <item>BKT mastery update (inline, not a service call -- hot path)</item>
///   <item>Cognitive load / fatigue monitoring with per-question scoring</item>
///   <item>Item selection: next question based on KST graph + BKT priority</item>
///   <item>Session timeout enforcement (45 min max)</item>
///   <item>Fatigue-based early termination</item>
/// </list>
///
/// <para><b>Lifecycle:</b></para>
/// Created by StudentActor on StartSession, stopped on EndSession or timeout.
/// All state is transient -- the parent persists events to Marten.
/// </summary>
public sealed class LearningSessionActor : IActor
{
    // ---- Dependencies ----
    private readonly ILogger<LearningSessionActor> _logger;

    // ---- State ----
    private readonly LearningSessionState _state;
    private readonly StudentState _studentState; // read-only reference to parent's state

    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Actors.LearningSessionActor", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Actors.LearningSessionActor", "1.0.0");
    private static readonly Histogram<double> BktUpdateLatency =
        MeterInstance.CreateHistogram<double>("cena.session.bkt_update_us", description: "BKT update latency in microseconds");
    private static readonly Histogram<double> FatigueScoreHistogram =
        MeterInstance.CreateHistogram<double>("cena.session.fatigue_score", description: "Fatigue score distribution");
    private static readonly Counter<long> SessionTimeoutCounter =
        MeterInstance.CreateCounter<long>("cena.session.timeouts_total", description: "Sessions ended by timeout");

    // ---- Timer handle for session timeout ----
    private CancellationTokenSource? _timeoutCts;

    public LearningSessionActor(
        string sessionId,
        string studentId,
        Methodology methodology,
        StudentState studentState,
        ILogger<LearningSessionActor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _studentState = studentState ?? throw new ArgumentNullException(nameof(studentState));

        _state = new LearningSessionState
        {
            SessionId = sessionId,
            StudentId = studentId,
            ActiveMethodology = methodology,
            StartedAt = DateTimeOffset.UtcNow
        };

        // Initialize BKT cache from student's mastery map
        foreach (var (conceptId, pKnown) in studentState.MasteryMap)
        {
            _state.BktCache[conceptId] = new BktParameters { PKnown = pKnown };
        }
    }

    /// <summary>
    /// Main message dispatch for the session actor.
    /// </summary>
    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started             => OnStarted(context),
            Stopping            => OnStopping(context),
            Stopped             => OnStopped(context),

            // ---- Session messages ----
            PresentNextQuestion => HandlePresentNextQuestion(context),
            EvaluateAnswer cmd  => HandleEvaluateAnswer(context, cmd),
            RequestHint cmd     => HandleRequestHint(context, cmd),
            SkipQuestion cmd    => HandleSkipQuestion(context, cmd),
            SessionTimeoutTick  => HandleSessionTimeout(context),
            GetSessionSummary   => HandleGetSessionSummary(context),

            _ => Task.CompletedTask
        };
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    private Task OnStarted(IContext context)
    {
        _logger.LogInformation(
            "LearningSessionActor started. Session={SessionId}, Student={StudentId}, " +
            "Methodology={Methodology}",
            _state.SessionId, _state.StudentId, _state.ActiveMethodology);

        // Set session timeout (45 minutes)
        _timeoutCts = new CancellationTokenSource();
        var timeout = TimeSpan.FromMinutes(LearningSessionState.MaxSessionDurationMinutes);
        _ = Task.Delay(timeout, _timeoutCts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) context.Send(context.Self, new SessionTimeoutTick());
        });

        // Build initial concept queue
        RefreshConceptQueue();

        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();

        _logger.LogInformation(
            "LearningSessionActor stopping. Session={SessionId}, " +
            "QuestionsAttempted={Attempted}, Correct={Correct}, FatigueScore={Fatigue:F3}",
            _state.SessionId, _state.QuestionsAttempted,
            _state.QuestionsCorrect, _state.FatigueScore);

        return Task.CompletedTask;
    }

    private Task OnStopped(IContext context)
    {
        return Task.CompletedTask;
    }

    // =========================================================================
    // PRESENT NEXT QUESTION
    // =========================================================================

    /// <summary>
    /// Selects and presents the next question based on KST prerequisite graph
    /// and BKT-based priority scoring. The algorithm:
    ///
    /// 1. Check fatigue threshold -- recommend termination if exceeded.
    /// 2. Check session duration -- enforce 45-minute max.
    /// 3. Dequeue next concept from priority queue (refresh every 5 questions).
    /// 4. Select specific question within concept based on difficulty + methodology.
    /// 5. Return PresentExercise to parent for relay to client.
    /// </summary>
    private Task HandlePresentNextQuestion(IContext context)
    {
        using var activity = ActivitySourceInstance.StartActivity("Session.PresentNextQuestion");

        // ---- Fatigue check ----
        if (_state.FatigueScore >= LearningSessionState.FatigueTerminationThreshold)
        {
            _logger.LogInformation(
                "Session {SessionId}: Fatigue threshold reached ({Fatigue:F3}). " +
                "Recommending termination.",
                _state.SessionId, _state.FatigueScore);

            // Notify parent to end session due to fatigue
            context.Send(context.Parent!, new EndSession(
                _state.StudentId, _state.SessionId, SessionEndReason.Fatigue));

            context.Respond(new ActorResult<PresentExercise>(
                false, ErrorCode: "FATIGUE_THRESHOLD",
                ErrorMessage: "Cognitive load threshold exceeded. Session should end."));
            return Task.CompletedTask;
        }

        // ---- Duration check ----
        var elapsed = DateTimeOffset.UtcNow - _state.StartedAt;
        if (elapsed.TotalMinutes >= LearningSessionState.MaxSessionDurationMinutes)
        {
            context.Send(context.Parent!, new EndSession(
                _state.StudentId, _state.SessionId, SessionEndReason.Timeout));

            context.Respond(new ActorResult<PresentExercise>(
                false, ErrorCode: "SESSION_TIMEOUT",
                ErrorMessage: "Maximum session duration exceeded."));
            return Task.CompletedTask;
        }

        // ---- Refresh queue if needed ----
        if (_state.ConceptQueue.Count == 0 ||
            _state.QuestionIndex % LearningSessionState.ConceptQueueRefreshInterval == 0)
        {
            RefreshConceptQueue();
        }

        // ---- Dequeue next item ----
        if (_state.ConceptQueue.Count == 0)
        {
            context.Respond(new ActorResult<PresentExercise>(
                false, ErrorCode: "NO_QUESTIONS",
                ErrorMessage: "No more questions available in current concept space."));
            return Task.CompletedTask;
        }

        var item = _state.ConceptQueue.Dequeue();
        _state.QuestionIndex++;

        // ---- Update current question state ----
        _state.CurrentQuestionId = item.QuestionId ?? $"q_{item.ConceptId}_{_state.QuestionIndex}";
        _state.CurrentConceptId = item.ConceptId;
        _state.CurrentDifficulty = item.Difficulty;
        _state.CurrentHintLevel = 0;
        _state.QuestionPresentedAt = DateTimeOffset.UtcNow;

        // ---- Build response ----
        var exercise = new PresentExercise(
            _state.SessionId,
            item.ConceptId,
            _state.CurrentQuestionId,
            QuestionType.MultipleChoice, // TODO: select based on methodology + difficulty
            item.Difficulty,
            _state.ActiveMethodology,
            "", // QuestionText -- filled by content service
            null, // DiagramUrl
            null, // Options -- filled by content service
            item.IsReview,
            _state.QuestionIndex);

        context.Respond(new ActorResult<PresentExercise>(true, exercise));
        return Task.CompletedTask;
    }

    // =========================================================================
    // EVALUATE ANSWER (HOT PATH -- BKT INLINE)
    // =========================================================================

    /// <summary>
    /// Evaluates a student's answer. Performs inline BKT update (no service call),
    /// computes fatigue score, and returns evaluation result to parent.
    ///
    /// <para><b>BKT Update (Bayesian Knowledge Tracing):</b></para>
    /// Standard 4-parameter model (Corbett & Anderson, 1994).
    /// P(L_n | obs) = P(obs | L_n) * P(L_n) / P(obs)
    /// Then: P(L_{n+1}) = P(L_n | obs) + (1 - P(L_n | obs)) * P(T)
    ///
    /// This runs inline because it is on the critical path (~1 microsecond).
    /// </summary>
    private Task HandleEvaluateAnswer(IContext context, EvaluateAnswer cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Session.EvaluateAnswer");
        activity?.SetTag("session.id", _state.SessionId);
        activity?.SetTag("question.id", cmd.QuestionId);

        var sw = Stopwatch.StartNew();

        // ---- Validate current question ----
        if (_state.CurrentQuestionId != cmd.QuestionId)
        {
            context.Respond(new ActorResult<EvaluateAnswerResponse>(
                false, ErrorCode: "QUESTION_MISMATCH",
                ErrorMessage: $"Current question is {_state.CurrentQuestionId}, not {cmd.QuestionId}"));
            return Task.CompletedTask;
        }

        var conceptId = _state.CurrentConceptId!;

        // ---- TODO: LLM evaluation for correctness + error classification ----
        // In production, this calls the LLM ACL (Kimi K2.5) via a circuit-breaker-
        // protected client. For now, we use the answer directly.
        bool isCorrect = true; // placeholder -- LLM determines this
        double score = isCorrect ? 1.0 : 0.0;
        var errorType = isCorrect ? ErrorType.None : ErrorType.Procedural;

        // ---- BKT UPDATE (inline, microsecond-level) ----
        var bkt = GetOrCreateBktParams(conceptId);
        var updatedMastery = BktUpdate(bkt, isCorrect);

        sw.Stop();
        BktUpdateLatency.Record(sw.Elapsed.TotalMicroseconds);

        // ---- Update session state ----
        _state.QuestionsAttempted++;
        if (isCorrect) _state.QuestionsCorrect++;
        _state.ResponseTimesMs.Add(cmd.ResponseTimeMs);

        // ---- Compute fatigue score ----
        var fatiguePoint = new FatigueDataPoint(
            cmd.ResponseTimeMs, isCorrect, _state.CurrentHintLevel,
            false, cmd.BackspaceCount, cmd.AnswerChangeCount,
            DateTimeOffset.UtcNow);
        _state.FatigueWindow.Add(fatiguePoint);
        _state.FatigueScore = ComputeFatigueScore();

        FatigueScoreHistogram.Record(_state.FatigueScore);

        // ---- XP calculation ----
        int xpEarned = isCorrect
            ? Math.Max(2, 10 - (_state.CurrentHintLevel * 2))
            : (int)(score * 5);

        // ---- Determine next action hint ----
        string nextAction;
        if (_state.FatigueScore >= LearningSessionState.FatigueTerminationThreshold * 0.9)
            nextAction = "consider_break";
        else if (updatedMastery >= 0.85)
            nextAction = "advance_concept";
        else if (updatedMastery < 0.4 && !isCorrect)
            nextAction = "provide_scaffolding";
        else
            nextAction = "continue";

        // ---- Build response ----
        var response = new EvaluateAnswerResponse(
            cmd.QuestionId,
            isCorrect,
            score,
            "", // Explanation -- filled by LLM ACL
            errorType,
            updatedMastery,
            nextAction,
            xpEarned);

        context.Respond(new ActorResult<EvaluateAnswerResponse>(true, response));

        _logger.LogDebug(
            "Session {SessionId}: Answer evaluated. Concept={ConceptId}, Correct={Correct}, " +
            "P(known)={PKnown:F3}, Fatigue={Fatigue:F3}, NextAction={NextAction}",
            _state.SessionId, conceptId, isCorrect, updatedMastery,
            _state.FatigueScore, nextAction);

        return Task.CompletedTask;
    }

    // =========================================================================
    // BKT -- BAYESIAN KNOWLEDGE TRACING (INLINE HOT PATH)
    // =========================================================================

    /// <summary>
    /// Standard BKT update. Given an observation (correct/incorrect), computes
    /// the posterior P(known) using Bayes' theorem, then applies the learning
    /// transition.
    ///
    /// <para><b>Formulas:</b></para>
    /// <code>
    /// If correct:
    ///   P(L_n | correct) = P(L_n) * (1 - P(S)) / [P(L_n) * (1 - P(S)) + (1 - P(L_n)) * P(G)]
    /// If incorrect:
    ///   P(L_n | incorrect) = P(L_n) * P(S) / [P(L_n) * P(S) + (1 - P(L_n)) * (1 - P(G))]
    /// Then:
    ///   P(L_{n+1}) = P(L_n | obs) + (1 - P(L_n | obs)) * P(T)
    /// </code>
    ///
    /// Performance: ~100ns per call. No allocations.
    /// </summary>
    /// <param name="bkt">BKT parameters for the concept.</param>
    /// <param name="isCorrect">Whether the student answered correctly.</param>
    /// <returns>Updated P(known) value.</returns>
    private static double BktUpdate(BktParameters bkt, bool isCorrect)
    {
        double pLn = bkt.PKnown;
        double pS = bkt.PSlip;
        double pG = bkt.PGuess;
        double pT = bkt.PLearn;

        // ---- Posterior: P(L_n | observation) ----
        double pCorrectGivenKnown = 1.0 - pS;
        double pCorrectGivenNotKnown = pG;
        double pIncorrectGivenKnown = pS;
        double pIncorrectGivenNotKnown = 1.0 - pG;

        double posterior;
        if (isCorrect)
        {
            double numerator = pLn * pCorrectGivenKnown;
            double denominator = numerator + (1.0 - pLn) * pCorrectGivenNotKnown;
            posterior = denominator > 0 ? numerator / denominator : pLn;
        }
        else
        {
            double numerator = pLn * pIncorrectGivenKnown;
            double denominator = numerator + (1.0 - pLn) * pIncorrectGivenNotKnown;
            posterior = denominator > 0 ? numerator / denominator : pLn;
        }

        // ---- Learning transition: P(L_{n+1}) ----
        double updated = posterior + (1.0 - posterior) * pT;

        // Clamp to [0.01, 0.99] to avoid degenerate states
        updated = Math.Clamp(updated, 0.01, 0.99);

        bkt.PKnown = updated;
        return updated;
    }

    /// <summary>
    /// Gets existing BKT params or creates default for a new concept.
    /// </summary>
    private BktParameters GetOrCreateBktParams(string conceptId)
    {
        if (!_state.BktCache.TryGetValue(conceptId, out var bkt))
        {
            // Initialize from student's mastery map if available
            var prior = _studentState.MasteryMap.GetValueOrDefault(conceptId, 0.3);
            bkt = new BktParameters
            {
                PriorKnown = prior,
                PKnown = prior,
                PLearn = 0.1,
                PGuess = 0.25,
                PSlip = 0.1
            };
            _state.BktCache[conceptId] = bkt;
        }
        return bkt;
    }

    // =========================================================================
    // COGNITIVE LOAD / FATIGUE MONITORING
    // =========================================================================

    /// <summary>
    /// Computes cognitive load / fatigue score from the sliding window of
    /// recent question interactions. The score is a weighted composite of:
    ///
    /// <list type="bullet">
    ///   <item>Response time drift: how much slower than baseline</item>
    ///   <item>Accuracy decline: recent accuracy vs. session baseline</item>
    ///   <item>Hint dependency: increasing hint usage</item>
    ///   <item>Skip rate: increasing question skips</item>
    ///   <item>Behavioral signals: backspace/answer-change frequency</item>
    /// </list>
    ///
    /// Window size: last 5 questions. Score: 0.0 (fresh) to 1.0 (exhausted).
    /// </summary>
    private double ComputeFatigueScore()
    {
        const int windowSize = 5;
        var window = _state.FatigueWindow
            .Skip(Math.Max(0, _state.FatigueWindow.Count - windowSize))
            .ToList();

        if (window.Count < 2) return 0.0;

        // ---- 1. Response time drift (0.0-1.0) ----
        // Compare recent median to overall session median
        var overallMedianRt = GetMedian(_state.ResponseTimesMs);
        var windowMedianRt = GetMedian(window.Select(w => w.ResponseTimeMs).ToList());
        double rtDrift = overallMedianRt > 0
            ? Math.Clamp((windowMedianRt - overallMedianRt) / overallMedianRt, 0.0, 1.0)
            : 0.0;

        // ---- 2. Accuracy decline (0.0-1.0) ----
        double windowAccuracy = window.Count(w => w.IsCorrect) / (double)window.Count;
        double overallAccuracy = _state.QuestionsAttempted > 0
            ? _state.QuestionsCorrect / (double)_state.QuestionsAttempted
            : 0.5;
        double accuracyDecline = Math.Clamp(overallAccuracy - windowAccuracy, 0.0, 1.0);

        // ---- 3. Hint dependency (0.0-1.0) ----
        double avgHints = window.Average(w => w.HintsUsed);
        double hintDependency = Math.Clamp(avgHints / 3.0, 0.0, 1.0);

        // ---- 4. Skip rate (0.0-1.0) ----
        double skipRate = window.Count(w => w.WasSkipped) / (double)window.Count;

        // ---- 5. Behavioral uncertainty (0.0-1.0) ----
        double avgBackspace = window.Average(w => w.BackspaceCount);
        double avgChanges = window.Average(w => w.AnswerChangeCount);
        double uncertainty = Math.Clamp((avgBackspace + avgChanges) / 20.0, 0.0, 1.0);

        // ---- Weighted composite ----
        const double wRt = 0.25;
        const double wAccuracy = 0.25;
        const double wHints = 0.20;
        const double wSkip = 0.15;
        const double wUncertainty = 0.15;

        double fatigue = (rtDrift * wRt)
                       + (accuracyDecline * wAccuracy)
                       + (hintDependency * wHints)
                       + (skipRate * wSkip)
                       + (uncertainty * wUncertainty);

        // Session duration amplifier: fatigue effect increases linearly after 20 minutes
        var sessionMinutes = (DateTimeOffset.UtcNow - _state.StartedAt).TotalMinutes;
        if (sessionMinutes > 20)
        {
            double durationFactor = 1.0 + ((sessionMinutes - 20) / 25.0) * 0.3; // up to 30% amplification
            fatigue *= Math.Min(durationFactor, 1.3);
        }

        return Math.Clamp(fatigue, 0.0, 1.0);
    }

    // =========================================================================
    // HINT HANDLING
    // =========================================================================

    /// <summary>
    /// Handles a hint request. Increments hint level (max 3), logs for
    /// stagnation detection, and returns hint text from LLM ACL.
    /// </summary>
    private Task HandleRequestHint(IContext context, RequestHint cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Session.RequestHint");

        if (_state.CurrentQuestionId != cmd.QuestionId)
        {
            context.Respond(new ActorResult<HintResponse>(
                false, ErrorCode: "QUESTION_MISMATCH"));
            return Task.CompletedTask;
        }

        if (cmd.HintLevel > 3)
        {
            context.Respond(new ActorResult<HintResponse>(
                false, ErrorCode: "MAX_HINTS_REACHED",
                ErrorMessage: "Maximum 3 hint levels available."));
            return Task.CompletedTask;
        }

        _state.CurrentHintLevel = cmd.HintLevel;
        _state.TotalHintsUsed++;

        // TODO: Call LLM ACL (Claude Sonnet) for hint generation
        var hintText = $"Hint level {cmd.HintLevel} for question {cmd.QuestionId}";

        var response = new HintResponse(cmd.QuestionId, cmd.HintLevel, hintText);
        context.Respond(new ActorResult<HintResponse>(true, response));

        _logger.LogDebug(
            "Session {SessionId}: Hint level {Level} for question {QuestionId}",
            _state.SessionId, cmd.HintLevel, cmd.QuestionId);

        return Task.CompletedTask;
    }

    // =========================================================================
    // SKIP HANDLING
    // =========================================================================

    /// <summary>
    /// Handles a question skip. Records behavioral data for stagnation
    /// detection and advances to next question.
    /// </summary>
    private Task HandleSkipQuestion(IContext context, SkipQuestion cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Session.SkipQuestion");

        if (_state.CurrentQuestionId != cmd.QuestionId)
        {
            context.Respond(new ActorResult(false, ErrorCode: "QUESTION_MISMATCH"));
            return Task.CompletedTask;
        }

        _state.QuestionsSkipped++;

        // Record fatigue data point for skip
        _state.FatigueWindow.Add(new FatigueDataPoint(
            cmd.TimeSpentBeforeSkipMs, false, 0, true, 0, 0,
            DateTimeOffset.UtcNow));
        _state.FatigueScore = ComputeFatigueScore();

        _logger.LogDebug(
            "Session {SessionId}: Question {QuestionId} skipped after {TimeMs}ms. " +
            "Reason={Reason}, Fatigue={Fatigue:F3}",
            _state.SessionId, cmd.QuestionId, cmd.TimeSpentBeforeSkipMs,
            cmd.Reason, _state.FatigueScore);

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // SESSION TIMEOUT
    // =========================================================================

    /// <summary>
    /// Handles session timeout (45 min). Notifies parent to end session.
    /// </summary>
    private Task HandleSessionTimeout(IContext context)
    {
        _logger.LogInformation(
            "Session {SessionId} timed out after {Minutes} minutes. " +
            "Questions={Attempted}, Fatigue={Fatigue:F3}",
            _state.SessionId, LearningSessionState.MaxSessionDurationMinutes,
            _state.QuestionsAttempted, _state.FatigueScore);

        SessionTimeoutCounter.Add(1);

        context.Send(context.Parent!, new EndSession(
            _state.StudentId, _state.SessionId, SessionEndReason.Timeout));

        return Task.CompletedTask;
    }

    // =========================================================================
    // SESSION SUMMARY (requested by parent before teardown)
    // =========================================================================

    private Task HandleGetSessionSummary(IContext context)
    {
        var elapsed = DateTimeOffset.UtcNow - _state.StartedAt;
        var avgRt = _state.ResponseTimesMs.Count > 0
            ? _state.ResponseTimesMs.Average()
            : 0;

        context.Respond(new SessionSummary(
            (int)elapsed.TotalMinutes,
            _state.QuestionsAttempted,
            _state.QuestionsCorrect,
            avgRt,
            _state.FatigueScore,
            _state.CurrentConceptId));

        return Task.CompletedTask;
    }

    // =========================================================================
    // ITEM SELECTION ALGORITHM
    // =========================================================================

    /// <summary>
    /// Builds / refreshes the concept queue using a priority-based algorithm:
    ///
    /// 1. Get all unlocked concepts from KST prerequisite graph.
    /// 2. For each concept, compute priority score:
    ///    - P(known) closer to 0.5 = higher priority (zone of proximal development)
    ///    - Review items (HLR recall < 0.85) get priority boost
    ///    - Concepts not attempted in current session get novelty boost
    /// 3. Sort by priority descending.
    /// 4. Select appropriate difficulty level based on BKT P(known):
    ///    - P < 0.4 -> Recall
    ///    - 0.4 <= P < 0.6 -> Comprehension
    ///    - 0.6 <= P < 0.8 -> Application
    ///    - P >= 0.8 -> Analysis
    /// 5. Enqueue top N items.
    /// </summary>
    private void RefreshConceptQueue()
    {
        _state.ConceptQueue.Clear();

        // TODO: In production, query KST graph for prerequisite-unlocked concepts.
        // For now, use all concepts in the student's mastery map.
        var concepts = _studentState.MasteryMap.ToList();

        var prioritized = concepts
            .Select(kv =>
            {
                var conceptId = kv.Key;
                var pKnown = _state.BktCache.TryGetValue(conceptId, out var bkt)
                    ? bkt.PKnown
                    : kv.Value;

                // Zone of proximal development: peak priority at P(known) = 0.5
                // Uses inverted parabola: priority = 1 - 4 * (p - 0.5)^2
                double zpd = 1.0 - 4.0 * Math.Pow(pKnown - 0.5, 2);

                // Review boost: if concept has HLR timer and recall is declining
                double reviewBoost = 0.0;
                bool isReview = false;
                if (_studentState.HlrTimers.TryGetValue(conceptId, out var hlr))
                {
                    var delta = (DateTimeOffset.UtcNow - hlr.LastReviewAt).TotalHours;
                    var recall = Math.Pow(2, -delta / hlr.HalfLifeHours);
                    if (recall < 0.85)
                    {
                        reviewBoost = 0.3 * (1.0 - recall);
                        isReview = true;
                    }
                }

                // Novelty boost: concepts not yet attempted in this session
                double noveltyBoost = _state.BktCache.ContainsKey(conceptId) ? 0.0 : 0.1;

                double totalPriority = zpd + reviewBoost + noveltyBoost;

                // Determine difficulty level from P(known)
                var difficulty = pKnown switch
                {
                    < 0.4 => DifficultyLevel.Recall,
                    < 0.6 => DifficultyLevel.Comprehension,
                    < 0.8 => DifficultyLevel.Application,
                    _     => DifficultyLevel.Analysis
                };

                return new ConceptQueueItem(conceptId, null, difficulty, totalPriority, isReview);
            })
            .OrderByDescending(c => c.Priority)
            .Take(10);

        foreach (var item in prioritized)
        {
            _state.ConceptQueue.Enqueue(item);
        }

        _logger.LogDebug(
            "Session {SessionId}: Refreshed concept queue. Items={Count}",
            _state.SessionId, _state.ConceptQueue.Count);
    }

    // =========================================================================
    // UTILITY
    // =========================================================================

    /// <summary>
    /// Computes the median of an integer list. Used for response time baselines.
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
// INTERNAL MESSAGES
// =============================================================================

/// <summary>Request to present the next question in the session.</summary>
internal sealed record PresentNextQuestion;

/// <summary>Timer tick for session timeout.</summary>
internal sealed record SessionTimeoutTick;
