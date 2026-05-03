// =============================================================================
// Cena Platform — Flow State Service (RDY-034)
//
// Pure domain service that maps four session signals to the 5-state flow
// machine shared with the student PWA useFlowState composable. Consumes
// ICognitiveLoadService for cooldown + difficulty-adjustment advice so there
// is exactly one cognitive-load authority in the stack.
//
// Design notes:
//   • Stateless. No per-session memory here — the transition-logging hook
//     fires on every call with the full inputs + state, which downstream
//     log analytics collapse into transitions.
//   • Thresholds match the frontend composable exactly (useFlowState.ts).
//     The frontend keeps a local copy so offline/cached UI can still render
//     an approximate state when the API is unreachable.
//   • NO random, NO stub. Every branch is deterministic on the inputs.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Canonical flow-state categories. Matches the TypeScript union type in
/// <c>src/student/full-version/src/composables/useFlowState.ts</c>.
/// </summary>
public enum FlowStateKind
{
    Warming = 0,
    Approaching = 1,
    InFlow = 2,
    Disrupted = 3,
    Fatigued = 4,
}

/// <summary>
/// Recommended client-side action. Narrow enum keeps clients type-safe.
/// </summary>
public enum FlowStateAction
{
    /// <summary>No signal — keep the session rolling.</summary>
    Continue,
    /// <summary>Mild negative signal — pace the next question, no UI intrusion.</summary>
    SlowDown,
    /// <summary>Accuracy dropping — ease difficulty on the next question.</summary>
    ReduceDifficulty,
    /// <summary>High fatigue / long session — surface the break suggestion.</summary>
    SuggestBreak,
}

/// <summary>
/// Full flow-state assessment. Includes pass-through inputs so callers can
/// log the snapshot without re-wiring it.
/// </summary>
public sealed record FlowStateAssessment(
    FlowStateKind State,
    double FatigueLevel,
    double AccuracyTrend,
    int ConsecutiveCorrect,
    double SessionDurationMinutes,
    FlowStateAction RecommendedAction,
    int? CooldownMinutes,
    DifficultyAdjustment? DifficultyAdjustmentAdvice);

/// <summary>
/// Flow state service — sole authority for the backend flow-state machine.
/// </summary>
public interface IFlowStateService
{
    /// <summary>
    /// Compute a flow-state assessment from the current session signals.
    /// </summary>
    /// <param name="fatigueLevel">
    ///     Fatigue score in [0,1] (clamped defensively). Typically the
    ///     <see cref="FatigueAssessment.FatigueScore"/> from
    ///     <see cref="ICognitiveLoadService.ComputeFatigue"/>.
    /// </param>
    /// <param name="accuracyTrend">
    ///     Accuracy trend in [-1,1] (clamped defensively).
    ///     Negative = declining, positive = improving.
    /// </param>
    /// <param name="consecutiveCorrect">
    ///     Streak of correct answers ending at now (clamped to &gt;= 0).
    /// </param>
    /// <param name="sessionDurationMinutes">
    ///     Minutes since session start (clamped to &gt;= 0).
    /// </param>
    /// <param name="currentDifficulty">
    ///     Optional current difficulty (1-10). When supplied the response
    ///     carries a <see cref="DifficultyAdjustment"/> recommendation.
    /// </param>
    FlowStateAssessment Assess(
        double fatigueLevel,
        double accuracyTrend,
        int consecutiveCorrect,
        double sessionDurationMinutes,
        int? currentDifficulty = null);
}

/// <summary>
/// RDY-034: production flow-state service. Pure, stateless, deterministic.
/// Thresholds mirror the Vue composable in
/// src/student/full-version/src/composables/useFlowState.ts — keep in sync.
/// </summary>
public sealed class FlowStateService : IFlowStateService
{
    // ── Thresholds (ADR-0032-era flow model) ────────────────────────────
    internal const double HighFatigueThreshold = 0.7;
    internal const double MaxSessionMinutes    = 45.0;
    internal const double DisruptedTrendFloor  = -0.3;
    internal const int    InFlowStreakFloor    = 3;
    internal const double InFlowTrendFloor     = 0.1;
    internal const double InFlowFatigueCeiling = 0.4;

    // Secondary thresholds for finer RecommendedAction shaping.
    internal const double SlowDownFatigueFloor = 0.5;

    private readonly ICognitiveLoadService _cognitiveLoad;
    private readonly ILogger<FlowStateService> _logger;

