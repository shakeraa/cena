// =============================================================================
// Cena Platform — ParametricTemplate validation tests (prr-200)
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class ParametricTemplateTests
{
    private static ParametricTemplate Valid() => new()
    {
        Id = "t1", Version = 1,
        Subject = "math", Topic = "algebra",
        Track = TemplateTrack.FourUnit,
        Difficulty = TemplateDifficulty.Easy,
        Methodology = TemplateMethodology.Halabi,
        StemTemplate = "{a}", SolutionExpr = "a",
        AcceptShapes = AcceptShape.Integer,
        Slots = new[]
        {
            new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer,
                                 IntegerMin = 1, IntegerMax = 3 }
        }
    };

    [Fact]
    public void Validate_Accepts_WellFormedTemplate()
    {
        Valid().Validate();   // should not throw
    }

    [Fact]
    public void Validate_RejectsEmptyId()
    {
        var t = Valid() with { Id = "" };
        Assert.Throws<ArgumentException>(t.Validate);
    }

    [Fact]
    public void Validate_RejectsDuplicateSlotNames()
    {
        var t = Valid() with
        {
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 2 },
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 3, IntegerMax = 4 }
            }
        };
        Assert.Throws<ArgumentException>(t.Validate);
    }

    [Fact]
    public void Validate_RejectsStemReferencingUndeclaredSlot()
    {
        var t = Valid() with { StemTemplate = "{undeclared}" };
        Assert.Throws<ArgumentException>(t.Validate);
    }

    [Fact]
    public void Validate_AcceptsDoubleBraceLiteral()
    {
        var t = Valid() with { StemTemplate = "{{literal}} {a}" };
        t.Validate();
    }

    [Fact]
    public void ExtractReferencedSlotNames_IgnoresEscapedBraces()
    {
        var names = ParametricTemplate.ExtractReferencedSlotNames("{{escaped}} {real}").ToArray();
        Assert.Equal(new[] { "real" }, names);
    }

    [Fact]
    public void Validate_RejectsAcceptShapesNone()
    {
        var t = Valid() with { AcceptShapes = AcceptShape.None };
        Assert.Throws<ArgumentException>(t.Validate);
    }
}
