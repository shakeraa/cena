// =============================================================================
// prr-041 cohort difficulty target — DifficultyTarget.TargetSuccessRate
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class DifficultyTargetTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_75pct_when_no_bagrut_date()
    {
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, registeredBagrutDateUtc: null);
        Assert.Equal(0.75, target, precision: 6);
    }

    [Fact]
    public void Default_75pct_when_bagrut_more_than_30_days_out()
    {
        var examDate = Now.AddDays(45);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, examDate);
        Assert.Equal(0.75, target, precision: 6);
    }

    [Fact]
    public void PreExam_85pct_within_30_day_window()
    {
        var examDate = Now.AddDays(15);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, examDate);
        Assert.Equal(0.85, target, precision: 6);
    }

    [Fact]
    public void PreExam_85pct_on_boundary_exactly_30_days_out()
    {
        var examDate = Now.AddDays(30);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, examDate);
        Assert.Equal(0.85, target, precision: 6);
    }

    [Fact]
    public void PreExam_85pct_on_boundary_today_zero_days_out()
    {
        var examDate = Now;
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, examDate);
        Assert.Equal(0.85, target, precision: 6);
    }

    [Fact]
    public void Exam_in_past_falls_back_to_default_75pct()
    {
        var examDate = Now.AddDays(-5);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Neutral, Now, examDate);
        Assert.Equal(0.75, target, precision: 6);
    }

    [Fact]
    public void Anxious_profile_adds_5pp_to_default_80pct()
    {
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Anxious, Now, registeredBagrutDateUtc: null);
        Assert.Equal(0.80, target, precision: 6);
    }

    [Fact]
    public void Anxious_profile_adds_5pp_to_preexam_90pct()
    {
        var examDate = Now.AddDays(10);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Anxious, Now, examDate);
        Assert.Equal(0.90, target, precision: 6);
    }

    [Fact]
    public void Confident_profile_no_adjustment()
    {
        // Confident gets no +5pp boost — the boost is specifically for anxiety mitigation.
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Confident, Now, registeredBagrutDateUtc: null);
        Assert.Equal(0.75, target, precision: 6);
    }

    [Fact]
    public void Anxious_profile_past_exam_falls_back_to_80pct_not_90()
    {
        // Past exam date disables pre-exam boost but anxious boost still applies.
        var examDate = Now.AddDays(-3);
        var target = DifficultyTarget.TargetSuccessRate(MotivationProfile.Anxious, Now, examDate);
        Assert.Equal(0.80, target, precision: 6);
    }

    [Fact]
    public void Output_is_always_in_valid_unit_interval()
    {
        foreach (var p in new[] { MotivationProfile.Neutral, MotivationProfile.Confident, MotivationProfile.Anxious })
        foreach (var d in new DateTimeOffset?[] { null, Now.AddDays(-100), Now.AddDays(15), Now.AddDays(500) })
            Assert.InRange(DifficultyTarget.TargetSuccessRate(p, Now, d), 0.0, 1.0);
    }
}
