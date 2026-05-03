// Cena Platform — LearningSessionActor (Classic, Session-Scoped)
// Created by StudentActor on StartSession, destroyed on EndSession.
// Owns: current question, fatigue scoring, BKT updates, item selection,
// hint generation with confusion gating and disengagement suppression (SAI-01b).

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;
using Cena.Actors.Events;
using Cena.Actors.Hints;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Actors.Services;
using Cena.Actors.Students;
using Cena.Actors.Tutoring;

namespace Cena.Actors.Sessions;

/// <summary>
/// Classic child actor that manages an active learning session.
/// Handles question presentation, answer evaluation, fatigue monitoring,
/// and item selection. All events are delegated to the parent StudentActor
/// for persistence (this actor does NOT persist directly to Marten).
/// </summary>
public sealed class LearningSessionActor : IActor
{
    private readonly IBktService _bkt;
    private readonly IHintAdjustedBktService _hintAdjustedBkt;
    private readonly ICognitiveLoadService _cognitiveLoad;
    private readonly IFlowStateService _flowState;
    private readonly IHintGenerator _hintGenerator;
    private readonly IHintGenerationService _hintGenerationService;
    private readonly IConfusionDetector _confusionDetector;
    private readonly IDisengagementClassifier _disengagementClassifier;
    private readonly IDeliveryGate _deliveryGate;
    private readonly IPersonalizedExplanationService _personalizedExplanation;
    private readonly Func<TutorActor> _tutorFactory;
    private readonly Mastery.IConceptGraphCache _graphCache;
    private readonly ILogger<LearningSessionActor> _logger;

    // ── Session State ──
    private string _sessionId = "";
    private string _studentId = "";
    private string _subject = "";
    private string _methodology = "socratic";
    private string _language = "he";
    private DateTimeOffset _startedAt;
    private int _questionsAttempted;
    private int _questionsCorrect;
    private double _fatigueScore;

    // RDY-057b: concepts the student self-reported as anxious in
    // onboarding. Used as a tie-breaker signal in ZPD item selection —
    // never the primary decision. Empty / null = cold-start behaviour.
    private HashSet<string> _anxiousConceptIds = new(StringComparer.Ordinal);
    private int _consecutiveHighFatigue;
    private readonly Queue<double> _recentAccuracies = new();
    private readonly Queue<double> _recentResponseTimes = new();

    // ── RDY-034 slice 3: flow-state transition tracking ──
    // _lastFlowState is null until the first answer fires. On each answer
    // we recompute the state and emit [FLOW_STATE_TRANSITION] only when
    // the state differs from the previous one, so analytics collapsers
    // don't have to dedupe. Keeps server-side authority on the state
    // machine; clients just render what the backend reports.
    private FlowStateKind? _lastFlowState;
    private int _consecutiveCorrect;

    // ── Cached cognitive state (SAI-005: reused by DeliveryGate in both hint and explanation paths) ──
    private ConfusionState _lastConfusionState = ConfusionState.NotConfused;
    private DisengagementType? _lastDisengagementType;
    private double _baselineAccuracy = 0.5;
    private double _baselineResponseTimeMs = 5000;

    // ── Confusion tracking state (for ConfusionDetector input) ──
    private int _confusionWindowQuestions;
    private int _confusionWindowCorrect;
    private bool _lastAnswerCorrect;
    private int _lastAnswerChangeCount;
    #pragma warning disable CS0649 // Assigned by future confusion-tracking logic
    private bool _lastHintRequestedThenCancelled;
    #pragma warning restore CS0649
    private bool _lastWrongOnMastered;
    private double _lastResponseTimeRatio = 1.0;

    // ── Task 07: Tutor actor state (stubs until TutorActor is implemented) ──
    private Proto.PID? _tutorPid;
    private string _currentConceptId = "";
    private double _currentConceptMastery;
    private float _currentQuestionDifficulty;

    // ── Disengagement tracking state ──
    private int _hintRequestCount;
    #pragma warning disable CS0169 // Used by future microbreak scheduling
    private double _minutesSinceLastBreak;
    #pragma warning restore CS0169

    // ── Fatigue Configuration ──
    private const double FatigueThreshold = 0.7;
    private const int ConsecutiveFatigueLimit = 2;
    private const int MaxSessionMinutes = 45;
    private const int DefaultSessionMinutes = 25;

    // ── Telemetry (ACT-023: instance-based via IMeterFactory) ──
    private readonly ActivitySource _activitySource;
    private readonly Histogram<double> _fatigueHistogram;
    private readonly Counter<long> _questionsCounter;

