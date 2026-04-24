using Cena.Actors.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Services;

/// <summary>
/// RDY-028: Tests for BagrutAnchorProvider — anchor loading, difficulty
/// validation, band assignment, and IRT prior generation.
/// </summary>
public sealed class BagrutAnchorProviderTests
{
    // ── Anchor loading from config ──

    [Fact]
    public void GetAllAnchors_LoadsFromConfig()
    {
        var sut = CreateProvider();

        var anchors = sut.GetAllAnchors();

        Assert.NotEmpty(anchors);
        Assert.All(anchors, a =>
        {
            Assert.NotEmpty(a.AnchorId);
            Assert.NotEmpty(a.ConceptId);
            Assert.NotEmpty(a.Subject);
            Assert.InRange(a.PassRate, 0.01, 0.99);
            Assert.NotEmpty(a.Band);
        });
    }

    [Fact]
    public void GetAnchorsForTrack_ReturnsCorrectTrack()
    {
        var sut = CreateProvider();

        var math5u = sut.GetAnchorsForTrack("math_5u");
        var math4u = sut.GetAnchorsForTrack("math_4u");

        Assert.True(math5u.Count >= 10, $"math_5u should have >= 10 anchors, got {math5u.Count}");
        Assert.True(math4u.Count >= 5, $"math_4u should have >= 5 anchors, got {math4u.Count}");
    }

    [Fact]
    public void GetAnchorsForTrack_UnknownTrack_ReturnsEmpty()
    {
        var sut = CreateProvider();

        var result = sut.GetAnchorsForTrack("physics_5u");

        Assert.Empty(result);
    }

    // ── Difficulty = -logit(passRate) validation ──

    [Fact]
    public void AnchorDifficulties_MatchPassRateLogitTransform()
    {
        var sut = CreateProvider();
        var anchors = sut.GetAllAnchors();

        foreach (var anchor in anchors)
        {
            var expectedB = -Math.Log(anchor.PassRate / (1 - anchor.PassRate));
            var delta = Math.Abs(expectedB - anchor.Difficulty);

            Assert.True(delta < 0.05,
                $"{anchor.AnchorId}: expected b={expectedB:F2} from p={anchor.PassRate}, stored b={anchor.Difficulty:F2}, delta={delta:F3}");
        }
    }

    // ── Band assignment consistency ──

    [Fact]
    public void AnchorBands_ConsistentWithThresholds()
    {
        var sut = CreateProvider();
        var (easyMax, hardMin) = sut.GetBandThresholds();
        var anchors = sut.GetAllAnchors();

        foreach (var anchor in anchors)
        {
            var expectedBand = anchor.Difficulty < easyMax ? "easy"
                : anchor.Difficulty > hardMin ? "hard"
                : "medium";

            Assert.True(expectedBand == anchor.Band,
                $"{anchor.AnchorId}: b={anchor.Difficulty:F2} should be {expectedBand} but stored as {anchor.Band}");
        }
    }

    [Fact]
    public void BandThresholds_AreReasonable()
    {
        var sut = CreateProvider();
        var (easyMax, hardMin) = sut.GetBandThresholds();

        Assert.True(easyMax < 0, $"Easy max should be negative (below average), got {easyMax}");
        Assert.True(hardMin > 0, $"Hard min should be positive (above average), got {hardMin}");
        Assert.True(easyMax < hardMin, $"Easy max ({easyMax}) must be < Hard min ({hardMin})");
    }

    // ── IRT prior generation ──

    [Fact]
    public void GetAnchorPriors_ReturnsProductionConfidence()
    {
        var sut = CreateProvider();

        var priors = sut.GetAnchorPriors("math_5u");

        Assert.NotEmpty(priors);
        Assert.All(priors, p =>
        {
            Assert.Equal(CalibrationConfidence.Production, p.Confidence);
            Assert.Equal(1.0, p.Discrimination); // Rasch
            Assert.Equal(0.0, p.GuessParameter);
            Assert.True(p.ResponseCount >= 30_000, "Anchor response count should reflect national scale");
        });
    }

    [Fact]
    public void GetAnchorPriors_DifficultiesSpanFullRange()
    {
        var sut = CreateProvider();
        var priors = sut.GetAnchorPriors("math_5u");

        var minB = priors.Min(p => p.Difficulty);
        var maxB = priors.Max(p => p.Difficulty);

        Assert.True(minB < -1.0, $"Easiest anchor should have b < -1.0, got {minB:F2}");
        Assert.True(maxB > 0.5, $"Hardest anchor should have b > 0.5, got {maxB:F2}");
    }

    // ── Subject coverage ──

    [Fact]
    public void Math5u_CoversAllMajorSubjects()
    {
        var sut = CreateProvider();
        var anchors = sut.GetAnchorsForTrack("math_5u");

        var subjects = anchors.Select(a => a.Subject).Distinct().OrderBy(s => s).ToArray();

        Assert.Contains("algebra", subjects);
        Assert.Contains("calculus", subjects);
        Assert.Contains("geometry", subjects);
        Assert.Contains("probability", subjects);
    }

    // ── Helpers ──

    private static BagrutAnchorProvider CreateProvider()
    {
        var configPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "config", "bagrut-anchors.json");

        // Resolve to absolute path for reliable test execution
        var resolved = Path.GetFullPath(configPath);
        if (!File.Exists(resolved))
            throw new FileNotFoundException($"bagrut-anchors.json not found at {resolved}");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(resolved, optional: false)
            .Build();

        return new BagrutAnchorProvider(
            configuration,
            NullLogger<BagrutAnchorProvider>.Instance);
    }
}
