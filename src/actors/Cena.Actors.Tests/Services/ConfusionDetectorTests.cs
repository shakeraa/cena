using System.Diagnostics.Metrics;
using Cena.Actors.Services;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-005: Confusion vs Frustration Discriminator tests.
/// Covers confusion detection, resolution tracking, and struggle classifier integration.
/// </summary>
public sealed class ConfusionDetectorTests
{
    private readonly ConfusionDetector _detector = new();
    private static readonly DateTimeOffset Now = new(2026, 3, 27, 14, 0, 0, TimeSpan.Zero);

    // ═══════════════════════════════════════════════════════════════
    // FOC-005.1: Confusion detection signals
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void NotConfused_WhenNoSignals()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: false,
            ResponseTimeRatio: 1.0,
            LastAnswerCorrect: true,
            AnswerChangedCount: 0,
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 0,
            AccuracyInConfusionWindow: 0);

        Assert.Equal(ConfusionState.NotConfused, _detector.Detect(input));
    }

    [Fact]
    public void NotConfused_WithOnlyOneSignal()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: true, // only 1 signal
            ResponseTimeRatio: 1.0,
            LastAnswerCorrect: false,
            AnswerChangedCount: 0,
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 0,
            AccuracyInConfusionWindow: 0);

        Assert.Equal(ConfusionState.NotConfused, _detector.Detect(input));
    }

    [Fact]
    public void Confused_WithTwoSignals()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: true,    // signal 1
            ResponseTimeRatio: 2.0,          // signal 2 (slow)
            LastAnswerCorrect: true,         // got it right after thinking
            AnswerChangedCount: 0,
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 0,
            AccuracyInConfusionWindow: 0);

        Assert.Equal(ConfusionState.Confused, _detector.Detect(input));
    }

    [Fact]
    public void Confused_WithThreeSignals()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: true,    // signal 1
            ResponseTimeRatio: 1.8,          // signal 2
            LastAnswerCorrect: true,
            AnswerChangedCount: 2,           // signal 3
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 0,
            AccuracyInConfusionWindow: 0);

        Assert.Equal(ConfusionState.Confused, _detector.Detect(input));
    }

    [Fact]
    public void ConfusionResolving_WhenAccuracyRecovering()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: true,
            ResponseTimeRatio: 1.6,
            LastAnswerCorrect: true,
            AnswerChangedCount: 1,
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 3,   // in active window
            AccuracyInConfusionWindow: 0.67); // recovering (>0.5)

        Assert.Equal(ConfusionState.ConfusionResolving, _detector.Detect(input));
    }

    [Fact]
    public void ConfusionStuck_WhenWindowExpired()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: true,
            ResponseTimeRatio: 1.8,
            LastAnswerCorrect: true,
            AnswerChangedCount: 1,
            HintRequestedThenCancelled: false,
            QuestionsInConfusionWindow: 5,   // patience window full
            AccuracyInConfusionWindow: 0.2,  // not recovering
            PatienceWindowSize: 5);

        Assert.Equal(ConfusionState.ConfusionStuck, _detector.Detect(input));
    }

    [Fact]
    public void HintCancelledSignal_Counted()
    {
        var input = new ConfusionInput(
            WrongOnMasteredConcept: false,
            ResponseTimeRatio: 1.0,
            LastAnswerCorrect: false,
            AnswerChangedCount: 1,           // signal 1
            HintRequestedThenCancelled: true, // signal 2
            QuestionsInConfusionWindow: 0,
            AccuracyInConfusionWindow: 0);

        Assert.Equal(ConfusionState.Confused, _detector.Detect(input));
    }
}

