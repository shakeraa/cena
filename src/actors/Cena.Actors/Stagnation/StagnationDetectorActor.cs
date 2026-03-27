// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — StagnationDetectorActor (Classic, Timer-Based)
// Monitors 5 signals across sessions to detect learning plateaus.
// Triggers methodology switch when composite score > 0.7 for 3 sessions.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Stagnation;

public sealed class StagnationDetectorActor : IActor
{
    private readonly ILogger<StagnationDetectorActor> _logger;

    // ── Per-concept sliding window state ──
    private readonly Dictionary<string, ConceptStagnationWindow> _windows = new();

    // ── Adaptive threshold (per-student, updated from historical data) ──
    private double _studentAvgImprovementRate = 0.05;

    // ── Weights (from intelligence-layer.md Flywheel 5) ──
    private const double W_AccuracyPlateau = 0.30;
    private const double W_ResponseTimeDrift = 0.20;
    private const double W_SessionAbandonment = 0.20;
    private const double W_ErrorRepetition = 0.20;
    private const double W_AnnotationSentiment = 0.10;
    private const double StagnationThreshold = 0.7;
    private const int ConsecutiveSessionsRequired = 3;
    private const int CooldownSessions = 3;

    // ── Telemetry ──
    private static readonly Meter Meter = new("Cena.Actors.Stagnation", "1.0.0");
    private static readonly Histogram<double> ScoreHistogram =
        Meter.CreateHistogram<double>("cena.stagnation.composite_score");

