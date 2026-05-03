// =============================================================================
// PP-004: CAS LLM Output Verifier — Math Claim Extraction Tests
// =============================================================================

using Cena.Actors.Cas;

namespace Cena.Actors.Tests.Cas;

public class CasLlmOutputVerifierTests
{
    // =========================================================================
    // Existing behaviour — regression guards
    // =========================================================================

    [Theory]
    [InlineData("The value is $x + 2$", "x + 2")]
    [InlineData("Consider $\\frac{1}{2}$", "\\frac{1}{2}")]
    public void ExtractMathClaims_InlineDollarSign_Extracted(string text, string expected)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression == expected);
    }

    [Fact]
    public void ExtractMathClaims_BlockDollarSign_Extracted()
    {
        var text = "Result: $$x^2 + 1 = 0$$";
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression == "x^2 + 1 = 0");
    }

    [Theory]
    [InlineData("x = 42", "x = 42")]
    [InlineData("y = -3.14", "y = -3.14")]
    public void ExtractMathClaims_BareEquation_Extracted(string text, string expected)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression == expected);
    }

    // =========================================================================
    // PP-004 gap 1: English number words
    // =========================================================================

    [Theory]
    [InlineData("the answer is three")]
    [InlineData("The answer is twelve")]
    [InlineData("equals twenty")]
    [InlineData("is equal to five")]
    public void ExtractMathClaims_EnglishNumberWord_Extracted(string text)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.NotEmpty(claims);
    }

    // =========================================================================
    // PP-004 gap 2: Bare LaTeX without dollar signs
    // =========================================================================

    [Theory]
    [InlineData("Consider \\frac{1}{2} here")]
    [InlineData("The value is \\sqrt{9} obviously")]
    public void ExtractMathClaims_BareLatex_Extracted(string text)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.NotEmpty(claims);
    }

    // =========================================================================
    // PP-004 gap 3: Unicode math symbols
    // =========================================================================

    [Theory]
    [InlineData("√9 = 3")]
    [InlineData("x² + y² = 25")]
    public void ExtractMathClaims_UnicodeMath_Extracted(string text)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.NotEmpty(claims);
    }

    // =========================================================================
    // PP-004 gap 4: Multi-line LaTeX environments
    // =========================================================================

    [Fact]
    public void ExtractMathClaims_AlignEnvironment_Extracted()
    {
        var text = "Here:\n\\begin{align}\nx &= 1 \\\\\ny &= 2\n\\end{align}\ndone";
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression.Contains("\\begin{align}"));
    }

    [Fact]
    public void ExtractMathClaims_EquationEnvironment_Extracted()
    {
        var text = "\\begin{equation}E = mc^2\\end{equation}";
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression.Contains("\\begin{equation}"));
    }

    // =========================================================================
    // PP-004 gap 5: Code-fence math
    // =========================================================================

    [Theory]
    [InlineData("Try `x = 42 + 1`", "x = 42 + 1")]
    [InlineData("Result is `√9 = 3`", "√9 = 3")]
    public void ExtractMathClaims_CodeFenceMath_Extracted(string text, string expected)
    {
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Contains(claims, c => c.Expression == expected);
    }

    // =========================================================================
    // PP-004: IsAnswerLeak — false positive fix
    // =========================================================================

    [Fact]
    public void IsAnswerLeak_SubstringFalsePositive_NotDetected()
    {
        // "x = 31" should NOT leak answer "3"
        Assert.False(CasLlmOutputVerifier.IsAnswerLeak("x=31", "3"));
    }

    [Fact]
    public void IsAnswerLeak_SubstringFalsePositive_13_NotDetected()
    {
        // "x = 13" should NOT leak answer "3"
        Assert.False(CasLlmOutputVerifier.IsAnswerLeak("x=13", "3"));
    }

    [Fact]
    public void IsAnswerLeak_ExactMatch_Detected()
    {
        Assert.True(CasLlmOutputVerifier.IsAnswerLeak("x=3", "3"));
    }

    [Fact]
    public void IsAnswerLeak_NumericalEquivalence_Detected()
    {
        Assert.True(CasLlmOutputVerifier.IsAnswerLeak("3.0", "3"));
    }

    [Fact]
    public void IsAnswerLeak_DifferentValue_NotDetected()
    {
        Assert.False(CasLlmOutputVerifier.IsAnswerLeak("x=5", "3"));
    }

    // =========================================================================
    // No duplicate extraction — block math should not also appear as inline
    // =========================================================================

    [Fact]
    public void ExtractMathClaims_BlockAndInline_NoDuplicate()
    {
        var text = "$$x + 1$$";
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.Single(claims);
    }

    // =========================================================================
    // Mixed content — multiple claim types in one response
    // =========================================================================

    [Fact]
    public void ExtractMathClaims_MixedContent_AllExtracted()
    {
        var text = "We know $x + 1$ and the answer is three, also x = 5";
        var claims = CasLlmOutputVerifier.ExtractMathClaims(text);
        Assert.True(claims.Count >= 3, $"Expected >= 3 claims, got {claims.Count}");
    }
}