/// <summary>
/// FOC-005.2: Confusion Resolution Tracker tests.
/// </summary>
public sealed class ConfusionResolutionTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 27, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewTracker_DefaultResolutionRate()
    {
        var tracker = new ConfusionResolutionTracker();
        Assert.Equal(0.5, tracker.GetResolutionRate());
    }

    [Fact]
    public void OnConfusionDetected_StartsWindow()
    {
        var tracker = new ConfusionResolutionTracker();
        tracker.OnConfusionDetected("algebra-1", Now);
        Assert.True(tracker.IsInConfusionWindow("algebra-1"));
        Assert.False(tracker.IsInConfusionWindow("geometry-1"));
    }

    [Fact]
    public void Resolution_WhenCorrectAnswer()
    {
        var tracker = new ConfusionResolutionTracker();
        tracker.OnConfusionDetected("algebra-1", Now);

        var outcome = tracker.OnQuestionAttempted("algebra-1", correct: true, Now.AddMinutes(1));
        Assert.Equal(ConfusionWindowOutcome.Resolved, outcome);
        Assert.False(tracker.IsInConfusionWindow("algebra-1"));
    }

    [Fact]
    public void Unresolved_WhenWindowExpires()
    {
        var tracker = new ConfusionResolutionTracker();
        tracker.OnConfusionDetected("algebra-1", Now);

        // Default patience window is 5 questions
        ConfusionWindowOutcome outcome = ConfusionWindowOutcome.Monitoring;
        for (int i = 0; i < 5; i++)
        {
            outcome = tracker.OnQuestionAttempted("algebra-1", correct: false, Now.AddMinutes(i + 1));
        }

        Assert.Equal(ConfusionWindowOutcome.Unresolved, outcome);
        Assert.False(tracker.IsInConfusionWindow("algebra-1"));
    }

    [Fact]
    public void Monitoring_WhileInWindow()
    {
        var tracker = new ConfusionResolutionTracker();
        tracker.OnConfusionDetected("algebra-1", Now);

        var outcome = tracker.OnQuestionAttempted("algebra-1", correct: false, Now.AddMinutes(1));
        Assert.Equal(ConfusionWindowOutcome.Monitoring, outcome);
        Assert.True(tracker.IsInConfusionWindow("algebra-1"));
    }

    [Fact]
    public void NotInWindow_ForUntracked()
    {
        var tracker = new ConfusionResolutionTracker();
        var outcome = tracker.OnQuestionAttempted("unknown", correct: true, Now);
        Assert.Equal(ConfusionWindowOutcome.NotInWindow, outcome);
    }

    [Fact]
    public void ResolutionRate_UpdatesOverTime()
    {
        var tracker = new ConfusionResolutionTracker();

        // 3 resolved, 1 unresolved
        for (int i = 0; i < 3; i++)
        {
            tracker.OnConfusionDetected($"c-{i}", Now);
            tracker.OnQuestionAttempted($"c-{i}", correct: true, Now.AddMinutes(i));
        }
        // After 3 resolved, rate=1.0, adaptive window=7
        tracker.OnConfusionDetected("c-fail", Now);
        for (int i = 0; i < 7; i++) // exhaust the full adaptive window
            tracker.OnQuestionAttempted("c-fail", correct: false, Now.AddMinutes(i));

        Assert.Equal(0.75, tracker.GetResolutionRate());
    }

    [Fact]
    public void AdaptivePatienceWindow_HighResolvers()
    {
        var tracker = new ConfusionResolutionTracker();
        // Build up high resolution rate (>0.7)
        for (int i = 0; i < 8; i++)
        {
            tracker.OnConfusionDetected($"c-{i}", Now);
            tracker.OnQuestionAttempted($"c-{i}", correct: true, Now.AddMinutes(i));
        }

        Assert.Equal(7, tracker.GetAdaptivePatienceWindow());
    }

    [Fact]
    public void AdaptivePatienceWindow_LowResolvers()
    {
        var tracker = new ConfusionResolutionTracker();
        // Build up low resolution rate (<0.3)
        for (int i = 0; i < 8; i++)
        {
            tracker.OnConfusionDetected($"c-{i}", Now);
            for (int j = 0; j < 5; j++)
                tracker.OnQuestionAttempted($"c-{i}", correct: false, Now.AddMinutes(j));
        }

        Assert.Equal(3, tracker.GetAdaptivePatienceWindow());
    }
}

