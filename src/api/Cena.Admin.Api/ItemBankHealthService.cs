// =============================================================================
// Cena Platform — Item Bank Health Service (IRT-002)
// Dashboard data + quality gate for the question bank.
//
// Per improvement #14:
// - Per-concept coverage heatmap
// - Items with poor IRT fit (infit/outfit MNSQ outside 0.7-1.3)
// - DIF flags by language/track
// - Exposure rate distribution
// - Quality gate: reject items < 30 responses or fit outside range
// =============================================================================

using Cena.Actors.Services;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

/// <summary>
/// IRT-002: Provides item bank health metrics for the admin dashboard.
/// </summary>
public sealed class ItemBankHealthService
{
    /// <summary>Minimum responses before an item's IRT parameters are trusted.</summary>
    public const int MinResponsesForCalibration = 30;

    /// <summary>Acceptable infit/outfit MNSQ range (Rasch model).</summary>
    public const double FitLowerBound = 0.7;
    public const double FitUpperBound = 1.3;

    private readonly IDocumentStore _store;
    private readonly ILogger<ItemBankHealthService> _logger;

    public ItemBankHealthService(IDocumentStore store, ILogger<ItemBankHealthService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Generates the full item bank health report.
    /// </summary>
    public async Task<ItemBankHealthReport> GetHealthReportAsync(
        string? trackId = null,
        CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();

        var questions = await session.Query<QuestionDocument>()
            .Where(q => q.IsActive)
            .ToListAsync(ct);

        if (trackId != null)
        {
            questions = questions
                .Where(q => q.BagrutAlignment?.ExamCode != null)
                .ToList();
        }

        var conceptCoverage = BuildConceptCoverage(questions);
        var poorFitItems = FindPoorFitItems(questions);
        var underCalibratedItems = questions
            .Where(q => q.EloAttemptCount < MinResponsesForCalibration)
            .Select(q => new UnderCalibratedItem(q.Id, q.ConceptId, q.EloAttemptCount))
            .ToList();

        var exposureDistribution = BuildExposureDistribution(questions);
        var confidenceDistribution = BuildConfidenceDistribution(questions);

        return new ItemBankHealthReport
        {
            TotalItems = questions.Count,
            ActiveItems = questions.Count(q => q.IsActive),
            CalibratedItems = questions.Count(q => q.EloAttemptCount >= MinResponsesForCalibration),
            UnderCalibratedCount = underCalibratedItems.Count,
            PoorFitCount = poorFitItems.Count,
            ConceptCoverage = conceptCoverage,
            PoorFitItems = poorFitItems,
            UnderCalibratedItems = underCalibratedItems,
            ExposureDistribution = exposureDistribution,
            ConfidenceDistribution = confidenceDistribution,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Quality gate check: can this item be used in assessments?
    /// </summary>
    public static ItemQualityGateResult CheckItemQuality(QuestionDocument item)
    {
        var violations = new List<string>();

        if (item.EloAttemptCount < MinResponsesForCalibration)
            violations.Add($"Insufficient responses ({item.EloAttemptCount}/{MinResponsesForCalibration})");

        // Note: infit/outfit MNSQ would come from the IRT calibration pipeline
        // once IRT-001 is implemented. For now we check Elo stability.
        if (item.EloAttemptCount > 0 && item.DifficultyElo is < 800 or > 2200)
            violations.Add($"Extreme difficulty Elo ({item.DifficultyElo:F0}) — likely miscalibrated");

        return new ItemQualityGateResult
        {
            ItemId = item.Id,
            Passed = violations.Count == 0,
            Violations = violations,
            IsCalibrated = item.EloAttemptCount >= MinResponsesForCalibration
        };
    }

    private static List<ConceptCoverageEntry> BuildConceptCoverage(IReadOnlyList<QuestionDocument> questions)
    {
        return questions
            .GroupBy(q => q.ConceptId)
            .Select(g => new ConceptCoverageEntry
            {
                ConceptId = g.Key,
                ItemCount = g.Count(),
                CalibratedCount = g.Count(q => q.EloAttemptCount >= MinResponsesForCalibration),
                AvgDifficultyElo = g.Average(q => q.DifficultyElo),
                HasGoodCoverage = g.Count(q => q.EloAttemptCount >= MinResponsesForCalibration) >= 5
            })
            .OrderBy(c => c.CalibratedCount)
            .ToList();
    }

    private static List<PoorFitItem> FindPoorFitItems(IReadOnlyList<QuestionDocument> questions)
    {
        // Placeholder: real infit/outfit needs IRT calibration pipeline (IRT-001)
        // For now, flag items with extreme Elo values as potential misfits
        return questions
            .Where(q => q.EloAttemptCount >= MinResponsesForCalibration)
            .Where(q => q.DifficultyElo < 900 || q.DifficultyElo > 2100)
            .Select(q => new PoorFitItem(q.Id, q.ConceptId, q.DifficultyElo, "Extreme Elo — potential misfit"))
            .ToList();
    }

    /// <summary>PP-011: Confidence distribution across all items.</summary>
    private static ConfidenceDistribution BuildConfidenceDistribution(IReadOnlyList<QuestionDocument> questions)
    {
        return new ConfidenceDistribution(
            Default: questions.Count(q => IrtItemParameters.ConfidenceFromN(q.EloAttemptCount) == CalibrationConfidence.Default),
            LowConfidence: questions.Count(q => IrtItemParameters.ConfidenceFromN(q.EloAttemptCount) == CalibrationConfidence.LowConfidence),
            Moderate: questions.Count(q => IrtItemParameters.ConfidenceFromN(q.EloAttemptCount) == CalibrationConfidence.Moderate),
            High: questions.Count(q => IrtItemParameters.ConfidenceFromN(q.EloAttemptCount) == CalibrationConfidence.High),
            Production: questions.Count(q => IrtItemParameters.ConfidenceFromN(q.EloAttemptCount) == CalibrationConfidence.Production));
    }

    private static ExposureDistribution BuildExposureDistribution(IReadOnlyList<QuestionDocument> questions)
    {
        var attempts = questions.Select(q => q.EloAttemptCount).OrderBy(a => a).ToList();
        if (attempts.Count == 0)
            return new ExposureDistribution(0, 0, 0, 0, 0);

        return new ExposureDistribution(
            Min: attempts.First(),
            P25: attempts[attempts.Count / 4],
            Median: attempts[attempts.Count / 2],
            P75: attempts[3 * attempts.Count / 4],
            Max: attempts.Last());
    }
}

// ── Report DTOs ──

public sealed record ItemBankHealthReport
{
    public int TotalItems { get; init; }
    public int ActiveItems { get; init; }
    public int CalibratedItems { get; init; }
    public int UnderCalibratedCount { get; init; }
    public int PoorFitCount { get; init; }
    public IReadOnlyList<ConceptCoverageEntry> ConceptCoverage { get; init; } = Array.Empty<ConceptCoverageEntry>();
    public IReadOnlyList<PoorFitItem> PoorFitItems { get; init; } = Array.Empty<PoorFitItem>();
    public IReadOnlyList<UnderCalibratedItem> UnderCalibratedItems { get; init; } = Array.Empty<UnderCalibratedItem>();
    public ExposureDistribution ExposureDistribution { get; init; } = new(0, 0, 0, 0, 0);
    public ConfidenceDistribution ConfidenceDistribution { get; init; } = new(0, 0, 0, 0, 0);
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ConceptCoverageEntry
{
    public string ConceptId { get; init; } = "";
    public int ItemCount { get; init; }
    public int CalibratedCount { get; init; }
    public double AvgDifficultyElo { get; init; }
    public bool HasGoodCoverage { get; init; }
}

public sealed record PoorFitItem(string ItemId, string ConceptId, double DifficultyElo, string Reason);
public sealed record UnderCalibratedItem(string ItemId, string ConceptId, int ResponseCount);
public sealed record ExposureDistribution(int Min, int P25, int Median, int P75, int Max);

/// <summary>PP-011: How many items at each calibration confidence tier.</summary>
public sealed record ConfidenceDistribution(
    int Default, int LowConfidence, int Moderate, int High, int Production);

public sealed record ItemQualityGateResult
{
    public string ItemId { get; init; } = "";
    public bool Passed { get; init; }
    public bool IsCalibrated { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = Array.Empty<string>();
}
