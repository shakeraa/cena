// =============================================================================
// Cena Platform — CatalogBackedQuestionPaperCatalogValidator tests (prr-243)
//
// Verifies that the catalog-backed validator correctly gates paper codes
// against the loaded YAML catalog snapshot.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Student.Api.Host.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Catalog;

public sealed class CatalogBackedQuestionPaperCatalogValidatorTests
{
    private static CatalogSnapshot TestSnapshot()
    {
        var target = new CatalogTarget(
            ExamCode: "BAGRUT_MATH_5U",
            Family: "BAGRUT",
            Region: "IL-Bagrut-Hebrew",
            Track: "5U",
            Units: 5,
            Regulator: "ministry_of_education",
            MinistrySubjectCode: "035",
            MinistryQuestionPaperCodes: new[] { "035581", "035582", "035583" },
            Availability: "launch",
            ItemBankStatus: "full",
            PassbackEligible: true,
            DefaultLeadDays: 180,
            Sittings: Array.Empty<CatalogSitting>(),
            Display: new Dictionary<string, LocalizedEntry>
            {
                ["en"] = new("Bagrut Math 5U", null),
            },
            Topics: Array.Empty<CatalogTopic>());

        return new CatalogSnapshot(
            CatalogVersion: "2026.04.21-01",
            LoadedAt: DateTimeOffset.UtcNow,
            FamilyOrder: new[] { "BAGRUT" },
            Families: new Dictionary<string, IReadOnlyList<string>>
            {
                ["BAGRUT"] = new[] { "BAGRUT_MATH_5U" },
            },
            TargetsByCode: new Dictionary<string, CatalogTarget>
            {
                ["BAGRUT_MATH_5U"] = target,
            });
    }

    private static CatalogBackedQuestionPaperCatalogValidator Build()
    {
        var svc = ExamCatalogService.ForTests(
            TestSnapshot(),
            logger: NullLogger<ExamCatalogService>.Instance);
        return new CatalogBackedQuestionPaperCatalogValidator(svc);
    }

    [Theory]
    [InlineData("035581", true)]
    [InlineData("035582", true)]
    [InlineData("035583", true)]
    [InlineData("035584", false)] // not a Ministry 5U code
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void IsPaperCodeValid_matches_catalog(string paperCode, bool expected)
    {
        var v = Build();
        var ok = v.IsPaperCodeValid(
            new ExamCode("BAGRUT_MATH_5U"),
            new TrackCode("5U"),
            paperCode);
        Assert.Equal(expected, ok);
    }

    [Fact]
    public void IsPaperCodeValid_rejects_unknown_exam_code()
    {
        var v = Build();
        var ok = v.IsPaperCodeValid(
            new ExamCode("BAGRUT_UNKNOWN"),
            new TrackCode("5U"),
            "035581");
        Assert.False(ok);
    }

    [Fact]
    public void IsPaperCodeValid_rejects_track_mismatch()
    {
        var v = Build();
        // Catalog track is "5U"; caller claims "4U" — rejected.
        var ok = v.IsPaperCodeValid(
            new ExamCode("BAGRUT_MATH_5U"),
            new TrackCode("4U"),
            "035581");
        Assert.False(ok);
    }

    [Fact]
    public void IsPaperCodeValid_rejects_null_track_when_catalog_has_one()
    {
        var v = Build();
        var ok = v.IsPaperCodeValid(
            new ExamCode("BAGRUT_MATH_5U"),
            null,
            "035581");
        Assert.False(ok);
    }

    [Fact]
    public void AllowAll_default_accepts_any_nonempty_code()
    {
        var v = AllowAllQuestionPaperCatalogValidator.Instance;
        Assert.True(v.IsPaperCodeValid(new ExamCode("x"), null, "any"));
        Assert.False(v.IsPaperCodeValid(new ExamCode("x"), null, ""));
        Assert.False(v.IsPaperCodeValid(new ExamCode("x"), null, " "));
    }
}