/// <summary>
/// FOC-005.3: Struggle classifier integration tests.
/// </summary>
public sealed class ConfusionStruggleIntegrationTests
{
    private readonly FocusDegradationService _svc;

    public ConfusionStruggleIntegrationTests()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _svc = new FocusDegradationService(meterFactory);
    }

    [Fact]
    public void ProductiveConfusion_DetectedWhenConfusedButRecovering()
    {
        var input = new StruggleInput(
            AccuracySlope: 0.02,
            SameErrorTypeCount: 1,
            ResponseTimeMean: 4500,
            ResponseTimeStdDev: 800,
            AnnotationSentiment: 0.5,
            ConfusionState: ConfusionState.ConfusionResolving
        );
        var result = _svc.ClassifyStruggle(input);
        Assert.Equal(StruggleType.ProductiveConfusion, result.Type);
        Assert.Contains("NO hint", result.Recommendation);
    }

    [Fact]
    public void ConfusionStuck_MapsToFrustrationWithScaffold()
    {
        var input = new StruggleInput(
            AccuracySlope: -0.02,
            SameErrorTypeCount: 3,
            ResponseTimeMean: 5000,
            ResponseTimeStdDev: 2000,
            AnnotationSentiment: 0.2,
            ConfusionState: ConfusionState.ConfusionStuck
        );
        var result = _svc.ClassifyStruggle(input);
        Assert.Equal(StruggleType.UnproductiveFrustration, result.Type);
        Assert.Contains("scaffolding", result.Recommendation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not methodology switch", result.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshConfusion_ClassifiedAsProductiveConfusion()
    {
        var input = new StruggleInput(
            AccuracySlope: 0.0,
            SameErrorTypeCount: 1,
            ResponseTimeMean: 4000,
            ResponseTimeStdDev: 1000,
            AnnotationSentiment: 0.5,
            ConfusionState: ConfusionState.Confused
        );
        var result = _svc.ClassifyStruggle(input);
        Assert.Equal(StruggleType.ProductiveConfusion, result.Type);
        Assert.Contains("Monitor", result.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotConfused_FallsThroughToStandardClassification()
    {
        var input = new StruggleInput(
            AccuracySlope: 0.05,
            SameErrorTypeCount: 1,
            ResponseTimeMean: 3000,
            ResponseTimeStdDev: 500,
            AnnotationSentiment: 0.7,
            ConfusionState: ConfusionState.NotConfused
        );
        var result = _svc.ClassifyStruggle(input);
        Assert.Equal(StruggleType.ProductiveStruggle, result.Type);
    }

    [Fact]
    public void NullConfusionState_BackwardsCompatible()
    {
        // No confusion data — standard classification
        var input = new StruggleInput(
            AccuracySlope: 0.05,
            SameErrorTypeCount: 1,
            ResponseTimeMean: 3000,
            ResponseTimeStdDev: 500,
            AnnotationSentiment: 0.7
        );
        var result = _svc.ClassifyStruggle(input);
        Assert.Equal(StruggleType.ProductiveStruggle, result.Type);
    }

    [Fact]
    public void ConfusionResolving_TakesPriority_OverStandardClassification()
    {
        // Even though standard signals say "frustration", confusion resolving wins
        var input = new StruggleInput(
            AccuracySlope: -0.05,       // declining
            SameErrorTypeCount: 4,       // repetitive errors
            ResponseTimeMean: 5000,
            ResponseTimeStdDev: 3000,
            AnnotationSentiment: 0.1,    // negative
            ConfusionState: ConfusionState.ConfusionResolving
        );
        var result = _svc.ClassifyStruggle(input);
        // Confusion resolving overrides standard frustration classification
        Assert.Equal(StruggleType.ProductiveConfusion, result.Type);
    }
}
