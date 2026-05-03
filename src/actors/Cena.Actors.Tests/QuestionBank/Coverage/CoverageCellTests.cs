// =============================================================================
// Cena Platform — Coverage Cell tests (prr-201)
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

public sealed class CoverageCellTests
{
    [Fact]
    public void Address_IsStableAcrossConstructions()
    {
        var a = new CoverageCell
        {
            Track = TemplateTrack.FourUnit,
            Subject = "math",
            Topic = "algebra.linear",
            Difficulty = TemplateDifficulty.Medium,
            Methodology = TemplateMethodology.Halabi,
            QuestionType = "multiple-choice",
            Language = "en"
        };
        var b = new CoverageCell
        {
            Track = TemplateTrack.FourUnit,
            Subject = "math",
            Topic = "algebra.linear",
            Difficulty = TemplateDifficulty.Medium,
            Methodology = TemplateMethodology.Halabi,
            QuestionType = "multiple-choice",
            Language = "en"
        };
        Assert.Equal(a.Address, b.Address);
    }

    [Fact]
    public void Address_ChangesWithMethodology()
    {
        var halabi = new CoverageCell
        {
            Track = TemplateTrack.FourUnit,
            Subject = "math",
            Topic = "algebra.linear",
            Difficulty = TemplateDifficulty.Medium,
            Methodology = TemplateMethodology.Halabi,
            QuestionType = "multiple-choice"
        };
        var rabi = halabi with { Methodology = TemplateMethodology.Rabinovitch };
        Assert.NotEqual(halabi.Address, rabi.Address);
    }

    [Fact]
    public void Address_IsUrlSafe()
    {
        var cell = new CoverageCell
        {
            Track = TemplateTrack.FiveUnit,
            Subject = "Math (pre-calc)",
            Topic = "Linear / Quadratic",
            Difficulty = TemplateDifficulty.Hard,
            Methodology = TemplateMethodology.Rabinovitch,
            QuestionType = "free-text",
            Language = "he"
        };
        foreach (var ch in cell.Address)
        {
            Assert.True(
                char.IsLetterOrDigit(ch) || ch == '-' || ch == '/' ,
                $"unsafe char '{ch}' in address {cell.Address}");
        }
    }
}