    public LearningSessionActor(
        IBktService bkt,
        IHintAdjustedBktService hintAdjustedBkt,
        ICognitiveLoadService cognitiveLoad,
        IFlowStateService flowState,
        IHintGenerator hintGenerator,
        IHintGenerationService hintGenerationService,
        IConfusionDetector confusionDetector,
        IDisengagementClassifier disengagementClassifier,
        IDeliveryGate deliveryGate,
        IPersonalizedExplanationService personalizedExplanation,
        Func<TutorActor> tutorFactory,
        Mastery.IConceptGraphCache graphCache,
        ILogger<LearningSessionActor> logger,
        IMeterFactory meterFactory)
    {
        _bkt = bkt;
        _hintAdjustedBkt = hintAdjustedBkt;
        _cognitiveLoad = cognitiveLoad;
        _flowState = flowState ?? throw new ArgumentNullException(nameof(flowState));
        _hintGenerator = hintGenerator;
        _hintGenerationService = hintGenerationService;
        _confusionDetector = confusionDetector;
        _disengagementClassifier = disengagementClassifier;
        _deliveryGate = deliveryGate;
        _personalizedExplanation = personalizedExplanation;
        _tutorFactory = tutorFactory;
        _graphCache = graphCache;
        _logger = logger;
        _activitySource = new ActivitySource("Cena.Actors.LearningSession", "1.0.0");
        var meter = meterFactory.Create("Cena.Actors.LearningSession", "1.0.0");
        _fatigueHistogram = meter.CreateHistogram<double>("cena.session.fatigue_score");
        _questionsCounter = meter.CreateCounter<long>("cena.session.questions_total");
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            InitSession init => HandleInit(context, init),
            ResumeSessionRequest req => HandleResumeSession(context, req),
            GetSessionSnapshot req => HandleGetSessionSnapshot(context, req),
            EvaluateAnswerRequest req => HandleEvaluateAnswer(context, req),
            RequestNextQuestion req => HandleNextQuestion(context, req),
            RequestHintMessage req => HandleHint(context, req),
            RequestPersonalizedExplanation req => HandlePersonalizedExplanation(context, req),
            AddAnnotationMessage msg => HandleAnnotation(context, msg),
            SessionTutorMessage msg => HandleTutorMessage(context, msg),
            DelegateEvent del => HandleDelegateFromChild(context, del),
            SkipQuestionMessage req => HandleSkip(context, req),
            EndSessionRequest => HandleEndSession(context),
            Stopping => HandleStopping(context),
            _ => Task.CompletedTask
        };
    }

    // ── Init ──
    private Task HandleInit(IContext context, InitSession init)
    {
        _sessionId = init.SessionId;
        _studentId = init.StudentId;
        _subject = init.Subject;
        _methodology = init.Methodology;
        _language = init.Language ?? "he";
        _startedAt = DateTimeOffset.UtcNow;
        _baselineAccuracy = init.BaselineAccuracy;
        _baselineResponseTimeMs = init.BaselineResponseTimeMs;

        // RDY-057b: copy the anxious-concepts set if the parent actor
        // passed one. Defensive copy so the caller's collection can
        // mutate without affecting our session state.
        _anxiousConceptIds = init.AnxiousConceptIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(init.AnxiousConceptIds, StringComparer.Ordinal);

        _logger.LogInformation(
            "Session {SessionId} started for {StudentId}, subject={Subject}, methodology={Methodology}",
            _sessionId, _studentId, _subject, _methodology);

        // ACT-030: Do NOT delegate SessionStarted_V1 — the parent StudentActor already
        // emits it in HandleStartSession. This actor only delegates events it owns:
        // ConceptAttempted, HintRequested, QuestionSkipped, SessionEnded.

        return Task.CompletedTask;
    }

    // ── Answer Evaluation (REAL BKT with hint credit adjustment) ──
    private Task HandleEvaluateAnswer(IContext context, EvaluateAnswerRequest req)
    {
        using var span = _activitySource.StartActivity("EvaluateAnswer");
        _questionsCounter.Add(1);
        _questionsAttempted++;

        // SAI-01b: Hint-adjusted BKT — reduces P(T) credit based on hints used
        var bktInput = new BktUpdateInput(
            PriorMastery: req.PriorMastery,
            IsCorrect: req.IsCorrect,
            Parameters: req.BktParameters);

        var bktResult = req.HintCountUsed > 0
            ? _hintAdjustedBkt.UpdateWithHints(bktInput, req.HintCountUsed)
            : _bkt.Update(bktInput);

        if (req.IsCorrect) _questionsCorrect++;

        // SAI-005: Track confusion signals for gating
        _lastAnswerCorrect = req.IsCorrect;
        _lastAnswerChangeCount = req.AnswerChangeCount;
        _lastResponseTimeRatio = _baselineResponseTimeMs > 0
            ? req.ResponseTimeMs / _baselineResponseTimeMs
            : 1.0;
        _lastWrongOnMastered = !req.IsCorrect && req.PriorMastery > 0.7;

        // SAI-005: Update confusion window counters BEFORE detection
        // The window tracks questions since confusion was first detected.
        if (_lastConfusionState != ConfusionState.NotConfused)
        {
            _confusionWindowQuestions++;
            if (req.IsCorrect) _confusionWindowCorrect++;
        }

        // Track current concept for tutoring
        _currentConceptId = req.ConceptId;
        _currentConceptMastery = bktResult.PosteriorMastery;

        // SAI-005: Detect cognitive state for explanation delivery gating
        _lastConfusionState = _confusionDetector.Detect(BuildConfusionInput());
        _lastDisengagementType = _disengagementClassifier.Classify(BuildDisengagementInput());

        // Reset confusion window when confusion resolves
        if (_lastConfusionState == ConfusionState.NotConfused)
        {
            _confusionWindowQuestions = 0;
            _confusionWindowCorrect = 0;
        }

        // SAI-07: Auto-trigger tutoring when ConfusionStuck and no active tutor
        if (_lastConfusionState == ConfusionState.ConfusionStuck && _tutorPid == null)
        {
            _logger.LogInformation(
                "Session {SessionId}: ConfusionStuck on concept {ConceptId} — auto-triggering tutoring",
                _sessionId, req.ConceptId);
            SpawnTutorActor(context);
            context.Send(_tutorPid!, new StartTutoringFromConfusionStuck(
                _studentId, _sessionId, req.ConceptId, _subject, _language,
                _methodology, bktResult.PosteriorMastery, 3,
                _currentQuestionDifficulty));
        }

        // Track for fatigue calculation (O(1) enqueue/dequeue)
        _recentAccuracies.Enqueue(req.IsCorrect ? 1.0 : 0.0);
        if (_recentAccuracies.Count > 20) _recentAccuracies.Dequeue();

        _recentResponseTimes.Enqueue(req.ResponseTimeMs);
        if (_recentResponseTimes.Count > 20) _recentResponseTimes.Dequeue();

        // REAL fatigue score computation
        _fatigueScore = ComputeFatigueScore();
        _fatigueHistogram.Record(_fatigueScore);

        // RDY-034 slice 3: emit [FLOW_STATE_TRANSITION] when the
        // authoritative backend state changes. Consumes the same fatigue
        // score + rolling-window signals so server-side, client-side
        // (useFlowState composable), and the GET /api/sessions/{id}
        // response all agree.
        EmitFlowStateIfTransitioned(req.IsCorrect);

        // Check fatigue threshold
        bool shouldEndSession = false;
        if (_fatigueScore > FatigueThreshold)
        {
            _consecutiveHighFatigue++;
            if (_consecutiveHighFatigue >= ConsecutiveFatigueLimit)
            {
                shouldEndSession = true;
                _logger.LogInformation(
                    "Session {SessionId}: fatigue {Fatigue:F2} exceeded threshold for {Count} consecutive questions — recommending break",
                    _sessionId, _fatigueScore, _consecutiveHighFatigue);
            }
        }
        else
        {
            _consecutiveHighFatigue = 0;
        }

        // Check session timeout
        var elapsed = DateTimeOffset.UtcNow - _startedAt;
        if (elapsed.TotalMinutes >= MaxSessionMinutes)
        {
            shouldEndSession = true;
            _logger.LogInformation("Session {SessionId}: 45-minute timeout reached", _sessionId);
        }

        // Delegate ConceptAttempted event to parent
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new ConceptAttempted_V1(
                _studentId, req.ConceptId, _sessionId, req.IsCorrect,
                (int)req.ResponseTimeMs, req.QuestionId, req.QuestionType,
                _methodology, req.ErrorType, req.PriorMastery,
                bktResult.PosteriorMastery, req.HintCountUsed, false,
                req.AnswerHash, req.BackspaceCount, req.AnswerChangeCount,
                false, DateTimeOffset.UtcNow)));

        // SAI-005: Gate explanation delivery (explanations are NOT student-initiated)
        var explanationGateCtx = new DeliveryContext(
            ConfusionState: _lastConfusionState,
            DisengagementType: _lastDisengagementType,
            FocusLevel: FocusLevel.Engaged,
            IsStudentInitiated: false,
            QuestionsUntilPatience: Math.Max(0, 5 - _confusionWindowQuestions));
        var explanationDecision = _deliveryGate.Evaluate(explanationGateCtx);
        bool suppressExplanation = explanationDecision.Action != DeliveryAction.Deliver;

        if (suppressExplanation)
            _logger.LogDebug("Session {SessionId}: explanation {Action} — {Reason}",
                _sessionId, explanationDecision.Action, explanationDecision.Reason);

        context.Respond(new SessionEvaluationResult(
            IsCorrect: req.IsCorrect,
            PosteriorMastery: bktResult.PosteriorMastery,
            CrossedProgressionThreshold: bktResult.CrossedProgressionThreshold,
            FatigueScore: _fatigueScore,
            ShouldEndSession: shouldEndSession,
            ErrorType: req.ErrorType,
            SuppressExplanation: suppressExplanation,
            ExplanationSuppressedReason: explanationDecision.Reason
        ));

        return Task.CompletedTask;
    }

    // ── RDY-034 slice 3: flow-state transition emission ──
    //
    // Called once per answer after _fatigueScore is refreshed. Updates the
    // consecutive-correct streak, asks IFlowStateService for the canonical
    // state, and logs a structured transition record when the state
    // differs from the previously observed one. Idempotent: repeated
    // calls in the same state are silent (only transitions are logged).
    internal void EmitFlowStateIfTransitioned(bool lastAnswerCorrect)
    {
        _consecutiveCorrect = lastAnswerCorrect ? _consecutiveCorrect + 1 : 0;

        var rolling5 = _recentAccuracies.Count >= 5
            ? _recentAccuracies.TakeLast(5).Sum() / 5.0
            : _recentAccuracies.Count > 0
                ? _recentAccuracies.Sum() / _recentAccuracies.Count
                : 0.0;
        var trend = Math.Clamp(rolling5 - _baselineAccuracy, -1.0, 1.0);
        var elapsedMin = (DateTimeOffset.UtcNow - _startedAt).TotalMinutes;

        var assessment = _flowState.Assess(
            fatigueLevel: _fatigueScore,
            accuracyTrend: trend,
            consecutiveCorrect: _consecutiveCorrect,
            sessionDurationMinutes: elapsedMin);

        if (_lastFlowState != assessment.State)
        {
            var trigger = ResolveTransitionTrigger(_lastFlowState, assessment);
            _logger.LogInformation(
                "[FLOW_STATE_TRANSITION] session={SessionId} student={StudentId} from={From} to={To} trigger={Trigger} fatigue={Fatigue:F2} trend={Trend:F2} streak={Streak} duration_min={DurationMin:F1}",
                _sessionId, _studentId,
                _lastFlowState?.ToString() ?? "initial",
                assessment.State,
                trigger,
                _fatigueScore,
                trend,
                _consecutiveCorrect,
                elapsedMin);
            _lastFlowState = assessment.State;
        }
    }

    // Translates the (from, to, assessment) triple into a short human trigger
    // that analytics can group by without reverse-engineering the state machine.
    private static string ResolveTransitionTrigger(FlowStateKind? from, FlowStateAssessment a)
    {
        return a.State switch
        {
            FlowStateKind.Fatigued    when a.SessionDurationMinutes > 45 => "session_timeout",
            FlowStateKind.Fatigued                                       => "fatigue_threshold",
            FlowStateKind.Disrupted                                      => "accuracy_decline",
            FlowStateKind.InFlow                                         => "streak_in_flow",
            FlowStateKind.Approaching when from == FlowStateKind.Warming => "first_correct",
            FlowStateKind.Approaching                                    => "momentum_rebuild",
            FlowStateKind.Warming                                        => "session_start",
            _                                                            => "unspecified",
        };
    }

    // ── Fatigue Score (delegates to ICognitiveLoadService — single source of truth) ──
    private double ComputeFatigueScore()
    {
        double rollingAccuracy5 = _recentAccuracies.Count >= 5
            ? _recentAccuracies.TakeLast(5).Sum() / 5.0
            : _recentAccuracies.Count > 0
                ? _recentAccuracies.Sum() / _recentAccuracies.Count
                : 0.5;

        double rollingRt5 = _recentResponseTimes.Count >= 5
            ? _recentResponseTimes.TakeLast(5).Sum() / 5.0
            : _recentResponseTimes.Count > 0
                ? _recentResponseTimes.Sum() / _recentResponseTimes.Count
                : _baselineResponseTimeMs;

        double elapsedMinutes = (DateTimeOffset.UtcNow - _startedAt).TotalMinutes;

        var assessment = _cognitiveLoad.ComputeFatigue(
            _baselineAccuracy, rollingAccuracy5,
            _baselineResponseTimeMs, rollingRt5,
            elapsedMinutes, DefaultSessionMinutes);

        return assessment.FatigueScore;
    }

    // ── Item Selection (Zone of Proximal Development) ──
    private Task HandleNextQuestion(IContext context, RequestNextQuestion req)
    {
        // ZPD scoring: prioritize concepts closest to P(known) = 0.5
        // This maximizes information gain per question
        string? bestConcept = null;
        double bestScore = double.MaxValue;
        bool bestIsAnxious = false;

        foreach (var (conceptId, mastery) in req.MasteryMap)
        {
            if (mastery >= MasteryConstants.ProgressionThreshold) continue; // Already mastered, skip

            // ZPD score: distance from 0.5 (optimal learning zone)
            double zpdScore = Math.Abs(mastery - 0.5);

            // Boost concepts that are close to mastery threshold (almost there)
            if (mastery > 0.7) zpdScore *= 0.8; // 20% priority boost

            // Boost concepts due for spaced repetition review
            if (req.ReviewDueConcepts.Contains(conceptId))
                zpdScore *= 0.5; // 50% priority boost

            // RDY-057b: anxious-topic penalty — if the student
            // self-reported as anxious on this concept and there's a
            // comparably-good alternative, prefer the alternative. We
            // apply a ~15% ZPD penalty (higher score = less preferred
            // under `<` comparison below). This is a TIE-BREAKER:
            // strong ZPD signal beats the penalty, so a genuinely
            // high-information-gain anxious concept still gets picked.
            // See ADR-0003 §misconception-scope + RDY-057 spec notes
            // on affective signal never being the primary driver.
            var isAnxious = _anxiousConceptIds.Contains(conceptId);
            if (isAnxious) zpdScore *= 1.15;

            if (zpdScore < bestScore)
            {
                bestScore = zpdScore;
                bestConcept = conceptId;
                bestIsAnxious = isAnxious;
            }
        }

        if (bestConcept is not null && bestIsAnxious)
        {
            // Structured log so downstream analytics can compute "how
            // often does the anxious-signal actually alter selection?"
            // The log line is structured, not the decision — PII-safe.
            _logger.LogInformation(
                "[ANXIOUS_OPENER] session={SessionId} concept={Concept} " +
                "rationale=faded-worked-example-preferred",
                _sessionId, bestConcept);
        }

        context.Respond(new NextQuestionResponse(
            ConceptId: bestConcept,
            Methodology: _methodology,
            FatigueScore: _fatigueScore
        ));

        return Task.CompletedTask;
    }

    // ── Hint (SAI-005: DeliveryGate gating + disengagement-aware simplification) ──
    private Task HandleHint(IContext context, RequestHintMessage req)
    {
        // SAI-005: Detect cognitive state and evaluate via DeliveryGate
        var confusionState = _confusionDetector.Detect(BuildConfusionInput());
        var disengagement = _disengagementClassifier.Classify(BuildDisengagementInput());

        // Cache for reuse in explanation path
        _lastConfusionState = confusionState;
        _lastDisengagementType = disengagement;

        var deliveryContext = new DeliveryContext(
            ConfusionState: confusionState,
            DisengagementType: disengagement,
            FocusLevel: FocusLevel.Engaged, // Focus not computed per-hint; default Engaged
            IsStudentInitiated: req.IsExplicitRequest,
            QuestionsUntilPatience: Math.Max(0, 5 - _confusionWindowQuestions));

        var decision = _deliveryGate.Evaluate(deliveryContext);

        if (decision.Action != DeliveryAction.Deliver)
        {
            _logger.LogDebug("Session {SessionId}: hint {Action} — {Reason}",
                _sessionId, decision.Action, decision.Reason);
            context.Respond(new HintResponse(req.HintLevel, Delivered: false,
                SuppressedReason: decision.Reason));
            return Task.CompletedTask;
        }

        if (confusionState == ConfusionState.ConfusionStuck)
            _logger.LogInformation("Session {SessionId}: student stuck on {ConceptId}", _sessionId, req.ConceptId);
        if (disengagement == DisengagementType.Fatigued_Cognitive)
            _logger.LogDebug("Session {SessionId}: Fatigued_Cognitive — simplifying hint", _sessionId);

        // Scaffold level determines max hints allowed
        var scaffoldLevel = ScaffoldingService.DetermineLevel(
            (float)req.CurrentMastery, (float)req.PrerequisiteSatisfaction);
        var scaffoldMeta = ScaffoldingService.GetScaffoldingMetadata(scaffoldLevel);

        if (req.HintLevel > scaffoldMeta.MaxHints)
        {
            context.Respond(new HintResponse(req.HintLevel, Delivered: false, HintText: null, HasMoreHints: false));
            return Task.CompletedTask;
        }

        // SAI-002: Resolve prerequisite edges from concept graph for richer hints
        var prerequisites = _graphCache.GetPrerequisites(req.ConceptId);
        var prereqNames = req.PrerequisiteConceptNames ?? BuildPrerequisiteNames(prerequisites);
        var prereqIds = prerequisites.Select(e => e.SourceConceptId).ToList();

        // SAI-01b: Determine effective hint level (fatigue simplification)
        int effectiveHintLevel = req.HintLevel;
        if (disengagement == DisengagementType.Fatigued_Cognitive && req.HintLevel > 1)
            effectiveHintLevel = 1; // Simplify to nudge-level

        // Track difficulty for tutoring entry points
        if (req.QuestionDifficulty > 0f)
            _currentQuestionDifficulty = req.QuestionDifficulty;

        // SAI-01b: Use new HintGenerationService for language-aware templates
        var hintGenContent = _hintGenerationService.GenerateHint(new HintGenerationContext(
            HintLevel: effectiveHintLevel,
            ConceptId: req.ConceptId,
            QuestionStem: "", // Stem not passed in current message shape
            PrerequisiteConceptIds: prereqIds,
            PrerequisiteNames: prereqNames,
            DistractorRationale: req.QuestionOptions?.FirstOrDefault(o =>
                !o.IsCorrect && !string.IsNullOrEmpty(o.DistractorRationale))?.DistractorRationale,
            ScaffoldingLevel: scaffoldLevel,
            Language: _language,
            Options: req.QuestionOptions,
            QuestionExplanation: req.QuestionExplanation,
            StudentAnswer: req.StudentAnswer,
            ConceptState: req.ConceptState,
            QuestionDifficulty: req.QuestionDifficulty > 0f ? req.QuestionDifficulty : null,
            StudentMastery: (float)req.CurrentMastery));

        _hintRequestCount++;

        // Emit event for analytics
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new HintRequested_V1(
                _studentId, _sessionId, req.ConceptId, req.QuestionId, req.HintLevel)));

        context.Respond(new HintResponse(req.HintLevel, Delivered: true,
            hintGenContent.HintText, hintGenContent.HasMoreHints));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolve prerequisite concept names from graph cache edges.
    /// Sorted by Strength descending so the strongest prerequisite appears first.
    /// </summary>
    private IReadOnlyList<string> BuildPrerequisiteNames(
        IReadOnlyList<MasteryPrerequisiteEdge> edges)
    {
        if (edges.Count == 0)
            return Array.Empty<string>();

        var names = new List<string>(edges.Count);
        foreach (var edge in edges.OrderByDescending(e => e.Strength))
        {
            if (_graphCache.Concepts.TryGetValue(edge.SourceConceptId, out var node))
                names.Add(node.Name);
        }
        return names;
    }

    // ── Confusion/Disengagement Input Builders (SAI-01b) ──

    private ConfusionInput BuildConfusionInput() => new(
        WrongOnMasteredConcept: _lastWrongOnMastered,
        ResponseTimeRatio: _lastResponseTimeRatio,
        LastAnswerCorrect: _lastAnswerCorrect,
        AnswerChangedCount: _lastAnswerChangeCount,
        HintRequestedThenCancelled: _lastHintRequestedThenCancelled,
        QuestionsInConfusionWindow: _confusionWindowQuestions,
        AccuracyInConfusionWindow: _confusionWindowQuestions > 0
            ? (double)_confusionWindowCorrect / _confusionWindowQuestions : 0.5);

    private DisengagementInput BuildDisengagementInput()
    {
        var mins = (DateTimeOffset.UtcNow - _startedAt).TotalMinutes;
        return new DisengagementInput(
            RecentAccuracy: _recentAccuracies.Count > 0
                ? _recentAccuracies.TakeLast(10).Sum() / Math.Min(_recentAccuracies.Count, 10) : 0.5,
            ResponseTimeRatio: _baselineResponseTimeMs > 0 && _recentResponseTimes.Count > 0
                ? _recentResponseTimes.Last() / _baselineResponseTimeMs : 1.0,
            EngagementTrend: 0.0,
            HintRequestRate: _questionsAttempted > 0 ? (double)_hintRequestCount / _questionsAttempted : 0.0,
            AppBackgroundingRate: 0.0,
            MinutesSinceLastBreak: mins,
            TouchPatternConsistencyDelta: 0.0,
            SessionsToday: 1,
            MinutesInSession: mins,
            IsLateEvening: false);
    }

    // ── Personalized Explanation (SAI-003 / Task 03) ──
    private async Task HandlePersonalizedExplanation(
        IContext context, RequestPersonalizedExplanation req)
    {
        // Track difficulty for tutoring entry points
        if (req.QuestionDifficulty > 0f)
            _currentQuestionDifficulty = req.QuestionDifficulty;

        var scaffolding = ScaffoldingService.DetermineLevel(
            (float)req.CurrentMastery, (float)req.PrerequisiteSatisfaction);

        var explCtx = new PersonalizedExplanationContext(
            QuestionId: req.QuestionId,
            QuestionStem: req.QuestionStem,
            CorrectAnswer: req.CorrectAnswer,
            StudentAnswer: req.StudentAnswer,
            ErrorType: req.ErrorType,
            Language: _language,
            Subject: _subject,
            StaticExplanation: req.StaticExplanation,
            DistractorRationale: req.DistractorRationale,
            MasteryProbability: (float)req.CurrentMastery,
            BloomLevel: req.BloomLevel,
            Scaffolding: scaffolding,
            PrerequisiteSatisfactionIndex: (float)req.PrerequisiteSatisfaction,
            ActiveMethodology: _methodology,
            ConfusionState: _lastConfusionState,
            DisengagementType: _lastDisengagementType?.ToString(),
            BackspaceCount: req.BackspaceCount,
            AnswerChangeCount: req.AnswerChangeCount,
            HintsUsed: req.HintsUsed,
            ResponseTimeMs: req.ResponseTimeMs,
            MedianResponseTimeMs: _baselineResponseTimeMs,
            QuestionDifficulty: req.QuestionDifficulty > 0f ? req.QuestionDifficulty : null,
            StudentBudgetKey: _studentId);

        var result = await _personalizedExplanation.ResolveAsync(explCtx, CancellationToken.None);

        _logger.LogDebug(
            "Session {SessionId}: personalized explanation resolved via {Tier} for question {QuestionId}",
            _sessionId, result.Tier, req.QuestionId);

        context.Respond(new PersonalizedExplanationResponse(
            Text: result.Text,
            Tier: result.Tier,
            OutputTokens: result.OutputTokens));
    }

    // ── Annotation: triggers tutoring for confusion/question kinds (SAI-07) ──
    private Task HandleAnnotation(IContext context, AddAnnotationMessage msg)
    {
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new AnnotationAdded_V1(
                _studentId, msg.ConceptId, Guid.NewGuid().ToString("N"),
                msg.Text.GetHashCode().ToString("X8"), 0.0, msg.Kind)));

        if (msg.Kind is not ("confusion" or "question"))
            return Task.CompletedTask;

        if (_tutorPid == null)
            SpawnTutorActor(context);

        if (msg.Kind == "confusion")
        {
            context.Send(_tutorPid!, new StartTutoringFromConfusion(
                _studentId, _sessionId, msg.ConceptId, _subject, _language,
                _methodology, _currentConceptMastery, 3,
                _currentQuestionDifficulty));
        }
        else
        {
            context.Send(_tutorPid!, new StartTutoringFromQuestion(
                _studentId, _sessionId, msg.ConceptId, _subject, _language,
                _methodology, _currentConceptMastery, 3, msg.Text,
                _currentQuestionDifficulty));
        }

        return Task.CompletedTask;
    }

    // ── Tutor Message: forward to child TutorActor (SAI-07) ──
    private async Task HandleTutorMessage(IContext context, SessionTutorMessage msg)
    {
        if (_tutorPid == null)
        {
            _logger.LogWarning("Session {SessionId}: TutorMessage but no active tutor", _sessionId);
            context.Respond(new TutorResponse(0, "No active tutoring session.", true, 0));
            return;
        }

        var response = await context.RequestAsync<TutorResponse>(
            _tutorPid, new TutorMessage(msg.Message));
        context.Respond(response);

        if (response.IsComplete)
        {
            context.Stop(_tutorPid);
            _tutorPid = null;
        }
    }

    // ── Delegate events from TutorActor child up to StudentActor parent ──
    private Task HandleDelegateFromChild(IContext context, DelegateEvent del)
    {
        if (context.Parent != null)
            context.Send(context.Parent, del);
        return Task.CompletedTask;
    }

    private void SpawnTutorActor(IContext context)
    {
        if (_tutorPid != null) return;
        var props = Props.FromProducer(() => _tutorFactory());
        _tutorPid = context.Spawn(props);
        _logger.LogDebug("Session {SessionId}: TutorActor spawned at {Pid}", _sessionId, _tutorPid);
    }

    // ── Skip ──
    private Task HandleSkip(IContext context, SkipQuestionMessage req)
    {
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new QuestionSkipped_V1(
                _studentId, _sessionId, req.ConceptId, req.QuestionId, req.TimeSpentMs)));

        return Task.CompletedTask;
    }

    // ── Get Session Snapshot ──
    // PWA-BE-001: Return a non-mutating snapshot of the current session state.
    private Task HandleGetSessionSnapshot(IContext context, GetSessionSnapshot req)
    {
        if (_sessionId != req.SessionId)
        {
            context.Respond(new SessionSnapshotResponse(
                req.SessionId, 0, null, new(), "full", new(),
                DateTimeOffset.UtcNow, 0, Error: "session_not_found"));
            return Task.CompletedTask;
        }

        var durationSeconds = (int)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
        var currentStepNumber = _questionsAttempted;
        if (_currentConceptId != "") currentStepNumber++;

        context.Respond(new SessionSnapshotResponse(
            _sessionId,
            currentStepNumber,
            null, // current question id not tracked in runtime actor
            new Dictionary<string, SkillMasteryDto>(),
            "exploratory",
            new List<StepResultDto>(),
            _startedAt,
            durationSeconds));

        return Task.CompletedTask;
    }

    // ── Resume Session ──
    // SES-002.2: Restore actor state from the last checkpoint persisted in the
    // TutoringSessionDocument / StudentProfileSnapshot. Because the actor is being
    // re-initialised after an interruption, we rebuild running counters from the
    // Marten document that was already loaded by the parent StudentActor, so we
    // only need the fields that arrived in the ResumeSessionRequest.
    private Task HandleResumeSession(IContext context, ResumeSessionRequest req)
    {
        _sessionId  = req.SessionId;
        _studentId  = req.StudentId;
        _subject    = req.Subject;
        _methodology = req.Methodology;
        _startedAt  = req.OriginalStartedAt;
        _questionsAttempted = req.QuestionsAttempted;
        _questionsCorrect   = req.QuestionsCorrect;
        _fatigueScore       = req.FatigueScoreAtCheckpoint;

        _logger.LogInformation(
            "Session {SessionId} resumed for {StudentId}, questions so far={Questions}, fatigue={Fatigue:F2}",
            _sessionId, _studentId, _questionsAttempted, _fatigueScore);

        context.Respond(new ResumeSessionResponse(
            SessionId: _sessionId,
            Subject: _subject,
            Methodology: _methodology,
            QuestionsAttempted: _questionsAttempted,
            FatigueScore: _fatigueScore,
            ResumedAt: DateTimeOffset.UtcNow));

        return Task.CompletedTask;
    }

    // ── End Session ──
    private Task HandleEndSession(IContext context)
    {
        // SAI-07: End active tutoring session if any
        if (_tutorPid != null)
        {
            context.Send(_tutorPid, new EndTutoring());
            _tutorPid = null;
        }

        var duration = (int)(DateTimeOffset.UtcNow - _startedAt).TotalMinutes;
        var avgRt = _recentResponseTimes.Count > 0
            ? _recentResponseTimes.Average()
            : 0;

        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new SessionEnded_V1(
                _studentId, _sessionId, "completed", duration,
                _questionsAttempted, _questionsCorrect, avgRt, _fatigueScore)));

        _logger.LogInformation(
            "Session {SessionId} ended: {Questions} questions, {Correct} correct, fatigue={Fatigue:F2}",
            _sessionId, _questionsAttempted, _questionsCorrect, _fatigueScore);

        return Task.CompletedTask;
    }

    private Task HandleStopping(IContext context)
    {
        _activitySource.Dispose();
        _logger.LogDebug("LearningSessionActor stopping for session {SessionId}", _sessionId);
        return Task.CompletedTask;
    }
}

