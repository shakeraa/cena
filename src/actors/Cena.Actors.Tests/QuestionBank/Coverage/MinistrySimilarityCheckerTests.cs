// =============================================================================
// Cena Platform — Ministry Similarity Checker tests (prr-201, ADR-0043)
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

public sealed class MinistrySimilarityCheckerTests
{
    [Fact]
    public void EmptyCorpus_ReturnsZero()
    {
        var c = new MinistrySimilarityChecker(new EmptyMinistryReferenceCorpus());
        var v = c.Score("Solve 2x+1=5", "math", "fourunit");
        Assert.Equal(0, v.Score);
        Assert.False(v.IsTooClose);
    }

    [Fact]
    public void VerbatimMatch_ScoresAboveThreshold()
    {
        var stem = "Prove that the sum of interior angles of any triangle is 180 degrees.";
        var corpus = new StubMinistryReferenceCorpus(("bagrut-geom-1", stem));
        var checker = new MinistrySimilarityChecker(corpus, threshold: 0.82);

        var v = checker.Score(stem, "math", "fourunit");
        Assert.True(v.Score > 0.9);
        Assert.True(v.IsTooClose);
        Assert.Equal("bagrut-geom-1", v.NearestReferenceId);
    }

    [Fact]
    public void UnrelatedStem_ScoresLow()
    {
        var corpus = new StubMinistryReferenceCorpus(
            ("bagrut-geom-1", "Prove the Pythagorean theorem for a right triangle."));
        var checker = new MinistrySimilarityChecker(corpus, threshold: 0.82);

        var v = checker.Score("Find the derivative of sin(x) at x=0", "math", "fourunit");
        Assert.True(v.Score < 0.3);
        Assert.False(v.IsTooClose);
    }

    [Fact]
    public void ThresholdBoundary_Respected()
    {
        // Verify threshold config actually gates; same input twice with
        // different thresholds flips IsTooClose.
        var stem = "Solve the system of linear equations x plus y equals seven.";
        var corpus = new StubMinistryReferenceCorpus(("b", stem));

        var strict = new MinistrySimilarityChecker(corpus, threshold: 0.99);
        var loose  = new MinistrySimilarityChecker(corpus, threshold: 0.50);

        var vStrict = strict.Score(stem, "math", "fourunit");
        var vLoose  = loose.Score(stem, "math", "fourunit");

        Assert.True(vStrict.Score >= 0.9);
        Assert.True(vLoose.IsTooClose);
        // strict threshold at 0.99 won't flip unless score ≥0.99
        Assert.Equal(vStrict.Score >= 0.99, vStrict.IsTooClose);
    }

    [Fact]
    public void NormaliseStripsPunctuationAndCase()
    {
        Assert.Equal("solve 2x 1 5", MinistrySimilarityChecker.Normalise("  Solve: 2x + 1 = 5!  "));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ConstructorRejectsInvalidThreshold(double bad)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MinistrySimilarityChecker(new EmptyMinistryReferenceCorpus(), bad));
    }
}
