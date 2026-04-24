// =============================================================================
// Cena Platform — ParametricRenderHelpers unit tests (prr-200)
// Substitution + zero-divisor screen + shape classification.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class ParametricRenderHelpersTests
{
    [Fact]
    public void SubstituteStem_ReplacesSingleIntegerSlot()
    {
        var slots = new Dictionary<string, ParametricSlotValue>
        {
            ["a"] = ParametricSlotValue.Integer("a", 5)
        };
        var stem = ParametricRenderHelpers.SubstituteStem("Compute {a} + 1", slots);
        Assert.Equal("Compute 5 + 1", stem);
    }

    [Fact]
    public void SubstituteStem_EscapesDoubleBraces()
    {
        var slots = new Dictionary<string, ParametricSlotValue>();
        Assert.Equal("{literal}", ParametricRenderHelpers.SubstituteStem("{{literal}}", slots));
    }

    [Fact]
    public void SubstituteStem_IntegralRational_RendersAsInteger()
    {
        var slots = new Dictionary<string, ParametricSlotValue>
        {
            ["a"] = ParametricSlotValue.Rational("a", 6, 3)
        };
        Assert.Equal("a=2", ParametricRenderHelpers.SubstituteStem("a={a}", slots));
    }

    [Fact]
    public void SubstituteStem_NonIntegralRational_RendersParenthesised()
    {
        var slots = new Dictionary<string, ParametricSlotValue>
        {
            ["a"] = ParametricSlotValue.Rational("a", 1, 3)
        };
        // Stem rendering uses ToExpressionString when non-integral.
        Assert.Equal("a=(1/3)", ParametricRenderHelpers.SubstituteStem("a={a}", slots));
    }

    [Fact]
    public void SubstituteStem_Throws_OnUnknownSlot()
    {
        var slots = new Dictionary<string, ParametricSlotValue>();
        Assert.Throws<FormatException>(() =>
            ParametricRenderHelpers.SubstituteStem("{x}", slots));
    }

    [Fact]
    public void SubstituteSlots_WordBoundaryAware()
    {
        // slot "a" must NOT replace the "a" inside "abs".
        var slots = new Dictionary<string, ParametricSlotValue>
        {
            ["a"] = ParametricSlotValue.Integer("a", 7)
        };
        Assert.Equal("abs(7) + 3", ParametricRenderHelpers.SubstituteSlots("abs(a) + 3", slots));
    }

    [Fact]
    public void SubstituteSlots_Rejects_DisallowedCharacters()
    {
        var slots = new Dictionary<string, ParametricSlotValue>();
        Assert.Throws<FormatException>(() =>
            ParametricRenderHelpers.SubstituteSlots("__import__('os')", slots));
    }

    [Fact]
    public void ContainsLiteralDivideByZero_CatchesPlainCase()
    {
        Assert.True(ParametricRenderHelpers.ContainsLiteralDivideByZero("(c-b) / 0"));
        Assert.True(ParametricRenderHelpers.ContainsLiteralDivideByZero("5/0+2"));
    }

    [Fact]
    public void ContainsLiteralDivideByZero_IgnoresNonzeroDenominators()
    {
        Assert.False(ParametricRenderHelpers.ContainsLiteralDivideByZero("3/5 + 1"));
        Assert.False(ParametricRenderHelpers.ContainsLiteralDivideByZero("1/02"));
        Assert.False(ParametricRenderHelpers.ContainsLiteralDivideByZero("1/0.5"));
    }

    [Theory]
    [InlineData("7", ParametricRenderHelpers.AnswerShape.Integer)]
    [InlineData("-3", ParametricRenderHelpers.AnswerShape.Integer)]
    [InlineData("7/3", ParametricRenderHelpers.AnswerShape.Rational)]
    [InlineData("6/3", ParametricRenderHelpers.AnswerShape.Integer)]
    [InlineData("(7/3)", ParametricRenderHelpers.AnswerShape.Rational)]
    [InlineData("1.5", ParametricRenderHelpers.AnswerShape.Decimal)]
    [InlineData("sqrt(2)", ParametricRenderHelpers.AnswerShape.Symbolic)]
    [InlineData("nan", ParametricRenderHelpers.AnswerShape.NonFinite)]
    [InlineData("Infinity", ParametricRenderHelpers.AnswerShape.NonFinite)]
    public void ClassifyAnswerShape_Bucketises(string input, ParametricRenderHelpers.AnswerShape expected)
    {
        Assert.Equal(expected, ParametricRenderHelpers.ClassifyAnswerShape(input));
    }

    [Fact]
    public void IsShapeAccepted_RespectsFlags()
    {
        Assert.True(ParametricRenderHelpers.IsShapeAccepted(
            ParametricRenderHelpers.AnswerShape.Integer, AcceptShape.Integer));
        Assert.False(ParametricRenderHelpers.IsShapeAccepted(
            ParametricRenderHelpers.AnswerShape.Rational, AcceptShape.Integer));
        Assert.False(ParametricRenderHelpers.IsShapeAccepted(
            ParametricRenderHelpers.AnswerShape.NonFinite, AcceptShape.Any));
    }
}
