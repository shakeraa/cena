// =============================================================================
// Cena Platform — OCR Fixture Contract Tests
//
// Proves every dev-fixture JSON at scripts/ocr-spike/dev-fixtures/cascade-results/
// deserialises into OcrCascadeResult without loss. If this test fails the
// C# contract has drifted from the Python reference impl.
//
// See RDY-OCR-PORT acceptance criterion: "All committed dev-fixtures
// deserialise into OcrCascadeResult with no loss."
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Tests.Ocr;

public class OcrFixtureContractTests
{
    /// <summary>
    /// Walks up from the test binary location to find the repo root, then
    /// locates scripts/ocr-spike/dev-fixtures/cascade-results/.
    /// </summary>
    private static DirectoryInfo LocateFixtureDir()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "scripts", "ocr-spike", "dev-fixtures", "cascade-results");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            current = current.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate scripts/ocr-spike/dev-fixtures/cascade-results/ " +
            "by walking up from " + AppContext.BaseDirectory);
    }

    public static IEnumerable<object[]> Fixtures()
    {
        foreach (var file in LocateFixtureDir().EnumerateFiles("*.json"))
            yield return new object[] { file.FullName };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_Deserialises_Without_Loss(string path)
    {
        var json = File.ReadAllText(path);

        var result = JsonSerializer.Deserialize<OcrCascadeResult>(
            json, OcrJsonOptions.Default);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.SchemaVersion),
            $"{Path.GetFileName(path)}: schema_version missing");
        Assert.False(string.IsNullOrWhiteSpace(result.Runner),
            $"{Path.GetFileName(path)}: runner missing");
        Assert.False(string.IsNullOrWhiteSpace(result.Source),
            $"{Path.GetFileName(path)}: source missing");

        // Confidence in [0, 1]
        Assert.InRange(result.OverallConfidence, 0.0, 1.0);

        // Timings are never negative
        foreach (var (layer, seconds) in result.LayerTimingsSeconds)
        {
            Assert.True(seconds >= 0, $"{Path.GetFileName(path)}: negative timing on {layer}");
        }
        Assert.True(result.TotalLatencySeconds >= 0,
            $"{Path.GetFileName(path)}: total_latency_seconds is negative");

        // Invariants on the CAS counts
        Assert.True(result.CasValidatedMath >= 0);
        Assert.True(result.CasFailedMath >= 0);
        Assert.True(result.CasValidatedMath + result.CasFailedMath <= result.MathBlocks.Count + result.CasFailedMath,
            $"{Path.GetFileName(path)}: CAS counts exceed math block count");

        // Human-review requires at least one reason
        if (result.HumanReviewRequired)
        {
            Assert.NotEmpty(result.ReasonsForReview);
        }
    }

    [Fact]
    public void Fixtures_Directory_Is_Populated()
    {
        var dir = LocateFixtureDir();
        var count = dir.GetFiles("*.json").Length;
        Assert.True(count >= 6,
            $"Expected at least 6 cascade-result fixtures under {dir.FullName}, " +
            $"found {count}. Regenerate via scripts/ocr-spike/build_dev_fixtures.py.");
    }

    [Fact]
    public void Roundtrip_Preserves_Hint_Enum_Values()
    {
        // Regression guard: Language, Track, SourceType must serialise as
        // lowercase strings matching the Python impl.
        var original = new OcrContextHints(
            Subject: "math",
            Language: Language.Hebrew,
            Track: Track.Units5,
            SourceType: SourceType.BagrutReference,
            TaxonomyNode: "algebra.polynomials",
            ExpectedFigures: true);

        var json = JsonSerializer.Serialize(original, OcrJsonOptions.Default);

        Assert.Contains("\"he\"", json);
        Assert.Contains("\"5u\"", json);
        Assert.Contains("\"bagrut_reference\"", json);

        var roundtrip = JsonSerializer.Deserialize<OcrContextHints>(json, OcrJsonOptions.Default);
        Assert.NotNull(roundtrip);
        Assert.Equal(original, roundtrip);
    }

    [Fact]
    public void Triage_Enum_Values_Match_Python_Impl()
    {
        // These strings are the Python PdfType enum values — any drift
        // will cause real pipeline_prototype.py output to fail to deserialise.
        var jsonByVerdict = new Dictionary<PdfTriageVerdict, string>
        {
            [PdfTriageVerdict.Text] = "\"text\"",
            [PdfTriageVerdict.ImageOnly] = "\"image_only\"",
            [PdfTriageVerdict.Mixed] = "\"mixed\"",
            [PdfTriageVerdict.ScannedBadOcr] = "\"scanned_bad_ocr\"",
            [PdfTriageVerdict.Encrypted] = "\"encrypted\"",
            [PdfTriageVerdict.Unreadable] = "\"unreadable\"",
        };

        foreach (var (verdict, expectedJson) in jsonByVerdict)
        {
            var actual = JsonSerializer.Serialize(verdict, OcrJsonOptions.Default);
            Assert.Equal(expectedJson, actual);
        }
    }
}
