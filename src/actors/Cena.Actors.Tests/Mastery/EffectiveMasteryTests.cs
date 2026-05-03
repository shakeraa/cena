// =============================================================================
// MST-005 Tests: Effective mastery compositor and full pipeline
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class EffectiveMasteryTests
{
    [Fact]
    public void EffectiveMastery_CombinesBktAndRecallWithPrereqs()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.85f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-168) // at half-life
        };
        var prereqSupport = 0.90f;
        var now = DateTimeOffset.UtcNow;

        var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport, now);

        // recall ~ 0.50 (at half-life), mastery = 0.85
        // min(0.85, 0.50) = 0.50, * 0.90 prereq = 0.45
        Assert.InRange(effective, 0.44f, 0.46f);
    }

    [Fact]
    public void EffectiveMastery_RecentPractice_UsesFullMastery()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.85f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var now = DateTimeOffset.UtcNow;

        var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport: 1.0f, now);

        // recall ~ 1.0, min(0.85, ~1.0) = 0.85
        Assert.InRange(effective, 0.84f, 0.86f);
    }

    [Fact]
    public void EffectiveMastery_ZeroPrereqSupport_ReturnsZero()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.95f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow
        };

        var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport: 0.0f, DateTimeOffset.UtcNow);

        Assert.Equal(0.0f, effective);
    }

    [Fact]
    public void EffectiveMastery_NeverInteracted_ReturnsZero()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.50f,
            HalfLifeHours = 168f
        };

        var effective = EffectiveMasteryCalculator.Compute(state, prereqSupport: 1.0f, DateTimeOffset.UtcNow);

        Assert.Equal(0.0f, effective);
    }

    [Fact]
    public void ThresholdCrossing_UpwardPast90_ReturnsMastered()
    {
        var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
            previousEffective: 0.88f, newEffective: 0.92f);

        Assert.Equal(MasteryThresholdEvent.ConceptMastered, evt);
    }

    [Fact]
    public void ThresholdCrossing_DownwardBelow70_ReturnsDecayed()
    {
        var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
            previousEffective: 0.75f, newEffective: 0.68f);

        Assert.Equal(MasteryThresholdEvent.MasteryDecayed, evt);
    }

    [Fact]
    public void ThresholdCrossing_DownwardBelow60_ReturnsPrerequisiteBlocked()
    {
        var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
            previousEffective: 0.62f, newEffective: 0.58f);

        Assert.Equal(MasteryThresholdEvent.PrerequisiteBlocked, evt);
    }

    [Fact]
    public void ThresholdCrossing_NoChange_ReturnsNull()
    {
        var evt = EffectiveMasteryCalculator.DetectThresholdCrossing(
            previousEffective: 0.80f, newEffective: 0.82f);

        Assert.Null(evt);
    }

    [Fact]
    public void FullPipeline_CorrectAnswer_IncreasesEffectiveMastery()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.50f,
            HalfLifeHours = 72f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-12),
            AttemptCount = 5,
            CorrectCount = 3,
            CurrentStreak = 1,
            BloomLevel = 2
        };
        var bktParams = new BktParameters(P_L0: 0.10f, P_T: 0.20f, P_S: 0.05f, P_G: 0.25f);
        var hlrFeatures = new HlrFeatures(6, 4, 0.5f, 2, 2, 7f);
        var hlrWeights = HlrWeights.Default;

        var result = MasteryPipeline.ProcessAttempt(
            state, isCorrect: true, bktParams, hlrFeatures, hlrWeights,
            prereqSupport: 1.0f, DateTimeOffset.UtcNow);

        Assert.True(result.NewState.MasteryProbability > state.MasteryProbability,
            $"Mastery should increase, was {state.MasteryProbability}, got {result.NewState.MasteryProbability}");
        Assert.Equal(6, result.NewState.AttemptCount);
        Assert.Equal(4, result.NewState.CorrectCount);
        Assert.Equal(2, result.NewState.CurrentStreak);
    }

    [Fact]
    public void FullPipeline_IncorrectAnswer_DecreasesMastery()
    {
        var state = new ConceptMasteryState
        {
            MasteryProbability = 0.80f,
            HalfLifeHours = 168f,
            LastInteraction = DateTimeOffset.UtcNow.AddHours(-1),
            AttemptCount = 10,
            CorrectCount = 8,
            CurrentStreak = 3,
            BloomLevel = 3
        };
        var bktParams = BktParameters.Default;
        var hlrFeatures = new HlrFeatures(11, 8, 0.5f, 2, 3, 14f);

        var result = MasteryPipeline.ProcessAttempt(
            state, isCorrect: false, bktParams, hlrFeatures, HlrWeights.Default,
            prereqSupport: 1.0f, DateTimeOffset.UtcNow);

        Assert.True(result.NewState.MasteryProbability < state.MasteryProbability);
        Assert.Equal(0, result.NewState.CurrentStreak); // reset on incorrect
    }
}
