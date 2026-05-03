// =============================================================================
// Cena Platform — Misconception taxonomy integrity tests (EPIC-PRR-J PRR-370/371/374)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class MisconceptionTaxonomyTests
{
    [Fact]
    public void AllTemplatesHaveAllThreeLocales()
    {
        foreach (var t in BagrutMath4MisconceptionTaxonomy.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.ExplanationHe), $"{t.TemplateId} missing HE");
            Assert.False(string.IsNullOrWhiteSpace(t.ExplanationAr), $"{t.TemplateId} missing AR");
            Assert.False(string.IsNullOrWhiteSpace(t.ExplanationEn), $"{t.TemplateId} missing EN");
        }
    }

    [Fact]
    public void TemplateIdsAreUnique()
    {
        var ids = BagrutMath4MisconceptionTaxonomy.All.Select(t => t.TemplateId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void MinConfidenceIsAlwaysInZeroOne()
    {
        foreach (var t in BagrutMath4MisconceptionTaxonomy.All)
        {
            Assert.InRange(t.MinConfidence, 0.0, 1.0);
        }
    }

    [Fact]
    public void ScorerReturnsMatchForEveryCoveredBreakType()
    {
        var scorer = new TemplateMatchingScorer();
        foreach (var t in BagrutMath4MisconceptionTaxonomy.All)
        {
            var sig = new CasBreakSignature(t.BreakType, "x", "y", string.Empty);
            var m = scorer.PickBestMatch(sig);
            Assert.NotNull(m);
            Assert.Equal(t.BreakType, m.Template.BreakType);
        }
    }

    [Fact]
    public void ScorerReturnsNullForUncoveredBreakType()
    {
        var scorer = new TemplateMatchingScorer();
        var sig = new CasBreakSignature(MisconceptionBreakType.Other, "x", "y", string.Empty);
        Assert.Null(scorer.PickBestMatch(sig));
    }

    [Fact]
    public void ScorerThrowsForNullInput()
    {
        var scorer = new TemplateMatchingScorer();
        Assert.Throws<ArgumentNullException>(() => scorer.PickBestMatch(null!));
    }
}
