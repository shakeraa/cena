// =============================================================================
// Cena Platform — MathContentDetector Tests (RDY-036 §11 / RDY-037)
//
// Covers the boundary-probe logic that tells the CAS gate whether a question
// body carries math content that must be verified. The detector is the
// tripwire that stops language/history word-problems containing hidden math
// from slipping past the CAS gate; these tests pin that behaviour.
// =============================================================================

using Cena.Actors.Cas;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class MathContentDetectorTests
{
    private readonly MathContentDetector _detector = new();

    // ── Subject-driven detection ────────────────────────────────────────

    [Theory]
    [InlineData("math")]
    [InlineData("Mathematics")]
    [InlineData("MATHS")]
    [InlineData("physics")]
    [InlineData("chemistry")]
    public void MathSubject_IsAlwaysMath_EvenWithPlainProseBody(string subject)
    {
        var result = _detector.Analyze("A ball rolls down a ramp.", subject);
        Assert.True(result.HasMathContent);
    }

    [Theory]
    [InlineData("history")]
    [InlineData("english")]
    [InlineData("hebrew")]
    [InlineData("civics")]
    public void NonMathSubject_PlainProse_IsNotMath(string subject)
    {
        var result = _detector.Analyze("Describe the causes of the Industrial Revolution.", subject);
        Assert.False(result.HasMathContent);
    }

    // ── LaTeX delimiter detection (word-problem safety-net) ──────────────

    [Fact]
    public void InlineDollarLatex_InNonMathSubject_IsDetected()
    {
        var result = _detector.Analyze(
            "Sara solves $x + 2 = 5$ in her notebook.",
            "english");

        Assert.True(result.HasMathContent);
        Assert.Single(result.ExtractedExpressions);
        Assert.Contains("x + 2 = 5", result.ExtractedExpressions[0]);
    }

    [Fact]
    public void DoubleDollar_IsNotConfusedWithInline()
    {
        // $$…$$ is display math; the inline regex explicitly excludes it so
        // the block regex (not yet added) can handle it separately. For now
        // the detector still flags it via the equation-like heuristic, so we
        // assert on overall detection, not on the single-dollar regex.
        var result = _detector.Analyze("The answer is $$x = 5$$.", "english");
        Assert.True(result.HasMathContent); // EquationLike catches "= 5" / "x = 5"
    }

    [Fact]
    public void LatexParenDelimiters_AreDetected()
    {
        var result = _detector.Analyze(
            @"Compute \(\sin(\theta) = 0.5\) for the given triangle.",
            "english");

        Assert.True(result.HasMathContent);
        Assert.Contains(
            result.ExtractedExpressions,
            e => e.Contains("sin", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LatexBracketDelimiters_AreDetected()
    {
        var result = _detector.Analyze(
            @"Solve \[2x + 3 = 11\] for x.",
            "english");

        Assert.True(result.HasMathContent);
        Assert.Contains(
            result.ExtractedExpressions,
            e => e.Contains("2x + 3 = 11"));
    }

    // ── Equation-like heuristic (word problems without LaTeX) ───────────

    [Theory]
    [InlineData("If 3 + 4 equals seven, what is 2 + 2?")]
    [InlineData("The speed equals 60 km/h when t = 5.")]
    [InlineData("Compute \\frac{1}{2} + \\frac{1}{3}.")]
    [InlineData("The side length is \\sqrt{16}.")]
    [InlineData("y = 2x is a linear function.")]
    public void EquationLikeHeuristic_CatchesWordProblems_InNonMathSubject(string body)
    {
        var result = _detector.Analyze(body, "english");
        Assert.True(result.HasMathContent);
    }

    [Theory]
    [InlineData("sin(x)", "Should detect sin() in prose")]
    [InlineData("cos(45)", "Should detect cos() in prose")]
    [InlineData("log(10)", "Should detect log() in prose")]
    [InlineData("ln(e)", "Should detect ln() in prose")]
    public void TrigAndLogFunctions_TriggerDetection(string expression, string reason)
    {
        var result = _detector.Analyze($"The value of {expression} is positive.", "english");
        Assert.True(result.HasMathContent, reason);
    }

    // ── Non-math prose must NOT flag ────────────────────────────────────

    [Theory]
    [InlineData("Who wrote Hamlet?")]
    [InlineData("Explain the meaning of the poem.")]
    [InlineData("List three causes of World War I.")]
    [InlineData("What is the capital of France?")]
    public void PureProse_InNonMathSubject_IsNotMath(string body)
    {
        var result = _detector.Analyze(body, "english");
        Assert.False(result.HasMathContent);
        Assert.Empty(result.ExtractedExpressions);
    }

    // ── Defensive nulls ─────────────────────────────────────────────────

    [Fact]
    public void NullBody_DoesNotThrow_AndIsNotMath()
    {
        var result = _detector.Analyze(null!, "english");
        Assert.False(result.HasMathContent);
    }

    [Fact]
    public void NullSubject_DoesNotThrow_FallsBackToHeuristic()
    {
        var result = _detector.Analyze("The answer is $x = 3$.", null!);
        Assert.True(result.HasMathContent);
    }

    // ── HasMathContent convenience probe ────────────────────────────────

    [Fact]
    public void HasMathContent_Convenience_DelegatesToAnalyze()
    {
        IMathContentDetector d = _detector;
        Assert.True(d.HasMathContent("$x+1=2$", "english"));
        Assert.False(d.HasMathContent("Write an essay.", "english"));
        Assert.True(d.HasMathContent("Write an essay.", "math"));
    }

    // ── Multiple expressions are all collected ──────────────────────────

    [Fact]
    public void MultipleLatexExpressions_AreAllExtracted()
    {
        var result = _detector.Analyze(
            @"First solve \(x + 1 = 2\), then solve \[y^2 = 4\], finally $z = 0$.",
            "english");

        Assert.True(result.HasMathContent);
        Assert.True(result.ExtractedExpressions.Count >= 3,
            $"Expected 3+ expressions, found {result.ExtractedExpressions.Count}");
    }
}
