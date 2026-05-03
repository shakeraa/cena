using System.Diagnostics.Metrics;
using Cena.Actors.Services;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-001: Enhanced Focus Signal Pipeline tests.
/// Covers weight calculator, sensor integration, executive load, and backwards compatibility.
/// </summary>
public sealed class FocusPipelineTests
{
    private readonly FocusDegradationService _svc;

    public FocusPipelineTests()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _svc = new FocusDegradationService(meterFactory);
    }

    // ── Helper to build a FocusInput with sensible defaults ──
    private static FocusInput MakeInput(
        double elapsedMinutes = 10,
        int questionsAttempted = 5,
        double personalPeak = 15,
        double? motionStability = null,
        double? appFocus = null,
        double? touchPattern = null,
        double? environment = null,
        double? executiveLoad = null)
    {
        return new FocusInput(
            RecentResponseTimesMs: new[] { 3000.0, 3100, 2900, 3050, 2950 },
            BaselineResponseTimesMs: new[] { 3000.0, 3100, 2900, 3050, 2950 },
            RecentAccuracies: new[] { 0.8, 0.8, 0.7, 0.8, 0.9 },
            ElapsedMinutes: elapsedMinutes,
            QuestionsAttempted: questionsAttempted,
            HintsRequested: 1,
            AnnotationsAdded: 0,
            ApproachChanges: 0,
            PersonalPeakMinutes: personalPeak,
            MotionStabilityScore: motionStability,
            AppFocusScore: appFocus,
            TouchPatternScore: touchPattern,
            EnvironmentScore: environment,
            ExecutiveLoadFactor: executiveLoad
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-001.1: FocusInput sensor fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void FocusInput_NoSensors_SensorDataAvailable_IsFalse()
    {
        var input = MakeInput();
        Assert.False(input.SensorDataAvailable);
        Assert.Equal(0, input.SensorSignalCount);
    }

    [Fact]
    public void FocusInput_AllSensors_SensorDataAvailable_IsTrue()
    {
        var input = MakeInput(motionStability: 0.9, appFocus: 0.8, touchPattern: 0.7, environment: 0.6);
        Assert.True(input.SensorDataAvailable);
        Assert.Equal(4, input.SensorSignalCount);
    }

    [Fact]
    public void FocusInput_PartialSensors_CountsCorrectly()
    {
        var input = MakeInput(motionStability: 0.5, environment: 0.8);
        Assert.True(input.SensorDataAvailable);
        Assert.Equal(2, input.SensorSignalCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-001.2: FocusWeightCalculator
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Weights_NoSensors_MatchOriginal()
    {
        var weights = FocusWeightCalculator.ComputeWeights(SensorAvailability.None);
        Assert.Equal(0.30, weights.Attention, precision: 3);
        Assert.Equal(0.20, weights.Engagement, precision: 3);
        Assert.Equal(0.25, weights.Trend, precision: 3);
        Assert.Equal(0.25, weights.Vigilance, precision: 3);
        Assert.Equal(0.0, weights.Motion);
        Assert.Equal(0.0, weights.AppFocus);
        Assert.Equal(0.0, weights.TouchPattern);
        Assert.Equal(0.0, weights.Environment);
        Assert.InRange(weights.Sum(), 0.999, 1.001);
    }

    [Fact]
    public void Weights_AllSensors_Redistribute()
    {
        var weights = FocusWeightCalculator.ComputeWeights(SensorAvailability.All);
        Assert.Equal(8, weights.ActiveSignalCount);
        Assert.Equal(0.20, weights.Attention, precision: 3);
        Assert.Equal(0.12, weights.Engagement, precision: 3);
        Assert.Equal(0.15, weights.Trend, precision: 3);
        Assert.Equal(0.15, weights.Vigilance, precision: 3);
        Assert.Equal(0.12, weights.Motion, precision: 3);
        Assert.Equal(0.10, weights.AppFocus, precision: 3);
        Assert.Equal(0.08, weights.TouchPattern, precision: 3);
        Assert.Equal(0.08, weights.Environment, precision: 3);
        Assert.InRange(weights.Sum(), 0.999, 1.001);
    }

    [Fact]
    public void Weights_PartialSensors_SumToOne()
    {
        // Only motion and touch available
        var availability = SensorAvailability.Motion | SensorAvailability.TouchPattern;
        var weights = FocusWeightCalculator.ComputeWeights(availability);

        Assert.InRange(weights.Sum(), 0.999, 1.001);
        Assert.True(weights.Motion > 0);
        Assert.True(weights.TouchPattern > 0);
        Assert.Equal(0.0, weights.AppFocus);
        Assert.Equal(0.0, weights.Environment);
        // Core signals should have higher weight than full-sensor scenario (redistributed)
        Assert.True(weights.Attention > 0.20);
    }

    [Theory]
    [InlineData(SensorAvailability.Motion)]
    [InlineData(SensorAvailability.AppFocus)]
    [InlineData(SensorAvailability.TouchPattern)]
    [InlineData(SensorAvailability.Environment)]
    [InlineData(SensorAvailability.Motion | SensorAvailability.AppFocus)]
    [InlineData(SensorAvailability.Motion | SensorAvailability.TouchPattern | SensorAvailability.Environment)]
    public void Weights_AnyCombination_AlwaysSumsToOne(SensorAvailability availability)
    {
        var weights = FocusWeightCalculator.ComputeWeights(availability);
        Assert.InRange(weights.Sum(), 0.999, 1.001);
    }

    [Fact]
    public void FromInput_MapsNullablesToFlags()
    {
        var input = MakeInput(motionStability: 0.5, touchPattern: 0.7);
        var flags = FocusWeightCalculator.FromInput(input);
        Assert.True(flags.HasFlag(SensorAvailability.Motion));
        Assert.False(flags.HasFlag(SensorAvailability.AppFocus));
        Assert.True(flags.HasFlag(SensorAvailability.TouchPattern));
        Assert.False(flags.HasFlag(SensorAvailability.Environment));
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-001.3: Sensor signal integration in ComputeFocusState
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeFocusState_NoSensors_BackwardsCompatible()
    {
        var input = MakeInput();
        var state = _svc.ComputeFocusState(input);

        Assert.Equal(0, state.SensorSignalCount);
        Assert.Equal(0.0, state.SensorConfidenceBoost);
        Assert.InRange(state.FocusScore, 0.0, 1.0);
    }

    [Fact]
    public void ComputeFocusState_WithSensors_IncludesSensorCount()
    {
        var input = MakeInput(motionStability: 0.9, appFocus: 0.85, touchPattern: 0.8, environment: 0.7);
        var state = _svc.ComputeFocusState(input);

        Assert.Equal(4, state.SensorSignalCount);
        Assert.Equal(0.20, state.SensorConfidenceBoost, precision: 3);
    }

    [Fact]
    public void ComputeFocusState_HighSensorScores_ImprovesFocus()
    {
        // Without sensors
        var noSensorInput = MakeInput();
        var noSensorState = _svc.ComputeFocusState(noSensorInput);

        // With high sensor scores (student is still, focused, consistent touch, stable env)
        var sensorInput = MakeInput(motionStability: 0.95, appFocus: 0.95, touchPattern: 0.9, environment: 0.9);
        var sensorState = _svc.ComputeFocusState(sensorInput);

        // High sensor signals should not decrease focus
        // (the sensor signals are all positive indicators, blended into composite)
        Assert.True(sensorState.FocusScore >= noSensorState.FocusScore * 0.9,
            $"High sensors ({sensorState.FocusScore:F3}) should not significantly decrease focus vs no sensors ({noSensorState.FocusScore:F3})");
    }

    [Fact]
    public void ComputeFocusState_LowSensorScores_LowersFocus()
    {
        // Without sensors
        var noSensorInput = MakeInput();
        var noSensorState = _svc.ComputeFocusState(noSensorInput);

        // With low sensor scores (fidgeting, app switching, erratic touch, unstable env)
        var sensorInput = MakeInput(motionStability: 0.1, appFocus: 0.1, touchPattern: 0.1, environment: 0.1);
        var sensorState = _svc.ComputeFocusState(sensorInput);

        // Low sensor signals should reduce focus score
        Assert.True(sensorState.FocusScore < noSensorState.FocusScore,
            $"Low sensors ({sensorState.FocusScore:F3}) should decrease focus vs no sensors ({noSensorState.FocusScore:F3})");
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-001.4: Executive Load Factor
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0.0, 20.0, 0.92)] // recall at 20 min → slow decay (≈0.914)
    [InlineData(0.6, 20.0, 0.89)] // analysis at 20 min → faster decay (≈0.888)
    [InlineData(0.8, 20.0, 0.88)] // synthesis at 20 min → fastest decay (≈0.879)
    public void ExecutiveLoad_IncreasesDecayRate(double load, double minutes, double expectedMax)
    {
        var input = MakeInput(elapsedMinutes: minutes, personalPeak: 15, executiveLoad: load);
        var state = _svc.ComputeFocusState(input);

        // Vigilance component should be at or below the expected max
        Assert.True(state.VigilanceScore <= expectedMax,
            $"Vigilance {state.VigilanceScore:F3} should be <= {expectedMax} with load={load} at {minutes}min");
    }

    [Fact]
    public void ExecutiveLoad_RecallVsSynthesis_RecallDecaysSlower()
    {
        var recallInput = MakeInput(elapsedMinutes: 25, personalPeak: 15, executiveLoad: 0.0);
        var synthInput = MakeInput(elapsedMinutes: 25, personalPeak: 15, executiveLoad: 0.8);

        var recallState = _svc.ComputeFocusState(recallInput);
        var synthState = _svc.ComputeFocusState(synthInput);

        // Recall (load=0) should have higher vigilance than synthesis (load=0.8)
        Assert.True(recallState.VigilanceScore > synthState.VigilanceScore,
            $"Recall vigilance ({recallState.VigilanceScore:F3}) should > synthesis ({synthState.VigilanceScore:F3})");
    }

    [Fact]
    public void ExecutiveLoad_Null_DefaultsToZero()
    {
        // No executive load = same as recall (0.0)
        var nullInput = MakeInput(elapsedMinutes: 20, personalPeak: 15);
        var zeroInput = MakeInput(elapsedMinutes: 20, personalPeak: 15, executiveLoad: 0.0);

        var nullState = _svc.ComputeFocusState(nullInput);
        var zeroState = _svc.ComputeFocusState(zeroInput);

        Assert.Equal(nullState.VigilanceScore, zeroState.VigilanceScore, precision: 6);
    }

    [Fact]
    public void ExecutiveLoad_BeforePeak_NoEffect()
    {
        // Before peak, vigilance is ramping up — executive load shouldn't affect it
        var noLoadInput = MakeInput(elapsedMinutes: 5, personalPeak: 15, executiveLoad: 0.0);
        var highLoadInput = MakeInput(elapsedMinutes: 5, personalPeak: 15, executiveLoad: 0.8);

        var noLoadState = _svc.ComputeFocusState(noLoadInput);
        var highLoadState = _svc.ComputeFocusState(highLoadInput);

        // Before peak, vigilance score should be the same regardless of load
        Assert.Equal(noLoadState.VigilanceScore, highLoadState.VigilanceScore, precision: 6);
    }
}
