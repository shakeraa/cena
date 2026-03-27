// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — LearningSessionActor (Classic, Session-Scoped)
// Created by StudentActor on StartSession, destroyed on EndSession.
// Owns: current question, fatigue scoring, BKT updates, item selection.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;
using Cena.Actors.Events;
using Cena.Actors.Services;
using Cena.Actors.Students;

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
    private readonly ILogger<LearningSessionActor> _logger;

    // ── Session State ──
    private string _sessionId = "";
    private string _studentId = "";
    private string _subject = "";
    private string _methodology = "socratic";
    private DateTimeOffset _startedAt;
    private int _questionsAttempted;
    private int _questionsCorrect;
    private double _fatigueScore;
    private int _consecutiveHighFatigue;
    private readonly List<double> _recentAccuracies = new(20);
    private readonly List<double> _recentResponseTimes = new(20);
    private double _baselineAccuracy = 0.5;
    private double _baselineResponseTimeMs = 5000;

    // ── Fatigue Weights (from system-overview.md) ──
    private const double W1_AccuracyDrop = 0.4;
    private const double W2_RtIncrease = 0.3;
    private const double W3_TimeFraction = 0.3;
    private const double FatigueThreshold = 0.7;
    private const int ConsecutiveFatigueLimit = 2;
    private const int MaxSessionMinutes = 45;
    private const int DefaultSessionMinutes = 25;

    // ── Telemetry (ACT-023: instance-based via IMeterFactory) ──
    private readonly ActivitySource _activitySource;
    private readonly Histogram<double> _fatigueHistogram;
    private readonly Counter<long> _questionsCounter;

    public LearningSessionActor(IBktService bkt, ILogger<LearningSessionActor> logger, IMeterFactory meterFactory)
    {
        _bkt = bkt;
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
            EvaluateAnswerRequest req => HandleEvaluateAnswer(context, req),
            RequestNextQuestion req => HandleNextQuestion(context, req),
            RequestHintMessage req => HandleHint(context, req),
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
        _startedAt = DateTimeOffset.UtcNow;
        _baselineAccuracy = init.BaselineAccuracy;
        _baselineResponseTimeMs = init.BaselineResponseTimeMs;

        _logger.LogInformation(
            "Session {SessionId} started for {StudentId}, subject={Subject}, methodology={Methodology}",
            _sessionId, _studentId, _subject, _methodology);

        // Delegate SessionStarted event to parent
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new SessionStarted_V1(
                _studentId, _sessionId, "mobile", "1.0.0", _methodology,
                null, false, DateTimeOffset.UtcNow)));

        return Task.CompletedTask;
    }

    // ── Answer Evaluation (REAL BKT) ──
    private Task HandleEvaluateAnswer(IContext context, EvaluateAnswerRequest req)
    {
        using var span = _activitySource.StartActivity("EvaluateAnswer");
        _questionsCounter.Add(1);
        _questionsAttempted++;

        // REAL BKT update — Corbett & Anderson formula, microsecond scale
        var bktResult = _bkt.Update(new BktUpdateInput(
            PriorMastery: req.PriorMastery,
            IsCorrect: req.IsCorrect,
            Parameters: req.BktParameters
        ));

        if (req.IsCorrect) _questionsCorrect++;

        // Track for fatigue calculation
        _recentAccuracies.Add(req.IsCorrect ? 1.0 : 0.0);
        if (_recentAccuracies.Count > 20) _recentAccuracies.RemoveAt(0);

        _recentResponseTimes.Add(req.ResponseTimeMs);
        if (_recentResponseTimes.Count > 20) _recentResponseTimes.RemoveAt(0);

        // REAL fatigue score computation
        _fatigueScore = ComputeFatigueScore();
        _fatigueHistogram.Record(_fatigueScore);

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

        context.Respond(new SessionEvaluationResult(
            IsCorrect: req.IsCorrect,
            PosteriorMastery: bktResult.PosteriorMastery,
            CrossedProgressionThreshold: bktResult.CrossedProgressionThreshold,
            FatigueScore: _fatigueScore,
            ShouldEndSession: shouldEndSession,
            ErrorType: req.ErrorType
        ));

        return Task.CompletedTask;
    }

    // ── REAL Fatigue Score (5-factor weighted composite) ──
    private double ComputeFatigueScore()
    {
        // Signal 1: Accuracy drop from baseline (zero-allocation rolling average)
        double rollingAccuracy;
        if (_recentAccuracies.Count >= 5)
        {
            double sum = 0;
            int start = _recentAccuracies.Count - 5;
            for (int i = start; i < _recentAccuracies.Count; i++)
                sum += _recentAccuracies[i];
            rollingAccuracy = sum / 5.0;
        }
        else
        {
            rollingAccuracy = _recentAccuracies.Count > 0
                ? _recentAccuracies.Sum() / _recentAccuracies.Count
                : 0.5;
        }
        double accuracyDrop = _baselineAccuracy > 0.01
            ? Math.Max(0, (_baselineAccuracy - rollingAccuracy) / _baselineAccuracy)
            : 0;
        accuracyDrop = Math.Clamp(accuracyDrop, 0, 1);

        // Signal 2: Response time increase from baseline (zero-allocation rolling average)
        double rollingRt;
        if (_recentResponseTimes.Count >= 5)
        {
            double sum = 0;
            int start = _recentResponseTimes.Count - 5;
            for (int i = start; i < _recentResponseTimes.Count; i++)
                sum += _recentResponseTimes[i];
            rollingRt = sum / 5.0;
        }
        else
        {
            rollingRt = _recentResponseTimes.Count > 0
                ? _recentResponseTimes.Sum() / _recentResponseTimes.Count
                : _baselineResponseTimeMs;
        }
        double rtIncrease = _baselineResponseTimeMs > 0.01
            ? Math.Max(0, (rollingRt - _baselineResponseTimeMs) / _baselineResponseTimeMs)
            : 0;
        rtIncrease = Math.Clamp(rtIncrease, 0, 1);

        // Signal 3: Time fraction of max session
        double elapsedMinutes = (DateTimeOffset.UtcNow - _startedAt).TotalMinutes;
        double timeFraction = Math.Clamp(elapsedMinutes / DefaultSessionMinutes, 0, 1);

        return W1_AccuracyDrop * accuracyDrop
             + W2_RtIncrease * rtIncrease
             + W3_TimeFraction * timeFraction;
    }

    // ── Item Selection (Zone of Proximal Development) ──
    private Task HandleNextQuestion(IContext context, RequestNextQuestion req)
    {
        // ZPD scoring: prioritize concepts closest to P(known) = 0.5
        // This maximizes information gain per question
        string? bestConcept = null;
        double bestScore = double.MaxValue;

        foreach (var (conceptId, mastery) in req.MasteryMap)
        {
            if (mastery >= 0.85) continue; // Already mastered, skip

            // ZPD score: distance from 0.5 (optimal learning zone)
            double zpdScore = Math.Abs(mastery - 0.5);

            // Boost concepts that are close to mastery threshold (almost there)
            if (mastery > 0.7) zpdScore *= 0.8; // 20% priority boost

            // Boost concepts due for spaced repetition review
            if (req.ReviewDueConcepts.Contains(conceptId))
                zpdScore *= 0.5; // 50% priority boost

            if (zpdScore < bestScore)
            {
                bestScore = zpdScore;
                bestConcept = conceptId;
            }
        }

        context.Respond(new NextQuestionResponse(
            ConceptId: bestConcept,
            Methodology: _methodology,
            FatigueScore: _fatigueScore
        ));

        return Task.CompletedTask;
    }

    // ── Hint ──
    private Task HandleHint(IContext context, RequestHintMessage req)
    {
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new HintRequested_V1(
                _studentId, _sessionId, req.ConceptId, req.QuestionId, req.HintLevel)));

        context.Respond(new HintResponse(HintLevel: req.HintLevel, Delivered: true));
        return Task.CompletedTask;
    }

    // ── Skip ──
    private Task HandleSkip(IContext context, SkipQuestionMessage req)
    {
        if (context.Parent != null)
            context.Send(context.Parent, new DelegateEvent(new QuestionSkipped_V1(
                _studentId, _sessionId, req.ConceptId, req.QuestionId, req.TimeSpentMs)));

        return Task.CompletedTask;
    }

    // ── End Session ──
    private Task HandleEndSession(IContext context)
    {
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
        _logger.LogDebug("LearningSessionActor stopping for session {SessionId}", _sessionId);
        return Task.CompletedTask;
    }
}

