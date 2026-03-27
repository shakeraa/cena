using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// Tests for BktService covering Corbett & Anderson (1994) update equations,
/// forgetting factor impact (ACT-024), and parameter divergence detection (ACT-019 finding).
/// </summary>
public sealed class BktServiceTests
{
    private readonly BktService _sut = new();

    // ── Core BKT: correct answer increases mastery ──

    [Fact]
    public void Update_CorrectAnswer_IncreasesMastery()
    {
        var result = _sut.Update(new BktUpdateInput(
            PriorMastery: 0.3,
            IsCorrect: true,
            Parameters: BktParameters.Default));

        Assert.True(result.PosteriorMastery > 0.3,
            $"Correct answer should increase mastery, got {result.PosteriorMastery}");
    }

    [Fact]
    public void Update_IncorrectAnswer_DecreasesMastery()
    {
        var result = _sut.Update(new BktUpdateInput(
            PriorMastery: 0.5,
            IsCorrect: false,
            Parameters: BktParameters.Default));

        Assert.True(result.PosteriorMastery < 0.5,
            $"Incorrect answer should decrease mastery, got {result.PosteriorMastery}");
    }

    // ── Threshold detection ──

    [Fact]
    public void Update_CrossingProgressionThreshold_DetectedCorrectly()
    {
        var result = _sut.Update(new BktUpdateInput(
            PriorMastery: 0.84,
            IsCorrect: true,
            Parameters: BktParameters.Default));

        // After correct at 0.84, mastery should cross 0.85
        // (depends on forgetting factor -- may or may not cross)
        // This test documents current behavior
        Assert.Equal(result.PosteriorMastery >= 0.85, result.CrossedProgressionThreshold);
    }

    [Fact]
    public void Update_CrossingPrerequisiteGate_DetectedCorrectly()
    {
        var result = _sut.Update(new BktUpdateInput(
            PriorMastery: 0.94,
            IsCorrect: true,
            Parameters: BktParameters.Default));

        Assert.Equal(result.PosteriorMastery >= 0.95, result.MeetsPrerequisiteGate);
    }