    public StagnationDetectorActor(ILogger<StagnationDetectorActor> logger)
    {
        _logger = logger;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            UpdateStagnationSignals msg => HandleUpdateSignals(context, msg),
            CheckStagnation msg => HandleCheckStagnation(context, msg),
            ResetAfterSwitch msg => HandleResetAfterSwitch(msg),
            UpdateAdaptiveThreshold msg => HandleUpdateThreshold(msg),
            _ => Task.CompletedTask
        };
    }

    private Task HandleUpdateSignals(IContext context, UpdateStagnationSignals msg)
    {
        var window = GetOrCreateWindow(msg.ConceptCluster);

        window.SessionSignals.Add(new SessionSignalSnapshot(
            Accuracy: msg.SessionAccuracy,
            AvgResponseTimeMs: msg.AvgResponseTimeMs,
            SessionDurationMinutes: msg.SessionDurationMinutes,
            ErrorRepeatCount: msg.ErrorRepeatCount,
            AnnotationSentiment: msg.AnnotationSentiment,
            Timestamp: DateTimeOffset.UtcNow
        ));

        // Keep only last 5 sessions (sliding window)
        while (window.SessionSignals.Count > 5)
            window.SessionSignals.RemoveAt(0);

        return Task.CompletedTask;
    }

    private Task HandleCheckStagnation(IContext context, CheckStagnation msg)
    {
        var window = GetOrCreateWindow(msg.ConceptCluster);

        if (window.CooldownRemaining > 0)
        {
            window.CooldownRemaining--;
            context.Respond(new StagnationCheckResult(
                IsStagnant: false, CompositeScore: 0,
                ConceptCluster: msg.ConceptCluster,
                Reason: $"Cooldown active ({window.CooldownRemaining} sessions remaining)"));
            return Task.CompletedTask;
        }

        if (window.SessionSignals.Count < 2)
        {
            context.Respond(new StagnationCheckResult(
                IsStagnant: false, CompositeScore: 0,
                ConceptCluster: msg.ConceptCluster,
                Reason: "Insufficient data (need at least 2 sessions)"));
            return Task.CompletedTask;
        }

        double compositeScore = ComputeCompositeScore(window);
        ScoreHistogram.Record(compositeScore);

        if (compositeScore > StagnationThreshold)
        {
            window.ConsecutiveStagnantSessions++;
        }
        else
        {
            window.ConsecutiveStagnantSessions = 0;
        }

        bool isStagnant = window.ConsecutiveStagnantSessions >= ConsecutiveSessionsRequired;

        if (isStagnant)
        {
            _logger.LogInformation(
                "Stagnation detected for cluster {Cluster}: score={Score:F3}, consecutive={Count}",
                msg.ConceptCluster, compositeScore, window.ConsecutiveStagnantSessions);
        }

        context.Respond(new StagnationCheckResult(
            IsStagnant: isStagnant,
            CompositeScore: compositeScore,
            ConceptCluster: msg.ConceptCluster,
            Reason: isStagnant
                ? $"Stagnation: score {compositeScore:F3} > {StagnationThreshold} for {window.ConsecutiveStagnantSessions} consecutive sessions"
                : $"Score {compositeScore:F3} (threshold: {StagnationThreshold}, consecutive: {window.ConsecutiveStagnantSessions}/{ConsecutiveSessionsRequired})"
        ));

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════
    // REAL 5-signal composite score with normalization formulas
    // ═══════════════════════════════════════════════════════════════════

    private double ComputeCompositeScore(ConceptStagnationWindow window)
    {
        var signals = window.SessionSignals;
        if (signals.Count < 2) return 0;

        var latest = signals[^1];
        var previous = signals[^2];

        // ── Signal 1: Accuracy Plateau (sigmoid) ──
        double improvementRate = previous.Accuracy > 0.01
            ? (latest.Accuracy - previous.Accuracy) / previous.Accuracy
            : 0;
        double adaptiveThreshold = Math.Max(0.02, _studentAvgImprovementRate * 0.5);
        double accuracyPlateau = Sigmoid(10.0 * (adaptiveThreshold - improvementRate));

        // ── Signal 2: Response Time Drift (linear) ──
        double baselineRt = signals.Count >= 3
            ? signals.Take(signals.Count - 1).Average(s => s.AvgResponseTimeMs)
            : signals[0].AvgResponseTimeMs;
        double rtDrift = baselineRt > 0.01
            ? Math.Clamp(Math.Max(0, (latest.AvgResponseTimeMs - baselineRt) / baselineRt) / 0.4, 0, 1)
            : 0;

        // ── Signal 3: Session Abandonment (linear) ──
        double avgDuration = signals.Average(s => s.SessionDurationMinutes);
        double abandonment = avgDuration > 0.01
            ? Math.Clamp(Math.Max(0, (avgDuration - latest.SessionDurationMinutes) / avgDuration) / 0.6, 0, 1)
            : 0;

        // ── Signal 4: Error Type Repetition (linear) ──
        double errorRepetition = Math.Clamp(latest.ErrorRepeatCount / 5.0, 0, 1);

        // ── Signal 5: Annotation Sentiment (inverted) ──
        double sentiment = 1.0 - Math.Clamp(latest.AnnotationSentiment, 0, 1);

        // ── Weighted composite ──
        double composite =
            W_AccuracyPlateau * accuracyPlateau +
            W_ResponseTimeDrift * rtDrift +
            W_SessionAbandonment * abandonment +
            W_ErrorRepetition * errorRepetition +
            W_AnnotationSentiment * sentiment;

        return Math.Clamp(composite, 0, 1);
    }

    private static double Sigmoid(double x)
    {
        return 1.0 / (1.0 + Math.Exp(-x));
    }

    private Task HandleResetAfterSwitch(ResetAfterSwitch msg)
    {
        if (_windows.TryGetValue(msg.ConceptCluster, out var window))
        {
            window.ConsecutiveStagnantSessions = 0;
            window.CooldownRemaining = CooldownSessions;
            _logger.LogInformation(
                "Stagnation reset for {Cluster} after methodology switch, cooldown={Cooldown}",
                msg.ConceptCluster, CooldownSessions);
        }
        return Task.CompletedTask;
    }

    private Task HandleUpdateThreshold(UpdateAdaptiveThreshold msg)
    {
        _studentAvgImprovementRate = Math.Clamp(msg.AvgImprovementRate, 0.01, 0.20);
        return Task.CompletedTask;
    }

    private ConceptStagnationWindow GetOrCreateWindow(string conceptCluster)
    {
        if (!_windows.TryGetValue(conceptCluster, out var window))
        {
            window = new ConceptStagnationWindow();
            _windows[conceptCluster] = window;
        }
        return window;
    }
}

// ── State ──

internal sealed class ConceptStagnationWindow
{
    public List<SessionSignalSnapshot> SessionSignals { get; } = new(5);
    public int ConsecutiveStagnantSessions { get; set; }
    public int CooldownRemaining { get; set; }
}

internal sealed record SessionSignalSnapshot(
    double Accuracy,
    double AvgResponseTimeMs,
    double SessionDurationMinutes,
    int ErrorRepeatCount,
    double AnnotationSentiment,
    DateTimeOffset Timestamp);

// ── Messages ──

public record UpdateStagnationSignals(
    string ConceptCluster, double SessionAccuracy, double AvgResponseTimeMs,
    double SessionDurationMinutes, int ErrorRepeatCount, double AnnotationSentiment);

public record CheckStagnation(string ConceptCluster);

public record StagnationCheckResult(
    bool IsStagnant, double CompositeScore, string ConceptCluster, string Reason);

public record ResetAfterSwitch(string ConceptCluster);
public record UpdateAdaptiveThreshold(double AvgImprovementRate);
