// =============================================================================
// Cena Platform — DiagnosticBlockSelector unit tests (prr-228)
//
// Pins the 2026-04-21 tightening:
//   - MIN items before adaptive stop = 4
//   - FLOOR cap = 6 (always at least this many unless converged after 4)
//   - CEILING cap = 8 (hard stop regardless of convergence)
//   - Skip responses don't penalise the posterior
//   - Convergence band = 0.25 around 0.5
// =============================================================================

using Cena.Actors.Diagnosis.PerTarget;
using Cena.Actors.Mastery;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PerTarget;

public sealed class DiagnosticBlockSelectorTests
{
    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1, 0.4)]
    [InlineData(2, 0.9)]
    [InlineData(3, 0.1)]
    public void Decide_ReturnsContinue_WhenBelowMinItems(int served, double posterior)
    {
        var decision = DiagnosticBlockSelector.Decide(served, posterior);
        Assert.Equal(AdaptiveStopDecision.Continue, decision);
    }

    [Fact]
    public void Decide_ReturnsStopCeiling_AtCeilingCap()
    {
        var decision = DiagnosticBlockSelector.Decide(
            DiagnosticBlockThresholds.CeilingCap, 0.5);
        Assert.Equal(AdaptiveStopDecision.StopCeiling, decision);
    }

    [Fact]
    public void Decide_ReturnsStopConverged_WhenPosteriorBelow025_AtFourItems()
    {
        // Clearly below band (|0.2 - 0.5| = 0.3 >= 0.25) and served >= 4
        var decision = DiagnosticBlockSelector.Decide(4, 0.2);
        Assert.Equal(AdaptiveStopDecision.StopConverged, decision);
    }

    [Fact]
    public void Decide_ReturnsStopConverged_WhenPosteriorAbove075_AtFourItems()
    {
        // Clearly above band (|0.8 - 0.5| = 0.3 >= 0.25) and served >= 4
        var decision = DiagnosticBlockSelector.Decide(4, 0.8);
        Assert.Equal(AdaptiveStopDecision.StopConverged, decision);
    }

    [Fact]
    public void Decide_ReturnsContinue_WhenInsideConvergenceBand()
    {
        // |0.55 - 0.5| = 0.05 < 0.25 — no convergence signal
        var decision = DiagnosticBlockSelector.Decide(5, 0.55);
        Assert.Equal(AdaptiveStopDecision.Continue, decision);
    }

    [Fact]
    public void Decide_FloorCapReached_ConvergesOnlyIfBandCrossed()
    {
        // At floor cap with non-converged posterior → still continue
        var notConverged = DiagnosticBlockSelector.Decide(
            DiagnosticBlockThresholds.FloorCap, 0.55);
        Assert.Equal(AdaptiveStopDecision.Continue, notConverged);

        // At floor cap with converged posterior → stop
        var converged = DiagnosticBlockSelector.Decide(
            DiagnosticBlockThresholds.FloorCap, 0.9);
        Assert.Equal(AdaptiveStopDecision.StopConverged, converged);
    }

    [Fact]
    public void Decide_RejectsNegativeServed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagnosticBlockSelector.Decide(-1, 0.5));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Decide_RejectsOutOfBandPosterior(double posterior)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagnosticBlockSelector.Decide(5, posterior));
    }

    // ------------------------------------------------------------------
    // Skip handling — skips do NOT move the posterior.
    // ------------------------------------------------------------------

    [Fact]
    public void UpdatePosterior_SkipIsNoop()
    {
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");
        var skipResponse = new DiagnosticBlockResponse(
            ItemId: "q1",
            SkillCode: skill,
            Action: DiagnosticResponseAction.Skipped,
            Correct: false, // ignored when skipped
            DifficultyIrt: -0.5);

        var before = 0.42;
        var after = DiagnosticBlockSelector.UpdatePosterior(before, skipResponse);
        Assert.Equal(before, after, precision: 4);
    }

    [Fact]
    public void UpdatePosterior_CorrectAnswerIncreasesPosterior()
    {
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");
        var correct = new DiagnosticBlockResponse(
            ItemId: "q1",
            SkillCode: skill,
            Action: DiagnosticResponseAction.Answered,
            Correct: true,
            DifficultyIrt: 0);

        var before = 0.3;
        var after = DiagnosticBlockSelector.UpdatePosterior(before, correct);
        Assert.True(after > before,
            $"expected posterior to rise on correct response, got {before} -> {after}");
    }

    [Fact]
    public void UpdatePosterior_WrongAnswerDecreasesPosterior_BeforeLearnStep()
    {
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");
        var wrong = new DiagnosticBlockResponse(
            ItemId: "q1",
            SkillCode: skill,
            Action: DiagnosticResponseAction.Answered,
            Correct: false,
            DifficultyIrt: 0);

        // Start from high prior so the learn-step doesn't mask the drop.
        var before = 0.8;
        var after = DiagnosticBlockSelector.UpdatePosterior(before, wrong);
        Assert.True(after < before,
            $"expected posterior to fall on wrong response, got {before} -> {after}");
    }

    [Fact]
    public void UpdatePosterior_ClampsToValidRange()
    {
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");
        var correct = new DiagnosticBlockResponse(
            ItemId: "q1",
            SkillCode: skill,
            Action: DiagnosticResponseAction.Answered,
            Correct: true,
            DifficultyIrt: 0);

        var result = DiagnosticBlockSelector.UpdatePosterior(1.0, correct);
        Assert.InRange(result, 0.0, 1.0);
    }

    // ------------------------------------------------------------------
    // Skip-heavy block still hits floor cap (cold-start safety net).
    // ------------------------------------------------------------------

    [Fact]
    public void Decide_AllSkipsPosteriorStaysAt05_BlockRunsToFloorCap()
    {
        double posterior = 0.5;
        var skill = SkillCode.Parse("math.algebra.quadratic-equations");

        // Simulate a student skipping every item from served=1 to served=5.
        for (int served = 1; served < DiagnosticBlockThresholds.FloorCap; served++)
        {
            var skip = new DiagnosticBlockResponse(
                "q" + served, skill, DiagnosticResponseAction.Skipped,
                Correct: false, DifficultyIrt: -0.5);
            posterior = DiagnosticBlockSelector.UpdatePosterior(posterior, skip);
            var decision = DiagnosticBlockSelector.Decide(served, posterior);
            // Posterior stays 0.5, which is dead-centre of the band → no
            // convergence signal → block continues.
            Assert.Equal(AdaptiveStopDecision.Continue, decision);
        }
    }
}