    // ── Clamping: output stays in [0.01, 0.99] ──

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(1.0, false)]
    [InlineData(-0.5, true)]
    [InlineData(1.5, false)]
    public void Update_ExtremePriors_ClampedToValidRange(double prior, bool isCorrect)
    {
        var result = _sut.Update(new BktUpdateInput(
            PriorMastery: prior,
            IsCorrect: isCorrect,
            Parameters: BktParameters.Default));

        Assert.InRange(result.PosteriorMastery, 0.01, 0.99);
    }

    // ── ProbCorrect formula ──

    [Fact]
    public void ProbCorrect_ReturnsCorrectBayesianProbability()
    {
        var p = BktParameters.Default;
        double mastery = 0.6;

        // P(correct) = P(L) * (1 - P(S)) + (1 - P(L)) * P(G)
        double expected = mastery * (1.0 - p.PSlip) + (1.0 - mastery) * p.PGuess;

        double actual = _sut.ProbCorrect(mastery, p);

        Assert.Equal(expected, actual, precision: 10);
    }

    // ══════════════════════════════════════════════════════════════════
    // ACT-024: Forgetting factor impact
    // pForget=0.02 means students need ~6 more correct answers to reach
    // 0.85 mastery compared to the standard Corbett & Anderson model.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_ForgettingFactor_DepressesMasteryOnEveryAttempt()
    {
        var withForget = BktParameters.Default; // pForget = 0.02
        var noForget = withForget with { PForget = 0.0 };

        var resultWith = _sut.Update(new BktUpdateInput(0.5, true, withForget));
        var resultWithout = _sut.Update(new BktUpdateInput(0.5, true, noForget));

        Assert.True(resultWith.PosteriorMastery < resultWithout.PosteriorMastery,
            $"Forgetting factor should depress mastery: {resultWith.PosteriorMastery} should be < {resultWithout.PosteriorMastery}");
    }

    [Fact]
    public void Update_ForgettingFactor_RequiresMoreAttemptsToReachMastery()
    {
        var withForget = BktParameters.Default; // pForget=0.02
        var noForget = withForget with { PForget = 0.0 };

        // Use a moderate forgetting factor to clearly show the difference
        var highForget = withForget with { PForget = 0.10 };

        int attemptsDefault = CountAttemptsToMastery(withForget);
        int attemptsNone = CountAttemptsToMastery(noForget);
        int attemptsHigh = CountAttemptsToMastery(highForget);

        // Higher forgetting should always require more attempts
        Assert.True(attemptsHigh > attemptsNone,
            $"With pForget=0.10: {attemptsHigh} attempts should be > without: {attemptsNone}");

        // Default forgetting (0.02) should require >= attempts than no forgetting
        Assert.True(attemptsDefault >= attemptsNone,
            $"With pForget=0.02: {attemptsDefault} attempts should be >= without: {attemptsNone}");
    }

    [Fact]
    public void Update_ForgettingFactor_CumulativeImpactOverManyAttempts()
    {
        // After 20 correct answers, the forgetting factor should produce
        // measurably lower mastery than without it
        var withForget = BktParameters.Default;
        var noForget = withForget with { PForget = 0.0 };

        double masteryWith = 0.10;
        double masteryWithout = 0.10;

        for (int i = 0; i < 20; i++)
        {
            masteryWith = _sut.Update(new BktUpdateInput(masteryWith, true, withForget)).PosteriorMastery;
            masteryWithout = _sut.Update(new BktUpdateInput(masteryWithout, true, noForget)).PosteriorMastery;
        }

        Assert.True(masteryWith < masteryWithout,
            $"After 20 correct answers: with forgetting ({masteryWith:F4}) should be < without ({masteryWithout:F4})");
    }

    private int CountAttemptsToMastery(BktParameters parameters)
    {
        double mastery = parameters.PInitial;
        for (int i = 0; i < 200; i++)
        {
            var result = _sut.Update(new BktUpdateInput(mastery, true, parameters));
            mastery = result.PosteriorMastery;
            if (mastery >= parameters.ProgressionThreshold)
                return i + 1;
        }
        return 200; // Did not reach mastery
    }

    // ══════════════════════════════════════════════════════════════════
    // ACT-019 FINDING: Inline BKT parameter divergence
    // The inline fallback in StudentActor.Commands.cs uses different
    // constants than BktParameters.Default. This test documents the
    // expected parameters so the divergence is caught at test time.
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void BktParametersDefault_MatchesExpectedValues()
    {
        var p = BktParameters.Default;

        // These are the canonical values. If someone changes them,
        // the inline BKT fallback (which hardcodes its own) will diverge further.
        Assert.Equal(0.10, p.PLearning);
        Assert.Equal(0.05, p.PSlip);
        Assert.Equal(0.20, p.PGuess);
        Assert.Equal(0.02, p.PForget);
        Assert.Equal(0.10, p.PInitial);
        Assert.Equal(0.85, p.ProgressionThreshold);
        Assert.Equal(0.95, p.PrerequisiteGateThreshold);
    }

    /// <summary>
    /// REGRESSION: The inline BKT in StudentActor.Commands.cs:79-80 uses
    /// pGuess=0.25 and pSlip=0.10, which differ from BktParameters.Default
    /// (pGuess=0.20, pSlip=0.05). This test quantifies the divergence.
    /// </summary>
    [Fact]
    public void InlineBktVsService_ParameterDivergence_ProducesDifferentResults()
    {
        // Inline fallback parameters (from StudentActor.Commands.cs:79-80)
        const double inlinePGuess = 0.25;
        const double inlinePSlip = 0.10;
        const double inlinePLearn = 0.10;

        // Service parameters
        var serviceParams = BktParameters.Default;

        // Same starting mastery
        double prior = 0.3;

        // Inline BKT calculation (correct answer, not skipped):
        double inlinePCorrect = (1 - inlinePSlip) * prior + inlinePGuess * (1 - prior);
        double inlinePosterior = ((1 - inlinePSlip) * prior) / inlinePCorrect;
        double inlineUpdated = inlinePosterior + (1 - inlinePosterior) * inlinePLearn;

        // Service BKT calculation (correct answer):
        var serviceResult = _sut.Update(new BktUpdateInput(prior, true, serviceParams));

        // NOTE: service also applies forgetting factor, inline does NOT
        // This makes the comparison even more divergent
        Assert.NotEqual(inlineUpdated, serviceResult.PosteriorMastery);

        // Document the actual divergence magnitude
        double divergence = Math.Abs(inlineUpdated - serviceResult.PosteriorMastery);
        Assert.True(divergence > 0.01,
            $"BKT divergence between inline and service should be significant, got {divergence:F4}");
    }
}
