using System.Diagnostics.Metrics;
using Cena.Actors.Services;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-006: Boredom-Fatigue Splitter tests.
/// Covers disengagement classification, extended focus levels,
/// and differentiated interventions.
/// </summary>
public sealed class DisengagementClassifierTests
{
    private readonly DisengagementClassifier _classifier = new();

    private static DisengagementInput MakeBored() => new(
        RecentAccuracy: 0.95,           // very high — too easy
        ResponseTimeRatio: 0.5,         // fast answers — rushing
        EngagementTrend: -0.1,          // declining engagement
        HintRequestRate: 0.0,           // no hints needed
        AppBackgroundingRate: 0.4,      // checking other apps
        MinutesSinceLastBreak: 8,
        TouchPatternConsistencyDelta: 0.0,
        SessionsToday: 1,
        MinutesInSession: 12,
        IsLateEvening: false
    );

    private static DisengagementInput MakeFatigued() => new(
        RecentAccuracy: 0.3,            // declining accuracy
        ResponseTimeRatio: 1.8,         // much slower than baseline
        EngagementTrend: -0.05,
        HintRequestRate: 0.1,
        AppBackgroundingRate: 0.05,
        MinutesSinceLastBreak: 25,      // long time without break
        TouchPatternConsistencyDelta: -0.4, // motor fatigue
        SessionsToday: 3,              // many sessions
        MinutesInSession: 20,          // late in session
        IsLateEvening: true
    );

    // ═══════════════════════════════════════════════════════════════
    // FOC-006.1: Disengagement classification
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Bored_TooEasy_HighAccuracyFastAnswers()
    {
        var result = _classifier.Classify(MakeBored());
        Assert.Equal(DisengagementType.Bored_TooEasy, result);
    }

    [Fact]
    public void Fatigued_Cognitive_SlowAndInaccurate()
    {
        var input = MakeFatigued() with { TouchPatternConsistencyDelta = -0.1 }; // not motor
        var result = _classifier.Classify(input);
        Assert.Equal(DisengagementType.Fatigued_Cognitive, result);
    }

    [Fact]
    public void Fatigued_Motor_TouchPatternDegradation()
    {
        var result = _classifier.Classify(MakeFatigued());
        Assert.Equal(DisengagementType.Fatigued_Motor, result);
    }

    [Fact]
    public void Unknown_InsufficientSignals()
    {
        var input = new DisengagementInput(
            RecentAccuracy: 0.6,
            ResponseTimeRatio: 1.0,
            EngagementTrend: 0.0,
            HintRequestRate: 0.05,
            AppBackgroundingRate: 0.1,
            MinutesSinceLastBreak: 10,
            TouchPatternConsistencyDelta: 0.0,
            SessionsToday: 1,
            MinutesInSession: 8,
            IsLateEvening: false
        );
        Assert.Equal(DisengagementType.Unknown, _classifier.Classify(input));
    }

    [Fact]
    public void Mixed_BothSignalsPresent()
    {
        // High accuracy + fast (boredom) BUT also long session + late evening (fatigue)
        var input = new DisengagementInput(
            RecentAccuracy: 0.9,
            ResponseTimeRatio: 0.6,
            EngagementTrend: -0.1,
            HintRequestRate: 0.0,
            AppBackgroundingRate: 0.35,
            MinutesSinceLastBreak: 25,
            TouchPatternConsistencyDelta: -0.3,
            SessionsToday: 3,
            MinutesInSession: 20,
            IsLateEvening: true
        );
        Assert.Equal(DisengagementType.Mixed, _classifier.Classify(input));
    }

    [Fact]
    public void Bored_NoValue_WhenAccuracyModerate()
    {
        // Boredom signals but accuracy not super high — perceived lack of value
        // Needs 3+ boredom signals: engagement decline + hint low + app backgrounding
        var input = MakeBored() with { RecentAccuracy = 0.82, HintRequestRate = 0.0 };
        var result = _classifier.Classify(input);
        Assert.Equal(DisengagementType.Bored_NoValue, result);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-006.3: Differentiated interventions
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BoredStudent_GetsChallenge_NotBreak()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var state = new FocusState(
            FocusScore: 0.15, Level: FocusLevel.DisengagedBored,
            AttentionScore: 0.2, EngagementScore: 0.1,
            TrendScore: 0.2, VigilanceScore: 0.3,
            MinutesActive: 15, QuestionsAttempted: 10);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);

        var rec = svc.RecommendBreak(state, timeCtx);
        Assert.False(rec.ShouldBreak, "Bored students should NOT get a break");
        Assert.Equal(AlternativeAction.IncreaseDifficulty, rec.AlternativeAction);
        Assert.NotNull(rec.Message);
    }

    [Fact]
    public void ExhaustedStudent_GetsBreak()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var state = new FocusState(
            FocusScore: 0.15, Level: FocusLevel.DisengagedExhausted,
            AttentionScore: 0.1, EngagementScore: 0.1,
            TrendScore: 0.1, VigilanceScore: 0.2,
            MinutesActive: 30, QuestionsAttempted: 15);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);

        var rec = svc.RecommendBreak(state, timeCtx);
        Assert.True(rec.ShouldBreak);
        Assert.True(rec.Minutes >= 15);
        Assert.Equal(AlternativeAction.None, rec.AlternativeAction);
    }

    [Fact]
    public void UnclassifiedDisengaged_StillGetsBreak()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var state = new FocusState(
            FocusScore: 0.1, Level: FocusLevel.Disengaged,
            AttentionScore: 0.1, EngagementScore: 0.1,
            TrendScore: 0.1, VigilanceScore: 0.1,
            MinutesActive: 20, QuestionsAttempted: 10);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);

        var rec = svc.RecommendBreak(state, timeCtx);
        Assert.True(rec.ShouldBreak);
        Assert.Equal(30, rec.Minutes); // Same as old Disengaged behavior
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-006.2: Extended FocusLevel handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PredictRemaining_DisengagedBored_ReturnsZero()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var state = new FocusState(
            FocusScore: 0.15, Level: FocusLevel.DisengagedBored,
            AttentionScore: 0.2, EngagementScore: 0.1,
            TrendScore: 0.2, VigilanceScore: 0.3,
            MinutesActive: 15, QuestionsAttempted: 10);

        Assert.Equal(0, svc.PredictRemainingProductiveQuestions(state));
    }

    [Fact]
    public void PredictRemaining_DisengagedExhausted_ReturnsZero()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var state = new FocusState(
            FocusScore: 0.1, Level: FocusLevel.DisengagedExhausted,
            AttentionScore: 0.1, EngagementScore: 0.1,
            TrendScore: 0.1, VigilanceScore: 0.1,
            MinutesActive: 30, QuestionsAttempted: 15);

        Assert.Equal(0, svc.PredictRemainingProductiveQuestions(state));
    }
}