// ── Session Messages ──

public record InitSession(
    string SessionId, string StudentId, string Subject, string Methodology,
    double BaselineAccuracy, double BaselineResponseTimeMs,
    string? Language = "he",
    // RDY-057b: anxious concepts come from the student's self-assessment.
    // Passed in at session-init so the opener can bias toward faded
    // worked-examples for topics the student self-reported as anxious,
    // without LearningSessionActor reaching across aggregates to load
    // a document at pick-question time. Null when self-assessment was
    // skipped or doesn't exist — opener falls back to the cold-start
    // path.
    IReadOnlyCollection<string>? AnxiousConceptIds = null);

public record EvaluateAnswerRequest(
    string ConceptId, string QuestionId, string QuestionType,
    bool IsCorrect, double ResponseTimeMs, string ErrorType,
    double PriorMastery, Services.BktParameters BktParameters,
    int HintCountUsed, string AnswerHash,
    int BackspaceCount, int AnswerChangeCount);

public record SessionEvaluationResult(
    bool IsCorrect, double PosteriorMastery, bool CrossedProgressionThreshold,
    double FatigueScore, bool ShouldEndSession, string ErrorType,
    // SAI-005: Explanation delivery gating
    bool SuppressExplanation = false, string? ExplanationSuppressedReason = null);

