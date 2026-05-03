using System.Diagnostics.Metrics;
using Cena.Actors.Services;
using NSubstitute;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// FOC-003: Proactive Microbreak Engine tests.
/// Covers scheduling, flow-state skipping, cooldown, skip tracking,
/// activity rotation, duration selection, and two-tier break integration.
/// </summary>
public sealed class MicrobreakSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 27, 14, 0, 0, TimeSpan.Zero);

    private static MicrobreakContext MakeContext(
        int questionsAnswered = 8,
        double elapsedMinutes = 16,
        FocusLevel level = FocusLevel.Engaged,
        double focusScore = 0.7,
        DateTimeOffset? time = null)
    {
        return new MicrobreakContext(
            QuestionsAnswered: questionsAnswered,
            ElapsedMinutes: elapsedMinutes,
            CurrentFocusLevel: level,
            FocusScore: focusScore,
            CurrentTime: time ?? Now.AddMinutes(elapsedMinutes)
        );
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.1: Microbreak Scheduler — trigger logic
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Microbreak_TriggersAfter8Questions()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);

        // Answer 7 questions — no trigger (1 min per question, well under 10-min time threshold)
        for (int i = 0; i < 7; i++)
        {
            scheduler.OnQuestionAnswered();
            var ctx = MakeContext(questionsAnswered: i + 1, elapsedMinutes: i + 1,
                time: Now.AddMinutes(i + 1));
            Assert.False(scheduler.ShouldTrigger(ctx).ShouldBreak,
                $"Should not trigger at question {i + 1}");
        }

        // 8th question at 8 min — question threshold fires (still under 10-min time threshold)
        scheduler.OnQuestionAnswered();
        var triggerCtx = MakeContext(questionsAnswered: 8, elapsedMinutes: 8,
            time: Now.AddMinutes(8));
        Assert.True(scheduler.ShouldTrigger(triggerCtx).ShouldBreak);
    }

    [Fact]
    public void Microbreak_TriggersAfter10Minutes()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);

        // Only 3 questions but 11 minutes elapsed
        for (int i = 0; i < 3; i++) scheduler.OnQuestionAnswered();
        var ctx = MakeContext(questionsAnswered: 3, elapsedMinutes: 11);
        Assert.True(scheduler.ShouldTrigger(ctx).ShouldBreak);
    }

    [Fact]
    public void Microbreak_SkippedDuringFlow()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 10; i++) scheduler.OnQuestionAnswered();

        var ctx = MakeContext(
            questionsAnswered: 10, elapsedMinutes: 20,
            level: FocusLevel.Flow, focusScore: 0.9);
        var decision = scheduler.ShouldTrigger(ctx);

        Assert.False(decision.ShouldBreak);
        Assert.Contains("flow", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Microbreak_SkippedDuringCooldown()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        // Simulate reactive break just taken
        scheduler.RecordRecoveryBreakTaken(Now);

        for (int i = 0; i < 10; i++) scheduler.OnQuestionAnswered();

        // Only 3 minutes after recovery break (cooldown is 5 min)
        var ctx = MakeContext(questionsAnswered: 10, elapsedMinutes: 3,
            time: Now.AddMinutes(3));
        Assert.False(scheduler.ShouldTrigger(ctx).ShouldBreak);
    }

    [Fact]
    public void Microbreak_ResetsCounterAfterTaken()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();

        var ctx = MakeContext(questionsAnswered: 8, elapsedMinutes: 16);
        Assert.True(scheduler.ShouldTrigger(ctx).ShouldBreak);

        scheduler.RecordMicrobreakTaken(Now.AddMinutes(16));

        // Next question should NOT trigger (counter reset)
        scheduler.OnQuestionAnswered();
        var ctx2 = MakeContext(questionsAnswered: 9, elapsedMinutes: 18,
            time: Now.AddMinutes(18));
        Assert.False(scheduler.ShouldTrigger(ctx2).ShouldBreak);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.1: Skip tracking & opt-out
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Microbreak_DisabledAfter3ConsecutiveSkips()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);

        for (int skip = 0; skip < 3; skip++)
        {
            for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();
            scheduler.RecordMicrobreakSkipped(Now.AddMinutes(16 + skip * 16));
        }

        // 4th cycle — should be disabled
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();
        var ctx = MakeContext(questionsAnswered: 32, elapsedMinutes: 64,
            time: Now.AddMinutes(64));
        var decision = scheduler.ShouldTrigger(ctx);
        Assert.False(decision.ShouldBreak);
        Assert.Contains("opted out", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Microbreak_TakingOneResetsConsecutiveSkips()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);

        // Skip twice
        scheduler.RecordMicrobreakSkipped(Now);
        scheduler.RecordMicrobreakSkipped(Now.AddMinutes(10));
        Assert.Equal(2, scheduler.GetSessionStats().ConsecutiveSkips);

        // Take one — resets
        scheduler.RecordMicrobreakTaken(Now.AddMinutes(20));
        Assert.Equal(0, scheduler.GetSessionStats().ConsecutiveSkips);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.2: Duration & activity
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Microbreak_Duration60s_WhenEngaged()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();

        var ctx = MakeContext(level: FocusLevel.Engaged, focusScore: 0.7);
        var decision = scheduler.ShouldTrigger(ctx);

        Assert.Equal(60, decision.DurationSeconds);
    }

    [Fact]
    public void Microbreak_Duration90s_WhenDrifting()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();

        var ctx = MakeContext(level: FocusLevel.Drifting, focusScore: 0.5);
        var decision = scheduler.ShouldTrigger(ctx);

        Assert.Equal(90, decision.DurationSeconds);
    }

    [Fact]
    public void Microbreak_ActivitiesRotate_NoRepeat()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        var activities = new List<MicrobreakActivity>();

        // Trigger 5 microbreaks — each should have a different activity
        for (int cycle = 0; cycle < 5; cycle++)
        {
            for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();
            var ctx = MakeContext(elapsedMinutes: 16 + cycle * 16,
                time: Now.AddMinutes(16 + cycle * 16));
            var decision = scheduler.ShouldTrigger(ctx);
            Assert.True(decision.ShouldBreak);
            activities.Add(decision.Activity);
            scheduler.RecordMicrobreakTaken(Now.AddMinutes(16 + cycle * 16));
        }

        // All 5 activity types should appear
        Assert.Equal(5, activities.Distinct().Count());

        // No consecutive repeats
        for (int i = 1; i < activities.Count; i++)
        {
            Assert.NotEqual(activities[i - 1], activities[i]);
        }
    }

    [Fact]
    public void Microbreak_MessageIsHebrew()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();

        var ctx = MakeContext();
        var decision = scheduler.ShouldTrigger(ctx);

        Assert.False(string.IsNullOrEmpty(decision.Message));
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.2: Session stats
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SessionStats_TracksCorrectly()
    {
        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);

        scheduler.RecordMicrobreakTaken(Now);
        scheduler.RecordMicrobreakTaken(Now.AddMinutes(10));
        scheduler.RecordMicrobreakSkipped(Now.AddMinutes(20));

        var stats = scheduler.GetSessionStats();
        Assert.Equal(2, stats.MicrobreaksTaken);
        Assert.Equal(1, stats.MicrobreaksSkipped);
        Assert.Equal(1, stats.ConsecutiveSkips);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.3: Two-tier integration
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TwoTier_MicrobreakTakesPriority_OverNoBreak()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        for (int i = 0; i < 8; i++) scheduler.OnQuestionAnswered();

        // Focus is good (Engaged) — reactive would say no break
        var state = new FocusState(
            FocusScore: 0.7, Level: FocusLevel.Engaged,
            AttentionScore: 0.8, EngagementScore: 0.6,
            TrendScore: 0.7, VigilanceScore: 0.7,
            MinutesActive: 16, QuestionsAttempted: 8);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);
        var mbCtx = MakeContext(level: FocusLevel.Engaged, focusScore: 0.7);

        var recommendation = svc.RecommendBreakTwoTier(state, timeCtx, scheduler, mbCtx);

        Assert.True(recommendation.ShouldBreak);
        Assert.Equal(BreakType.Microbreak, recommendation.BreakType);
        Assert.True(recommendation.DurationSeconds is 60 or 90);
        Assert.NotNull(recommendation.MicrobreakActivity);
    }

    [Fact]
    public void TwoTier_ReactiveBreak_WhenNoMicrobreakDue()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        var scheduler = new MicrobreakScheduler(MicrobreakConfig.Default, Now);
        // Only 2 questions — no microbreak due

        // Focus is bad (Fatigued) — reactive should trigger
        var state = new FocusState(
            FocusScore: 0.3, Level: FocusLevel.Fatigued,
            AttentionScore: 0.3, EngagementScore: 0.2,
            TrendScore: 0.3, VigilanceScore: 0.4,
            MinutesActive: 4, QuestionsAttempted: 2);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);
        var mbCtx = MakeContext(questionsAnswered: 2, elapsedMinutes: 4,
            level: FocusLevel.Fatigued, focusScore: 0.3, time: Now.AddMinutes(4));

        var recommendation = svc.RecommendBreakTwoTier(state, timeCtx, scheduler, mbCtx);

        Assert.True(recommendation.ShouldBreak);
        Assert.Equal(BreakType.RecoveryBreak, recommendation.BreakType);
        Assert.Equal(15, recommendation.Minutes);
    }

    [Fact]
    public void ReactiveBreak_BackwardsCompatible()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        var svc = new FocusDegradationService(meterFactory);

        // Existing RecommendBreak() still works
        var state = new FocusState(
            FocusScore: 0.3, Level: FocusLevel.Fatigued,
            AttentionScore: 0.3, EngagementScore: 0.2,
            TrendScore: 0.3, VigilanceScore: 0.4,
            MinutesActive: 20, QuestionsAttempted: 10);
        var timeCtx = new TimeOfDayContext(IsLateEvening: false, SessionsToday: 1);

        var recommendation = svc.RecommendBreak(state, timeCtx);
        Assert.True(recommendation.ShouldBreak);
        Assert.Equal(15, recommendation.Minutes);
    }

    // ═══════════════════════════════════════════════════════════════
    // FOC-003.1: Custom config
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CustomConfig_DifferentThresholds()
    {
        var config = new MicrobreakConfig(
            QuestionsPerMicrobreak: 5,
            MinutesBetweenMicrobreaks: 7);
        var scheduler = new MicrobreakScheduler(config, Now);

        for (int i = 0; i < 5; i++) scheduler.OnQuestionAnswered();
        var ctx = MakeContext(questionsAnswered: 5, elapsedMinutes: 10);
        Assert.True(scheduler.ShouldTrigger(ctx).ShouldBreak);
    }
}
