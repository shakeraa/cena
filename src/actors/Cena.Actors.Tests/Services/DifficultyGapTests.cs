// =============================================================================
// Cena Platform — DifficultyGap Utility Tests
// Tests classification, gap computation, and suggested max tokens.
// =============================================================================

using Cena.Actors.Services;

namespace Cena.Actors.Tests.Services;

public class DifficultyGapTests
{
    [Theory]
    [InlineData(0.9f, 0.3f, DifficultyFrame.Stretch)]       // gap +0.6
    [InlineData(0.7f, 0.3f, DifficultyFrame.Stretch)]       // gap +0.4
    [InlineData(0.61f, 0.3f, DifficultyFrame.Stretch)]      // gap +0.31 (just above boundary)
    public void Classify_StretchQuestions(float difficulty, float mastery, DifficultyFrame expected)
    {
        var gap = DifficultyGap.Compute(difficulty, mastery);
        Assert.Equal(expected, DifficultyGap.Classify(gap));
    }

    [Theory]
    [InlineData(0.5f, 0.3f, DifficultyFrame.Challenge)]     // gap +0.2
    [InlineData(0.6f, 0.45f, DifficultyFrame.Challenge)]    // gap +0.15
    public void Classify_ChallengeQuestions(float difficulty, float mastery, DifficultyFrame expected)
    {
        var gap = DifficultyGap.Compute(difficulty, mastery);
        Assert.Equal(expected, DifficultyGap.Classify(gap));
    }

    [Theory]
    [InlineData(0.5f, 0.5f, DifficultyFrame.Appropriate)]   // gap 0.0
    [InlineData(0.55f, 0.5f, DifficultyFrame.Appropriate)]  // gap +0.05
    [InlineData(0.45f, 0.5f, DifficultyFrame.Appropriate)]  // gap -0.05
    public void Classify_AppropriateQuestions(float difficulty, float mastery, DifficultyFrame expected)
    {
        var gap = DifficultyGap.Compute(difficulty, mastery);
        Assert.Equal(expected, DifficultyGap.Classify(gap));
    }

    [Theory]
    [InlineData(0.3f, 0.5f, DifficultyFrame.Expected)]      // gap -0.2
    [InlineData(0.35f, 0.5f, DifficultyFrame.Expected)]     // gap -0.15
    public void Classify_ExpectedQuestions(float difficulty, float mastery, DifficultyFrame expected)
    {
        var gap = DifficultyGap.Compute(difficulty, mastery);
        Assert.Equal(expected, DifficultyGap.Classify(gap));
    }

    [Theory]
    [InlineData(0.1f, 0.8f, DifficultyFrame.Regression)]    // gap -0.7
    [InlineData(0.2f, 0.6f, DifficultyFrame.Regression)]    // gap -0.4
    public void Classify_RegressionQuestions(float difficulty, float mastery, DifficultyFrame expected)
    {
        var gap = DifficultyGap.Compute(difficulty, mastery);
        Assert.Equal(expected, DifficultyGap.Classify(gap));
    }

    [Fact]
    public void Compute_ReturnsPositiveForStretch()
    {
        var gap = DifficultyGap.Compute(0.8f, 0.3f);
        Assert.True(gap > 0, "Stretch question should have positive gap");
        Assert.Equal(0.5f, gap, precision: 2);
    }

    [Fact]
    public void Compute_ReturnsNegativeForRegression()
    {
        var gap = DifficultyGap.Compute(0.2f, 0.8f);
        Assert.True(gap < 0, "Regression question should have negative gap");
        Assert.Equal(-0.6f, gap, precision: 2);
    }

    [Fact]
    public void Analyze_ReturnsGapAndFrame()
    {
        var (gap, frame) = DifficultyGap.Analyze(0.9f, 0.3f);
        Assert.Equal(0.6f, gap, precision: 2);
        Assert.Equal(DifficultyFrame.Stretch, frame);
    }

