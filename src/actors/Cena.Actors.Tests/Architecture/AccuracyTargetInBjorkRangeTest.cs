// =============================================================================
// Cena Platform — Architecture test: Accuracy target in Bjork range (prr-030)
//
// Enforces that every institute-level TargetAccuracy override ships inside
// the Bjork-bounded range [0.6, 0.9]. Below 0.6 triggers extinction
// learning / quitter-surrender; above 0.9 hits the ceiling effect where
// additional practice adds no consolidation signal (Wilson 2019).
//
// Scope:
//   - DifficultyTarget constants (Default, PreExam) MUST be in range.
//   - DifficultyTarget.IsInBjorkRange is the single arbiter — this test
//     is the guardrail against a future contributor quietly shipping a
//     0.55 or 0.95 override because it "felt right".
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Infrastructure.Documents;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public class AccuracyTargetInBjorkRangeTest
{
    [Fact]
    public void Default_cohort_target_is_in_Bjork_range()
    {
        Assert.True(
            DifficultyTarget.IsInBjorkRange(DifficultyTarget.Default),
            $"Default cohort target {DifficultyTarget.Default} is outside " +
            $"the Bjork-bounded range " +
            $"[{DifficultyTarget.BjorkMinTarget}, {DifficultyTarget.BjorkMaxTarget}]. " +
            "See docs/adr/0051-desirable-difficulty-institute-override.md.");
    }

    [Fact]
    public void PreExam_target_is_in_Bjork_range()
    {
        Assert.True(
            DifficultyTarget.IsInBjorkRange(DifficultyTarget.PreExam),
            $"Pre-exam window target {DifficultyTarget.PreExam} is outside " +
            $"the Bjork-bounded range " +
            $"[{DifficultyTarget.BjorkMinTarget}, {DifficultyTarget.BjorkMaxTarget}]. " +
            "Raising above the ceiling hits the Wilson 2019 consolidation " +
            "cap — drop it back to 0.85 or re-audit the ADR.");
    }

    [Theory]
    [InlineData(0.60)]
    [InlineData(0.65)]
    [InlineData(0.70)]
    [InlineData(0.75)]
    [InlineData(0.80)]
    [InlineData(0.85)]
    [InlineData(0.90)]
    public void Valid_institute_overrides_are_accepted(double target)
    {
        Assert.True(DifficultyTarget.IsInBjorkRange(target));
        var config = new InstituteConfig("institute-test", target);
        Assert.Equal(target, config.TargetAccuracy);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.59)]
    [InlineData(0.5)]
    [InlineData(0.91)]
    [InlineData(0.95)]
    [InlineData(1.0)]
    public void Out_of_range_targets_are_rejected_by_DifficultyTarget(double badTarget)
    {
        Assert.False(DifficultyTarget.IsInBjorkRange(badTarget));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DifficultyTarget.TargetSuccessRate(
                MotivationProfile.Neutral,
                DateTimeOffset.UtcNow,
                registeredBagrutDateUtc: null,
                instituteTargetOverride: badTarget));
    }

    [Fact]
    public void Null_override_uses_Default_75pct()
    {
        // Sanity: with no institute override, the default 0.75 drives.
        var target = DifficultyTarget.TargetSuccessRate(
            MotivationProfile.Neutral,
            DateTimeOffset.UtcNow,
            registeredBagrutDateUtc: null,
            instituteTargetOverride: null);
        Assert.Equal(0.75, target, precision: 6);
    }

    [Fact]
    public void Valid_override_replaces_Default_not_PreExam()
    {
        var now = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        // Institute configured 0.70. Outside pre-exam window → 0.70 applies.
        var outsideWindow = DifficultyTarget.TargetSuccessRate(
            MotivationProfile.Neutral, now, registeredBagrutDateUtc: null,
            instituteTargetOverride: 0.70);
        Assert.Equal(0.70, outsideWindow, precision: 6);

        // Inside pre-exam window → 0.85 still wins (pre-exam is a safety
        // floor; the override does not lower the exam-prep ceiling).
        var insideWindow = DifficultyTarget.TargetSuccessRate(
            MotivationProfile.Neutral, now, registeredBagrutDateUtc: now.AddDays(10),
            instituteTargetOverride: 0.70);
        Assert.Equal(0.85, insideWindow, precision: 6);
    }

    [Fact]
    public void Valid_override_composes_with_anxious_boost()
    {
        var now = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        var target = DifficultyTarget.TargetSuccessRate(
            MotivationProfile.Anxious, now, registeredBagrutDateUtc: null,
            instituteTargetOverride: 0.70);
        // Anxious +5pp on 0.70 = 0.75, still in range.
        Assert.Equal(0.75, target, precision: 6);
    }
}
