// =============================================================================
// Cena Platform — OCR Cascade Fixture Regression (RDY-OCR-REGRESSION / Phase 4)
//
// Guards the OcrCascadeResult contract against two regression classes:
//
//   1) Schema drift — the Python reference implementation's dev-fixtures
//      (scripts/ocr-spike/dev-fixtures/cascade-results/*.json) are the
//      authoritative shape. If a C# contract change breaks deserialization
//      of any fixture, this test fails. That's the hard contract guard.
//
//   2) Quality drift — each fixture has a known-good
//      (text_blocks_count, math_blocks_count, overall_confidence) tuple.
//      If a C# code change produces a delta > 5 percentage points on any
//      fixture, the test fails ("5pp drop budget" per roadmap).
//
// The test does NOT run the cascade against raw inputs — that needs real
// Surya/pix2tex/Tesseract. Instead it validates the reference fixtures
// round-trip through our C# contract and that our baseline table stays
// in sync with the Python reference output.
//
// Adding a new fixture: drop the JSON in the reference folder, run this
// test, and paste the "baseline" line it prints into the table below.
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr;

namespace Cena.Infrastructure.Tests.Ocr;

public sealed class OcrFixtureRegressionTests
{
    private const double ConfidenceDropBudgetPp = 0.05;  // 5 percentage points
    private const int    CountTolerance        = 1;      // allow ±1 block for rounding

    // Baseline known-good numbers captured from the Python reference cascade.
    // Do NOT edit these without a paired roadmap note — a drop means either
    // the Python reference regressed or the C# contract diverged.
    private static readonly Dictionary<string, Baseline> Baselines = new()
    {
        ["bagrut-3u-text-shortcut.json"]     = new(TextBlocks: 612, MathBlocks: 0, OverallConfidence: 0.990, HumanReview: false),
        ["bagrut-5u-full-cascade.json"]      = new(TextBlocks: 296, MathBlocks: 0, OverallConfidence: 0.888, HumanReview: false),
        ["geva-hebrew-solutions.json"]       = new(TextBlocks: 209, MathBlocks: 1, OverallConfidence: 0.841, HumanReview: true),
        ["pdf-encrypted-422.json"]           = new(TextBlocks:   0, MathBlocks: 0, OverallConfidence: 0.000, HumanReview: true),
        ["pdf-scanned-bad-ocr.json"]         = new(TextBlocks:   6, MathBlocks: 0, OverallConfidence: 0.210, HumanReview: true),
        ["psychometric-english-text.json"]   = new(TextBlocks: 175, MathBlocks: 1, OverallConfidence: 0.932, HumanReview: true),
        ["psychometric-hebrew-mixed.json"]   = new(TextBlocks: 133, MathBlocks: 0, OverallConfidence: 0.903, HumanReview: false),
        ["sat-english-text.json"]            = new(TextBlocks: 164, MathBlocks: 1, OverallConfidence: 0.934, HumanReview: true),
        ["student-photo-algebra-3u.json"]    = new(TextBlocks:   1, MathBlocks: 1, OverallConfidence: 0.890, HumanReview: false),
        ["student-photo-calculus-5u.json"]   = new(TextBlocks:   1, MathBlocks: 2, OverallConfidence: 0.890, HumanReview: false),
    };

    private sealed record Baseline(int TextBlocks, int MathBlocks, double OverallConfidence, bool HumanReview);

    private static string FixtureRoot => Path.Combine(
        FindRepoRoot(),
        "scripts", "ocr-spike", "dev-fixtures", "cascade-results");

    [Fact]
    public void Every_Baseline_Has_A_Fixture_File()
    {
        foreach (var name in Baselines.Keys)
        {
            var path = Path.Combine(FixtureRoot, name);
            Assert.True(File.Exists(path),
                $"Baseline references {name} but fixture file is missing at {path}. " +
                "Either restore the fixture or remove the baseline entry.");
        }
    }

    [Fact]
    public void Every_Fixture_File_Has_A_Baseline()
    {
        var files = Directory.EnumerateFiles(FixtureRoot, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .ToList();

        foreach (var f in files)
        {
            Assert.True(Baselines.ContainsKey(f!),
                $"Fixture {f} has no baseline entry. Add it to OcrFixtureRegressionTests.Baselines " +
                "once you verify it is known-good.");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_Roundtrips_Through_Contract(string fixtureName)
    {
        var path = Path.Combine(FixtureRoot, fixtureName);
        var bytes = File.ReadAllBytes(path);

        // If the contract has drifted we'll either throw here or end up
        // with null fields. Both fail the test.
        var result = JsonSerializer.Deserialize<OcrCascadeResult>(bytes, OcrJsonOptions.Default);

        Assert.NotNull(result);
        Assert.Equal("1.0", result!.SchemaVersion);
        Assert.NotNull(result.TextBlocks);
        Assert.NotNull(result.MathBlocks);
        Assert.NotNull(result.Figures);
        Assert.NotNull(result.FallbacksFired);
        Assert.NotNull(result.ReasonsForReview);
        Assert.NotNull(result.LayerTimingsSeconds);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_Matches_Quality_Baseline_Within_5pp(string fixtureName)
    {
        var baseline = Baselines[fixtureName];
        var path = Path.Combine(FixtureRoot, fixtureName);
        var result = JsonSerializer.Deserialize<OcrCascadeResult>(
            File.ReadAllBytes(path), OcrJsonOptions.Default)!;

        // --- counts (±1 tolerance) ---
        Assert.True(
            Math.Abs(result.TextBlocks.Count - baseline.TextBlocks) <= CountTolerance,
            $"{fixtureName}: text_blocks drift = expected {baseline.TextBlocks}±{CountTolerance}, got {result.TextBlocks.Count}.");

        Assert.True(
            Math.Abs(result.MathBlocks.Count - baseline.MathBlocks) <= CountTolerance,
            $"{fixtureName}: math_blocks drift = expected {baseline.MathBlocks}±{CountTolerance}, got {result.MathBlocks.Count}.");

        // --- confidence (5pp drop budget) ---
        var drop = baseline.OverallConfidence - result.OverallConfidence;
        Assert.True(drop <= ConfidenceDropBudgetPp,
            $"{fixtureName}: overall_confidence dropped by {drop:F3} (> 5pp budget). " +
            $"baseline={baseline.OverallConfidence:F3}, current={result.OverallConfidence:F3}.");

        // --- human review parity ---
        Assert.Equal(baseline.HumanReview, result.HumanReviewRequired);
    }

    public static IEnumerable<object[]> FixtureNames() =>
        Baselines.Keys.Select(k => new object[] { k });

    private static string FindRepoRoot()
    {
        // Tests run from bin/Debug/net9.0/ — walk up until we see the sln.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cena.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "scripts", "ocr-spike")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not find repo root; OCR fixture regression test needs scripts/ocr-spike/ at the repo root.");
    }
}
