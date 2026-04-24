// =============================================================================
// PP-014 / PP-015: Arabic Math Normalizer Tests
// =============================================================================

using Cena.Infrastructure.Localization;

namespace Cena.Infrastructure.Tests.Localization;

public class ArabicMathNormalizerTests
{
    // ── Existing math behavior ──

    [Theory]
    [InlineData("س + ص = ع", "x + y = z")]
    [InlineData("٢ × ٣ = ٦", "2 * 3 = 6")]
    [InlineData("جذر(٤)", "sqrt(4)")]
    public void Normalize_MathContext_BasicConversion(string input, string expected)
    {
        Assert.Equal(expected, ArabicMathNormalizer.Normalize(input));
    }

    // ── PP-014: Physics context ──

    [Fact]
    public void Normalize_PhysicsContext_ForceEquation()
    {
        // ق = م × ت → F = m × a (not F = m × t)
        var result = ArabicMathNormalizer.Normalize("ق = م × ت", NormalizationContext.Physics);
        Assert.Equal("F = m * a", result);
    }

    [Fact]
    public void Normalize_MathContext_SameInput_DifferentResult()
    {
        // Same input in math context: ت → t
        var result = ArabicMathNormalizer.Normalize("ق = م × ت", NormalizationContext.Mathematics);
        Assert.Contains("t", result);
        Assert.DoesNotContain("a", result.Replace("sqrt", ""));
    }

    [Theory]
    [InlineData("ط = م × ج × ح²", "E = m * V * v²")]
    [InlineData("ش = ق × س", "W = F * x")]
    public void Normalize_PhysicsContext_Variables(string input, string expected)
    {
        Assert.Equal(expected, ArabicMathNormalizer.Normalize(input, NormalizationContext.Physics));
    }

    [Theory]
    [InlineData("نيوتن", "N")]
    [InlineData("جول", "J")]
    [InlineData("واط", "W")]
    public void Normalize_PhysicsContext_Units(string input, string expected)
    {
        Assert.Equal(expected, ArabicMathNormalizer.Normalize(input, NormalizationContext.Physics));
    }

    [Fact]
    public void Normalize_PhysicsContext_ThetaAngle()
    {
        var result = ArabicMathNormalizer.Normalize("ث", NormalizationContext.Physics);
        Assert.Equal("θ", result);
    }

    // ── PP-015: Bidi-safe single-pass normalization ──

    [Fact]
    public void Normalize_SinglePass_NoIntermediateBidiCorruption()
    {
        // س² + ص² should normalize cleanly without intermediate mixed-direction states
        var result = ArabicMathNormalizer.Normalize("س² + ص²");
        Assert.Equal("x² + y²", result);
    }

    [Fact]
    public void Normalize_SinglePass_NoBidiControlChars()
    {
        // Build input programmatically to avoid invisible bidi markers in source
        var input = "\u0633\u00B2 + \u0635\u00B2 = \u0639\u00B2"; // س² + ص² = ع²
        var result = ArabicMathNormalizer.Normalize(input);
        Assert.Equal("x\u00B2 + y\u00B2 = z\u00B2", result);
    }

    [Fact]
    public void Normalize_IncrementalKeystroke_EachStateCorrect()
    {
        // Build input programmatically to avoid bidi markers
        var fullInput = "\u0633\u00B2 + \u0635\u00B2"; // س² + ص²
        var keystroke = "";

        foreach (var c in fullInput)
        {
            keystroke += c;
            var result = ArabicMathNormalizer.Normalize(keystroke);
            // Should not contain any Arabic characters that have mappings
            Assert.DoesNotContain("\u0633", result); // no remaining س
        }

        Assert.Equal("x\u00B2 + y\u00B2", ArabicMathNormalizer.Normalize(fullInput));
    }

    // ── Edge cases ──

    [Fact]
    public void Normalize_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", ArabicMathNormalizer.Normalize(""));
    }

    [Fact]
    public void Normalize_NullInput_ReturnsNull()
    {
        Assert.Null(ArabicMathNormalizer.Normalize(null!));
    }

    [Fact]
    public void Normalize_PureLatinInput_Unchanged()
    {
        Assert.Equal("x + y = z", ArabicMathNormalizer.Normalize("x + y = z"));
    }

    [Fact]
    public void NeedsNormalization_PhysicsVars_ReturnsTrue()
    {
        Assert.True(ArabicMathNormalizer.NeedsNormalization("ق = م × ت"));
    }
}