public record RequestNextQuestion(
    Dictionary<string, double> MasteryMap,
    HashSet<string> ReviewDueConcepts);

public record NextQuestionResponse(string? ConceptId, string Methodology, double FatigueScore);
public record RequestHintMessage(
    string ConceptId,
    string QuestionId,
    int HintLevel,
    double CurrentMastery = 0.5,
    double PrerequisiteSatisfaction = 1.0,
    IReadOnlyList<string>? PrerequisiteConceptNames = null,
    IReadOnlyList<Cena.Actors.Questions.QuestionOptionState>? QuestionOptions = null,
    string? QuestionExplanation = null,
    string? StudentAnswer = null,
    Cena.Actors.Mastery.ConceptMasteryState? ConceptState = null,
    bool IsExplicitRequest = true,
    float QuestionDifficulty = 0f);

public record HintResponse(int HintLevel, bool Delivered, string? HintText = null,
    bool HasMoreHints = false, string? SuppressedReason = null);
public record SkipQuestionMessage(string ConceptId, string QuestionId, int TimeSpentMs);
public record EndSessionRequest;

// SES-002.2: Resume an interrupted or paused session.
// The parent StudentActor loads these values from the last persisted
// TutoringSessionDocument snapshot before forwarding this message.
public record ResumeSessionRequest(
    string SessionId,
    string StudentId,
    string Subject,
    string Methodology,
    DateTimeOffset OriginalStartedAt,
    int QuestionsAttempted,
    int QuestionsCorrect,
    double FatigueScoreAtCheckpoint);

