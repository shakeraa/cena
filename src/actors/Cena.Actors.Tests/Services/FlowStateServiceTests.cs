// =============================================================================
// Cena Platform — FlowStateService unit tests (RDY-034)
//
// Covers the five flow-state branches, the action mapping, the cognitive-load
// delegations (cooldown + difficulty adjustment), defensive clamping, and the
// [FLOW_STATE] structured log emission.
//
// Tests drive a real CognitiveLoadService (no NSubstitute) because the
// cooldown + difficulty-adjustment formulas are small, deterministic, and
// central to the contract. Swapping them for stubs would hide regressions
// in the live rules. Logs are captured via ITestOutputHelper-backed
// ListLogger.
// =============================================================================

using System.Collections.Generic;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Services;

public sealed class FlowStateServiceTests
{
    // ── State-classification branches ────────────────────────────────────

    [Fact]
    public void Assess_Defaults_ClassifiesWarming()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.0,
            accuracyTrend: 0.0,
            consecutiveCorrect: 0,
            sessionDurationMinutes: 0.0);

        Assert.Equal(FlowStateKind.Warming, result.State);
        Assert.Equal(FlowStateAction.Continue, result.RecommendedAction);
        Assert.Null(result.CooldownMinutes);
        Assert.Null(result.DifficultyAdjustmentAdvice);
    }

    [Fact]
    public void Assess_StreakAndRisingTrend_ClassifiesApproaching()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.2,
            accuracyTrend: 0.05,
            consecutiveCorrect: 2,
            sessionDurationMinutes: 3.0);

        Assert.Equal(FlowStateKind.Approaching, result.State);
    }

    [Fact]
    public void Assess_StrongStreakPlusRisingTrendLowFatigue_ClassifiesInFlow()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.2,
            accuracyTrend: 0.25,
            consecutiveCorrect: 4,
            sessionDurationMinutes: 10.0);

        Assert.Equal(FlowStateKind.InFlow, result.State);
        Assert.Equal(FlowStateAction.Continue, result.RecommendedAction);
    }

    [Fact]
    public void Assess_SteepNegativeTrend_ClassifiesDisrupted()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.3,
            accuracyTrend: -0.6,
            consecutiveCorrect: 0,
            sessionDurationMinutes: 12.0);

        Assert.Equal(FlowStateKind.Disrupted, result.State);
        Assert.Equal(FlowStateAction.ReduceDifficulty, result.RecommendedAction);
    }

    [Fact]
    public void Assess_HighFatigue_ClassifiesFatigued()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.8,
            accuracyTrend: 0.0,
            consecutiveCorrect: 0,
            sessionDurationMinutes: 20.0);

        Assert.Equal(FlowStateKind.Fatigued, result.State);
        Assert.Equal(FlowStateAction.SuggestBreak, result.RecommendedAction);
        Assert.NotNull(result.CooldownMinutes);
        Assert.InRange(result.CooldownMinutes!.Value, 5, 30);
    }

    [Fact]
    public void Assess_LongSessionEvenWithoutFatigue_ClassifiesFatigued()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.1,
            accuracyTrend: 0.2,
            consecutiveCorrect: 5,
            sessionDurationMinutes: 46.0);

        Assert.Equal(FlowStateKind.Fatigued, result.State);
        Assert.NotNull(result.CooldownMinutes);
    }

    // ── Precedence rules: fatigued > disrupted > inFlow > approaching ────

    [Fact]
    public void Assess_FatiguedWinsOverDisrupted_WhenBothTrigger()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.9,   // fatigued
            accuracyTrend: -0.8, // also disrupted
            consecutiveCorrect: 0,
            sessionDurationMinutes: 10.0);

        Assert.Equal(FlowStateKind.Fatigued, result.State);
    }

    [Fact]
    public void Assess_DisruptedWinsOverInFlow_WhenTrendCollapses()
    {
        var sut = CreateService(out _);

        // Would be inFlow on streak + fatigue, but trend is below disrupted
        // floor — disrupted wins per the state-machine ordering.
        var result = sut.Assess(
            fatigueLevel: 0.1,
            accuracyTrend: -0.4,
            consecutiveCorrect: 5,
            sessionDurationMinutes: 10.0);

        Assert.Equal(FlowStateKind.Disrupted, result.State);
    }

    // ── Action sub-rules for ambiguous warming/approaching ───────────────

    [Fact]
    public void Assess_Approaching_WithBorderlineFatigue_RecommendsSlowDown()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.55,   // above SlowDown floor 0.5, below Fatigued 0.7
            accuracyTrend: 0.05,
            consecutiveCorrect: 2,
            sessionDurationMinutes: 8.0);

        Assert.Equal(FlowStateKind.Approaching, result.State);
        Assert.Equal(FlowStateAction.SlowDown, result.RecommendedAction);
    }

    [Fact]
    public void Assess_Warming_WithMildNegativeTrend_RecommendsSlowDown()
    {
        var sut = CreateService(out _);

        // Trend slightly negative (but above -0.3 so not disrupted),
        // no streak → warming. SlowDown recommended to pace the student.
        var result = sut.Assess(
            fatigueLevel: 0.2,
            accuracyTrend: -0.1,
            consecutiveCorrect: 0,
            sessionDurationMinutes: 2.0);

        Assert.Equal(FlowStateKind.Warming, result.State);
        Assert.Equal(FlowStateAction.SlowDown, result.RecommendedAction);
    }

    // ── Difficulty adjustment delegation ─────────────────────────────────

    [Fact]
    public void Assess_WhenCurrentDifficultyProvided_PopulatesAdvice()
    {
        var sut = CreateService(out _);

        // Low fatigue + mid-difficulty → CognitiveLoadService recommends Increase.
        var result = sut.Assess(
            fatigueLevel: 0.2,
            accuracyTrend: 0.1,
            consecutiveCorrect: 1,
            sessionDurationMinutes: 5.0,
            currentDifficulty: 5);

        Assert.NotNull(result.DifficultyAdjustmentAdvice);
        Assert.Equal(DifficultyAdjustment.Increase, result.DifficultyAdjustmentAdvice);
    }

    [Fact]
    public void Assess_WhenCurrentDifficultyOmitted_AdviceIsNull()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.2,
            accuracyTrend: 0.1,
            consecutiveCorrect: 1,
            sessionDurationMinutes: 5.0);

        Assert.Null(result.DifficultyAdjustmentAdvice);
    }

    [Fact]
    public void Assess_HighFatigueWithDifficulty_RecommendsEase()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 0.8,
            accuracyTrend: 0.0,
            consecutiveCorrect: 0,
            sessionDurationMinutes: 20.0,
            currentDifficulty: 6);

        Assert.Equal(FlowStateKind.Fatigued, result.State);
        Assert.Equal(DifficultyAdjustment.Ease, result.DifficultyAdjustmentAdvice);
    }

    // ── Defensive clamping ───────────────────────────────────────────────

    [Fact]
    public void Assess_ClampsOutOfRangeInputs()
    {
        var sut = CreateService(out _);

        var result = sut.Assess(
            fatigueLevel: 2.5,            // clamped to 1.0 → Fatigued
            accuracyTrend: -9.0,          // clamped to -1.0
            consecutiveCorrect: -3,       // clamped to 0
            sessionDurationMinutes: -1.0, // clamped to 0.0
            currentDifficulty: 7);

        Assert.Equal(FlowStateKind.Fatigued, result.State);  // fatigue clamp triggers
        Assert.Equal(1.0, result.FatigueLevel);
        Assert.Equal(-1.0, result.AccuracyTrend);
        Assert.Equal(0, result.ConsecutiveCorrect);
        Assert.Equal(0.0, result.SessionDurationMinutes);
        Assert.Equal(DifficultyAdjustment.Ease, result.DifficultyAdjustmentAdvice);
    }

    // ── Cooldown gating ──────────────────────────────────────────────────

    [Fact]
    public void Assess_NonFatiguedState_NeverReturnsCooldown()
    {
        var sut = CreateService(out _);

        var approaching = sut.Assess(0.2, 0.1, 1, 5.0);
        var inFlow      = sut.Assess(0.2, 0.25, 4, 10.0);
        var disrupted   = sut.Assess(0.3, -0.6, 0, 12.0);
        var warming     = sut.Assess(0.0, 0.0, 0, 0.0);

        Assert.Null(approaching.CooldownMinutes);
        Assert.Null(inFlow.CooldownMinutes);
        Assert.Null(disrupted.CooldownMinutes);
        Assert.Null(warming.CooldownMinutes);
    }

    // ── Structured logging ───────────────────────────────────────────────

    [Fact]
    public void Assess_EmitsFlowStateStructuredLog()
    {
        var sut = CreateService(out var logger);

        sut.Assess(0.8, 0.0, 0, 20.0, currentDifficulty: 6);

        Assert.Contains(logger.Entries, e =>
            e.Message.Contains("[FLOW_STATE]")
            && e.Message.Contains("Fatigued")
            && e.Message.Contains("action="));
    }

    // ── Constructor guards ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCognitiveLoad_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FlowStateService(
            cognitiveLoad: null!,
            logger: NullLogger<FlowStateService>.Instance));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FlowStateService(
            cognitiveLoad: new CognitiveLoadService(),
            logger: null!));
    }

    // ── Test helpers ─────────────────────────────────────────────────────

    private static FlowStateService CreateService(out ListLogger logger)
    {
        logger = new ListLogger();
        return new FlowStateService(new CognitiveLoadService(), logger);
    }

    private sealed class ListLogger : ILogger<FlowStateService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
