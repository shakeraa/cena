// =============================================================================
// Cena Platform — Stem Solution Extractor Tests (RDY-038)
// =============================================================================

using Cena.Actors.Cas;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public class StemSolutionExtractorTests
{
    private readonly StemSolutionExtractor _sut = new();

    [Theory]
    [InlineData("Solve 2x + 3 = 7", "2x + 3", "7")]
    [InlineData("Find x if x/2 = 4", "x/2", "4")]
    [InlineData("x^2 = 4", "x^2", "4")]
    public void EquationStem_ExtractsLhsAndRhs(string stem, string lhs, string rhs)
    {
        var result = _sut.Extract(stem, "math");
        var eq = Assert.IsType<StemExtraction.Equation>(result);
        Assert.Equal(lhs, eq.Lhs);
        Assert.Equal(rhs, eq.Rhs);
    }

    [Theory]
    [InlineData("Evaluate 2 + 3")]
    [InlineData("Simplify (x+1)^2")]
    [InlineData("Factor x^2 - 1")]
    public void DirectExpressionStem_ExtractsExpression(string stem)
    {
        var result = _sut.Extract(stem, "math");
        Assert.IsType<StemExtraction.ExpressionOnly>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("What is the capital of France?")]
    [InlineData("Describe the Pythagorean theorem.")]
    public void ProseOrEmpty_ReturnsNull(string stem)
    {
        // Non-extractable stems MUST return null so the gate falls through
        // to Unverifiable — NOT to Verified-on-parseability (the ADR-0002
        // violation this extractor exists to close).
        var result = _sut.Extract(stem, "math");
        Assert.Null(result);
    }

    [Fact]
    public void EquationWithNoVariable_ReturnsNull()
    {
        // "2 + 3 = 5" is an assertion, not a solve-for-x equation.
        var result = _sut.Extract("2 + 3 = 5", "math");
        Assert.Null(result);
    }

    [Fact]
    public void MultipleEquals_DecorativeUse_ReturnsNull()
    {
        // Multi-equation stems are ambiguous — extractor declines.
        var result = _sut.Extract("x = 2, y = 3", "math");
        Assert.Null(result);
    }
}