public record ResumeSessionResponse(
    string SessionId,
    string Subject,
    string Methodology,
    int QuestionsAttempted,
    double FatigueScore,
    DateTimeOffset ResumedAt);

// ── SAI-003: Personalized Explanation Messages ──

public record RequestPersonalizedExplanation(
    string QuestionId,
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    ExplanationErrorType ErrorType,
    string? StaticExplanation,
    string? DistractorRationale,
    double CurrentMastery = 0.5,
    double PrerequisiteSatisfaction = 1.0,
    int BloomLevel = 3,
    int BackspaceCount = 0,
    int AnswerChangeCount = 0,
    int HintsUsed = 0,
    int ResponseTimeMs = 0,
    float QuestionDifficulty = 0f);

public record PersonalizedExplanationResponse(
    string Text,
    string Tier,      // "L3", "L2", "L1", "generic"
    int OutputTokens);

/// <summary>Wrapper for events delegated from child actors to parent StudentActor.</summary>
// Fully-qualify to avoid collision with the sibling Cena.Actors.Sessions.Events
// sub-namespace that contains per-session event types (e.g. SessionStarted_V2).
public record DelegateEvent(Cena.Actors.Events.IDelegatedEvent Event);

// ── Task 07 stubs (TutorActor entry points) ──

/// <summary>Student annotation (confusion/question/note/insight) forwarded from StudentActor.</summary>
public record AddAnnotationMessage(string ConceptId, string Text, string Kind);

/// <summary>Student message within an active tutoring conversation.</summary>
public record SessionTutorMessage(string Message);
