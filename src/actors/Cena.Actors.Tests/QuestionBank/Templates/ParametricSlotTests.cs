// =============================================================================
// Cena Platform — ParametricSlot unit tests (prr-200)
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class ParametricSlotTests
{
    [Fact]
    public void Integer_Draw_HonoursExclude()
    {
        var slot = new ParametricSlot
        {
            Name = "a", Kind = ParametricSlotKind.Integer,
            IntegerMin = 0, IntegerMax = 4,
            IntegerExclude = new[] { 0, 2, 4 }
        };
        var rng = new Random(42);
        for (var i = 0; i < 100; i++)
        {
            var v = slot.Draw(rng);
            Assert.Equal(ParametricSlotKind.Integer, v.Kind);
            Assert.DoesNotContain(v.Numerator, new long[] { 0, 2, 4 });
            Assert.InRange(v.Numerator, 0, 4);
        }
    }

    [Fact]
    public void Integer_Draw_ThrowsWhenExcludeCoversRange()
    {
        var slot = new ParametricSlot
        {
            Name = "a", Kind = ParametricSlotKind.Integer,
            IntegerMin = 0, IntegerMax = 1,
            IntegerExclude = new[] { 0, 1 }
        };
        Assert.Throws<InvalidOperationException>(() => slot.Draw(new Random(0)));
    }

    [Fact]
    public void Rational_Draw_ReducesByDefault()
    {
        var slot = new ParametricSlot
        {
            Name = "r", Kind = ParametricSlotKind.Rational,
            NumeratorMin = 6, NumeratorMax = 6,
            DenominatorMin = 4, DenominatorMax = 4,
            ReduceRational = true
        };
        var v = slot.Draw(new Random(1));
        Assert.Equal(3, v.Numerator);
        Assert.Equal(2, v.Denominator);
    }

    [Fact]
    public void Rational_Draw_DoesNotEmitZeroDenominator()
    {
        var slot = new ParametricSlot
        {
            Name = "r", Kind = ParametricSlotKind.Rational,
            NumeratorMin = 1, NumeratorMax = 5,
            DenominatorMin = -2, DenominatorMax = 2
        };
        var rng = new Random(99);
        for (var i = 0; i < 200; i++)
        {
            var v = slot.Draw(rng);
            Assert.NotEqual(0, v.Denominator);
        }
    }

    [Fact]
    public void Choice_Draw_StaysInChoiceList()
    {
        var slot = new ParametricSlot
        {
            Name = "var", Kind = ParametricSlotKind.Choice,
            Choices = new[] { "x", "y", "t" }
        };
        var rng = new Random(7);
        for (var i = 0; i < 50; i++)
        {
            var v = slot.Draw(rng);
            Assert.Contains(v.ChoiceValue, new[] { "x", "y", "t" });
        }
    }

    [Fact]
    public void Validate_RejectsInvalidShapes()
    {
        var zeroRange = new ParametricSlot
        {
            Name = "a", Kind = ParametricSlotKind.Integer,
            IntegerMin = 5, IntegerMax = 1
        };
        Assert.Throws<ArgumentException>(zeroRange.Validate);

        var emptyChoices = new ParametricSlot
        {
            Name = "v", Kind = ParametricSlotKind.Choice,
            Choices = Array.Empty<string>()
        };
        Assert.Throws<ArgumentException>(emptyChoices.Validate);
    }

    [Fact]
    public void SlotValue_ToExpressionString_ParenthesisesRationals()
    {
        Assert.Equal("(1/3)", ParametricSlotValue.Rational("r", 1, 3).ToExpressionString());
        Assert.Equal("7", ParametricSlotValue.Integer("i", 7).ToExpressionString());
    }

    [Fact]
    public void SlotValue_IsIntegral_CatchesReducedIntegers()
    {
        Assert.True(ParametricSlotValue.Rational("r", 6, 3).IsIntegral());
        Assert.False(ParametricSlotValue.Rational("r", 1, 3).IsIntegral());
    }
}