    public FlowStateService(
        ICognitiveLoadService cognitiveLoad,
        ILogger<FlowStateService> logger)
    {
        _cognitiveLoad = cognitiveLoad
            ?? throw new ArgumentNullException(nameof(cognitiveLoad));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public FlowStateAssessment Assess(
        double fatigueLevel,
        double accuracyTrend,
        int consecutiveCorrect,
        double sessionDurationMinutes,
        int? currentDifficulty = null)
    {
        // Defensive clamps — never trust callers. Also allows the endpoint
        // to forward raw client input without pre-validation.
        var fatigue    = Math.Clamp(fatigueLevel, 0.0, 1.0);
        var trend      = Math.Clamp(accuracyTrend, -1.0, 1.0);
        var streak     = Math.Max(0, consecutiveCorrect);
        var sessionMin = Math.Max(0.0, sessionDurationMinutes);

        var state = ClassifyState(fatigue, trend, streak, sessionMin);

        // Cooldown: only meaningful when Fatigued. ICognitiveLoadService owns
        // the formula so flow state never encodes cooldown math locally.
        int? cooldown = state == FlowStateKind.Fatigued
            ? _cognitiveLoad.ComputeCooldownMinutes(fatigue)
            : null;

        // Difficulty advice: only when caller provides currentDifficulty.
        DifficultyAdjustment? diffAdjust = currentDifficulty.HasValue
            ? _cognitiveLoad.RecommendDifficultyAdjustment(fatigue, currentDifficulty.Value)
            : null;

        var action = RecommendAction(state, fatigue, trend);

        // Structured log. Collapsed by log analytics into transition chains —
        // no per-call session memory required server-side.
        _logger.LogInformation(
            "[FLOW_STATE] state={State} fatigue={Fatigue:F2} trend={Trend:F2} streak={Streak} duration_min={DurationMin:F1} action={Action} cooldown_min={Cooldown} difficulty_adjust={DiffAdjust}",
            state,
            fatigue,
            trend,
            streak,
            sessionMin,
            action,
            cooldown.HasValue ? cooldown.Value.ToString() : "null",
            diffAdjust.HasValue ? diffAdjust.Value.ToString() : "null");

        return new FlowStateAssessment(
            State: state,
            FatigueLevel: fatigue,
            AccuracyTrend: trend,
            ConsecutiveCorrect: streak,
            SessionDurationMinutes: sessionMin,
            RecommendedAction: action,
            CooldownMinutes: cooldown,
            DifficultyAdjustmentAdvice: diffAdjust);
    }

    // ── State machine ────────────────────────────────────────────────────
    // Evaluated top-down; first match wins. Matches useFlowState.ts exactly.

    private static FlowStateKind ClassifyState(
        double fatigue,
        double trend,
        int streak,
        double sessionMin)
    {
        // Fatigued: high fatigue OR very long session.
        if (fatigue > HighFatigueThreshold || sessionMin > MaxSessionMinutes)
            return FlowStateKind.Fatigued;

        // Disrupted: declining accuracy trend (independent of streak).
        if (trend < DisruptedTrendFloor)
            return FlowStateKind.Disrupted;

        // InFlow: streak + rising trend + low fatigue all together.
        if (streak >= InFlowStreakFloor
            && trend > InFlowTrendFloor
            && fatigue < InFlowFatigueCeiling)
            return FlowStateKind.InFlow;

        // Approaching: building momentum.
        if (streak >= 1 && trend >= 0)
            return FlowStateKind.Approaching;

        // Default warm-up.
        return FlowStateKind.Warming;
    }

    // ── Action mapping ───────────────────────────────────────────────────
    // State → primary action, with fatigue/trend tuning for ambiguous cases.

    private static FlowStateAction RecommendAction(
        FlowStateKind state,
        double fatigue,
        double trend)
    {
        return state switch
        {
            FlowStateKind.Fatigued    => FlowStateAction.SuggestBreak,
            FlowStateKind.Disrupted   => FlowStateAction.ReduceDifficulty,
            FlowStateKind.InFlow      => FlowStateAction.Continue,
            // Approaching / Warming: nudge only when secondary signals show
            // borderline fatigue or a mild negative trend.
            _ when fatigue >= SlowDownFatigueFloor => FlowStateAction.SlowDown,
            _ when trend < 0                       => FlowStateAction.SlowDown,
            _                                      => FlowStateAction.Continue,
        };
    }
}
