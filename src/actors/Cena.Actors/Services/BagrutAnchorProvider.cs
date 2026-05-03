// =============================================================================
// Cena Platform — Bagrut Anchor Provider (RDY-028)
//
// Loads anchor item parameters from config/bagrut-anchors.json. Anchors
// are Cena items mapped to real Bagrut exam questions with known pass rates.
// The IRT difficulty of each anchor is derived from the Bagrut pass rate:
//   b = -ln(p / (1-p))
//
// Used by IrtCalibrationPipeline as priors during concurrent calibration:
// anchor items fix the difficulty scale, non-anchor items are estimated
// relative to anchors.
//
// Reference: Kolen & Brennan (2014), Test Equating, Scaling, and Linking.
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// A single Bagrut anchor item with known difficulty from exam pass rates.
/// </summary>
public sealed record BagrutAnchor(
    string AnchorId,
    string ConceptId,
    string Subject,
    double PassRate,
    double Difficulty,
    string Band,
    string ExamCode,
    int Year);

/// <summary>
/// RDY-028: Provides Bagrut anchor items for IRT calibration scale linking.
/// </summary>
public interface IBagrutAnchorProvider
{
    /// <summary>All anchor items across all tracks.</summary>
    IReadOnlyList<BagrutAnchor> GetAllAnchors();

    /// <summary>Anchors for a specific track (e.g. "math_5u").</summary>
    IReadOnlyList<BagrutAnchor> GetAnchorsForTrack(string trackId);

    /// <summary>
    /// Convert anchors to <see cref="IrtItemParameters"/> for use as priors
    /// in <see cref="IIrtCalibrationPipeline.Calibrate"/>.
    /// </summary>
    IReadOnlyList<IrtItemParameters> GetAnchorPriors(string trackId);

    /// <summary>IRT difficulty band thresholds (logit scale).</summary>
    (double EasyMax, double HardMin) GetBandThresholds();
}

public sealed class BagrutAnchorProvider : IBagrutAnchorProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BagrutAnchor>> _byTrack;
    private readonly IReadOnlyList<BagrutAnchor> _all;
    private readonly double _easyMax;
    private readonly double _hardMin;

    public BagrutAnchorProvider(IConfiguration configuration, ILogger<BagrutAnchorProvider> logger)
    {
        var anchors = new Dictionary<string, List<BagrutAnchor>>(StringComparer.OrdinalIgnoreCase);
        var all = new List<BagrutAnchor>();

        // Parse tracks from config (bound from config/bagrut-anchors.json via AddJsonFile)
        var tracksSection = configuration.GetSection("tracks");

        foreach (var trackSection in tracksSection.GetChildren())
        {
            var trackId = trackSection.Key;
            var trackAnchors = new List<BagrutAnchor>();

            var anchorsSection = trackSection.GetSection("anchors");
            foreach (var anchorSection in anchorsSection.GetChildren())
            {
                var anchor = new BagrutAnchor(
                    AnchorId: anchorSection["anchorId"] ?? "",
                    ConceptId: anchorSection["conceptId"] ?? "",
                    Subject: anchorSection["subject"] ?? "",
                    PassRate: double.TryParse(anchorSection["passRate"], out var pr) ? pr : 0.5,
                    Difficulty: double.TryParse(anchorSection["difficulty"], out var d) ? d : 0.0,
                    Band: anchorSection["band"] ?? "medium",
                    ExamCode: anchorSection.GetSection("bagrutRef")["examCode"] ?? "",
                    Year: int.TryParse(anchorSection.GetSection("bagrutRef")["year"], out var y) ? y : 0);

                trackAnchors.Add(anchor);
                all.Add(anchor);
            }

            anchors[trackId] = trackAnchors;
            logger.LogInformation("Loaded {Count} Bagrut anchors for track {Track}", trackAnchors.Count, trackId);
        }

        _byTrack = anchors.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<BagrutAnchor>)kv.Value.AsReadOnly(),
            StringComparer.OrdinalIgnoreCase);
        _all = all.AsReadOnly();

        // Band thresholds
        var bandSection = configuration.GetSection("bandThresholds");
        _easyMax = double.TryParse(bandSection.GetSection("easy")["max"], out var em) ? em : -0.75;
        _hardMin = double.TryParse(bandSection.GetSection("hard")["min"], out var hm) ? hm : 0.50;

        logger.LogInformation("BagrutAnchorProvider initialized: {Total} anchors, {Tracks} tracks, bands=[easy<{EasyMax}, hard>{HardMin}]",
            all.Count, anchors.Count, _easyMax, _hardMin);
    }

    public IReadOnlyList<BagrutAnchor> GetAllAnchors() => _all;

    public IReadOnlyList<BagrutAnchor> GetAnchorsForTrack(string trackId) =>
        _byTrack.TryGetValue(trackId, out var anchors) ? anchors : Array.Empty<BagrutAnchor>();

    public IReadOnlyList<IrtItemParameters> GetAnchorPriors(string trackId)
    {
        var anchors = GetAnchorsForTrack(trackId);
        return anchors.Select(a => new IrtItemParameters(
            QuestionId: a.AnchorId,
            Difficulty: a.Difficulty,
            Discrimination: 1.0,
            GuessParameter: 0.0,
            ResponseCount: 30_000,  // national-scale response count
            FitResidual: 0.0,
            Confidence: CalibrationConfidence.Production,
            CalibratedAt: DateTimeOffset.UtcNow
        )).ToList().AsReadOnly();
    }

    public (double EasyMax, double HardMin) GetBandThresholds() => (_easyMax, _hardMin);
}