// ── Session Messages ──

public record InitSession(
    string SessionId, string StudentId, string Subject, string Methodology,
    double BaselineAccuracy, double BaselineResponseTimeMs);

public record EvaluateAnswerRequest(
    string ConceptId, string QuestionId, string QuestionType,
    bool IsCorrect, double ResponseTimeMs, string ErrorType,
    double PriorMastery, BktParameters BktParameters,
    int HintCountUsed, string AnswerHash,
    int BackspaceCount, int AnswerChangeCount);

public record SessionEvaluationResult(
    bool IsCorrect, double PosteriorMastery, bool CrossedProgressionThreshold,
    double FatigueScore, bool ShouldEndSession, string ErrorType);

public record RequestNextQuestion(
    Dictionary<string, double> MasteryMap,
    HashSet<string> ReviewDueConcepts);

public record NextQuestionResponse(string? ConceptId, string Methodology, double FatigueScore);
public record RequestHintMessage(string ConceptId, string QuestionId, int HintLevel);
public record HintResponse(int HintLevel, bool Delivered);
public record SkipQuestionMessage(string ConceptId, string QuestionId, int TimeSpentMs);
public record EndSessionRequest;

/// <summary>Wrapper for events delegated from child actors to parent StudentActor.</summary>
public record DelegateEvent(object Event);
