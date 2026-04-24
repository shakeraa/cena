using System.Diagnostics.Metrics;
using System.Reflection;
using Cena.Actors.Stagnation;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Stagnation;

/// <summary>
/// Tests for StagnationDetectorActor signal processing and composite scoring.
/// ACT-020: Validates that session-level signals produce correct stagnation detection.
/// </summary>
public sealed class StagnationDetectorTests
{
    private readonly StagnationDetectorActor _actor;

    public StagnationDetectorTests()
    {
        var logger = Substitute.For<ILogger<StagnationDetectorActor>>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _actor = new StagnationDetectorActor(logger, meterFactory);
    }

    [Fact]
    public void HandleUpdateSignals_StoresSessionSignal()
    {
        var msg = new UpdateStagnationSignals(
            "concept-1", SessionAccuracy: 0.6, AvgResponseTimeMs: 3000,
            SessionDurationMinutes: 15, ErrorRepeatCount: 2, AnnotationSentiment: 0.5);

        // HandleUpdateSignals takes (IContext, UpdateStagnationSignals)
        // IContext is not used in the method body, pass null
        InvokeMethod("HandleUpdateSignals", null!, msg);

        var windows = GetField<Dictionary<string, ConceptStagnationWindow>>("_windows");
        Assert.True(windows.ContainsKey("concept-1"));
        Assert.Single(windows["concept-1"].SessionSignals);
        Assert.Equal(0.6, windows["concept-1"].SessionSignals[0].Accuracy);
    }

    [Fact]
    public void HandleUpdateSignals_SlidingWindow_KeepsLast5()
    {
        for (int i = 0; i < 8; i++)
        {
            InvokeMethod("HandleUpdateSignals", null!, new UpdateStagnationSignals(
                "c1", 0.5 + i * 0.01, 3000, 15, 0, 0.5));
        }

        var windows = GetField<Dictionary<string, ConceptStagnationWindow>>("_windows");
        Assert.Equal(5, windows["c1"].SessionSignals.Count);
    }

    [Fact]
    public void HandleResetAfterSwitch_ResetsCooldownAndConsecutive()
    {
        // First add some signals
        InvokeMethod("HandleUpdateSignals", null!, new UpdateStagnationSignals(
            "c1", 0.5, 3000, 15, 3, 0.3));

        var windows = GetField<Dictionary<string, ConceptStagnationWindow>>("_windows");
        windows["c1"].ConsecutiveStagnantSessions = 5;

        InvokeMethod("HandleResetAfterSwitch", new ResetAfterSwitch("c1"));

        Assert.Equal(0, windows["c1"].ConsecutiveStagnantSessions);
        Assert.Equal(3, windows["c1"].CooldownRemaining); // CooldownSessions = 3
    }

    [Fact]
    public void ComputeCompositeScore_FlatAccuracy_ProducesPositiveScore()
    {
        // Two sessions with identical accuracy = plateau
        InvokeMethod("HandleUpdateSignals", null!, new UpdateStagnationSignals(
            "c1", 0.5, 3000, 15, 3, 0.3));
        InvokeMethod("HandleUpdateSignals", null!, new UpdateStagnationSignals(
            "c1", 0.5, 3500, 12, 4, 0.2));

        var method = typeof(StagnationDetectorActor)
            .GetMethod("ComputeCompositeScore", BindingFlags.NonPublic | BindingFlags.Instance);
        var windows = GetField<Dictionary<string, ConceptStagnationWindow>>("_windows");

        double score = (double)method!.Invoke(_actor, new object[] { windows["c1"] })!;

        Assert.True(score > 0.0, $"Flat accuracy with errors should produce positive score, got {score}");
        Assert.True(score <= 1.0, $"Score should be <= 1.0, got {score}");
    }

    // ── Helpers ──

    private void InvokeMethod(string name, params object?[] args)
    {
        var method = typeof(StagnationDetectorActor)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        method!.Invoke(_actor, args);
    }

    private T GetField<T>(string name)
    {
        var field = typeof(StagnationDetectorActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field!.GetValue(_actor)!;
    }
}
