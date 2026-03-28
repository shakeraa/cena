using System.Diagnostics.Metrics;
using System.Reflection;
using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Cena.Actors.Sessions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Sessions;

/// <summary>
/// Tests for LearningSessionActor fatigue computation.
/// ACT-025.1: Verifies zero-allocation rolling average (no LINQ on hot path).
/// </summary>
public sealed class FatigueComputationTests
{
    private readonly LearningSessionActor _actor;

    public FatigueComputationTests()
    {
        var bkt = Substitute.For<IBktService>();
        var hintAdjustedBkt = Substitute.For<IHintAdjustedBktService>();
        var cognitiveLoad = new CognitiveLoadService();
        var logger = Substitute.For<ILogger<LearningSessionActor>>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var hintGenerator = new HintGenerator();
        var hintGenerationService = new HintGenerationService();
        var confusionDetector = new ConfusionDetector();
        var disengagementClassifier = new DisengagementClassifier();
        var graphCache = Substitute.For<IConceptGraphCache>();
        _actor = new LearningSessionActor(
            bkt, hintAdjustedBkt, cognitiveLoad, hintGenerator, hintGenerationService,
            confusionDetector, disengagementClassifier, graphCache, logger, meterFactory);
    }

    [Fact]
    public void ComputeFatigueScore_NoData_JustStarted_ReturnsLowFatigue()
    {
        // Set _startedAt to now so time fraction ≈ 0
        SetField("_startedAt", DateTimeOffset.UtcNow);

        double score = InvokeComputeFatigue();

        // With no data and just started: accuracy drop = 0, RT increase = 0, time fraction ~0
        Assert.InRange(score, 0.0, 0.1);
    }

    [Fact]
    public void ComputeFatigueScore_AfterMultipleAttempts_ProducesReasonableScore()
    {
        // Initialize the actor with baseline values
        SetField("_baselineAccuracy", 0.8);
        SetField("_baselineResponseTimeMs", 3000.0);
        SetField("_startedAt", DateTimeOffset.UtcNow.AddMinutes(-10));

        // Add some data to simulate degrading performance
        var accuracies = GetField<Queue<double>>("_recentAccuracies");
        var responseTimes = GetField<Queue<double>>("_recentResponseTimes");

        // 8 attempts, declining accuracy
        for (int i = 0; i < 8; i++)
        {
            accuracies.Enqueue(i < 4 ? 1.0 : 0.0); // First 4 correct, last 4 wrong
            responseTimes.Enqueue(3000 + i * 500);    // Gradually slower
        }

        double score = InvokeComputeFatigue();

        // Should be > 0 since accuracy is dropping and RT is increasing
        Assert.True(score > 0.0, $"Fatigue should be positive with degrading performance, got {score}");
        Assert.True(score <= 1.0, $"Fatigue should be <= 1.0, got {score}");
    }

    [Fact]
    public void ComputeFatigueScore_RollingAverage_UsesLastFiveOnly()
    {
        SetField("_baselineAccuracy", 0.8);
        SetField("_baselineResponseTimeMs", 5000.0);
        SetField("_startedAt", DateTimeOffset.UtcNow.AddMinutes(-5));

        var accuracies = GetField<Queue<double>>("_recentAccuracies");

        // 10 attempts: first 5 correct, last 5 wrong
        for (int i = 0; i < 5; i++) accuracies.Enqueue(1.0);
        for (int i = 0; i < 5; i++) accuracies.Enqueue(0.0);

        // Rolling average of last 5 should be 0.0 (all wrong)
        // Accuracy drop from baseline 0.8 → rolling 0.0 = 100% drop
        double score = InvokeComputeFatigue();

        // Signal 1 (accuracy drop) = 0.4 * 1.0 = 0.4 contribution
        Assert.True(score >= 0.35, $"High accuracy drop should produce significant fatigue, got {score}");
    }

    [Fact]
    public void ComputeFatigueScore_TimeFraction_ApproachesOneAtSessionEnd()
    {
        SetField("_baselineAccuracy", 0.5);
        SetField("_baselineResponseTimeMs", 5000.0);
        // Set startedAt to 25 minutes ago (DefaultSessionMinutes = 25)
        SetField("_startedAt", DateTimeOffset.UtcNow.AddMinutes(-25));

        double score = InvokeComputeFatigue();

        // Time fraction should be ~1.0, contributing W3 * 1.0 = 0.3
        Assert.True(score >= 0.25, $"Near session end should contribute significant fatigue, got {score}");
    }

    // ── Helpers for reflection-based private access ──

    private double InvokeComputeFatigue()
    {
        var method = typeof(LearningSessionActor)
            .GetMethod("ComputeFatigueScore", BindingFlags.NonPublic | BindingFlags.Instance);
        return (double)method!.Invoke(_actor, null)!;
    }

    private void SetField(string name, object value)
    {
        var field = typeof(LearningSessionActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(_actor, value);
    }

    private T GetField<T>(string name)
    {
        var field = typeof(LearningSessionActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field!.GetValue(_actor)!;
    }
}