    [Theory]
    [InlineData(DifficultyFrame.Stretch, 500)]
    [InlineData(DifficultyFrame.Challenge, 400)]
    [InlineData(DifficultyFrame.Appropriate, 350)]
    [InlineData(DifficultyFrame.Expected, 250)]
    [InlineData(DifficultyFrame.Regression, 200)]
    public void SuggestedMaxTokens_DecreasesWithEasierFrames(DifficultyFrame frame, int expectedTokens)
    {
        Assert.Equal(expectedTokens, DifficultyGap.SuggestedMaxTokens(frame));
    }

    [Fact]
    public void SuggestedMaxTokens_StretchGetsMore()
    {
        Assert.True(
            DifficultyGap.SuggestedMaxTokens(DifficultyFrame.Stretch) >
            DifficultyGap.SuggestedMaxTokens(DifficultyFrame.Regression),
            "Stretch questions should get more tokens than regression");
    }

    [Theory]
    [InlineData(DifficultyFrame.Stretch)]
    [InlineData(DifficultyFrame.Challenge)]
    [InlineData(DifficultyFrame.Appropriate)]
    [InlineData(DifficultyFrame.Expected)]
    [InlineData(DifficultyFrame.Regression)]
    public void Label_ReturnsNonEmptyForAllFrames(DifficultyFrame frame)
    {
        var label = DifficultyGap.Label(frame);
        Assert.NotEmpty(label);
        Assert.DoesNotContain("unknown", label);
    }

    // ── ToPromptFrame tests ──

    [Fact]
    public void ToPromptFrame_Stretch_ContainsEncouragement()
    {
        var frame = DifficultyGap.ToPromptFrame(DifficultyFrame.Stretch);
        Assert.Contains("challenging", frame);
        Assert.Contains("Encourage", frame);
    }

    [Fact]
    public void ToPromptFrame_Appropriate_ReturnsEmpty()
    {
        var frame = DifficultyGap.ToPromptFrame(DifficultyFrame.Appropriate);
        Assert.Equal("", frame);
    }

    [Fact]
    public void ToPromptFrame_Regression_ContainsDiagnostic()
    {
        var frame = DifficultyGap.ToPromptFrame(DifficultyFrame.Regression);
        Assert.Contains("below", frame);
        Assert.Contains("prerequisite", frame);
    }

    [Fact]
    public void ToPromptFrame_Expected_ContainsGapFocus()
    {
        var frame = DifficultyGap.ToPromptFrame(DifficultyFrame.Expected);
        Assert.Contains("within", frame);
        Assert.Contains("gap", frame);
    }

    [Fact]
    public void ToPromptFrame_Challenge_ReturnsNonEmpty()
    {
        var frame = DifficultyGap.ToPromptFrame(DifficultyFrame.Challenge);
        Assert.NotEmpty(frame);
        Assert.Contains("productive", frame);
    }

    // ── AdjustMaxTokens tests ──

    [Fact]
    public void AdjustMaxTokens_Stretch_Increases50Percent()
    {
        Assert.Equal(600, DifficultyGap.AdjustMaxTokens(400, DifficultyFrame.Stretch));
    }

    [Fact]
    public void AdjustMaxTokens_Regression_Decreases30Percent()
    {
        Assert.Equal(280, DifficultyGap.AdjustMaxTokens(400, DifficultyFrame.Regression));
    }

    [Fact]
    public void AdjustMaxTokens_Appropriate_NoChange()
    {
        Assert.Equal(400, DifficultyGap.AdjustMaxTokens(400, DifficultyFrame.Appropriate));
    }

    [Fact]
    public void AdjustMaxTokens_Challenge_Increases20Percent()
    {
        Assert.Equal(480, DifficultyGap.AdjustMaxTokens(400, DifficultyFrame.Challenge));
    }

    [Fact]
    public void AdjustMaxTokens_Expected_Decreases15Percent()
    {
        Assert.Equal(340, DifficultyGap.AdjustMaxTokens(400, DifficultyFrame.Expected));
    }
}
