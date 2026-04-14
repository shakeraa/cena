// =============================================================================
// Cena Platform — Constrained CAT Algorithm (IRT-003)
// A-stratified exposure control with content balance constraints.
//
// Item selection: maximize Fisher information at current theta, subject to:
// 1. Content balance (Bagrut topic coverage across strata)
// 2. Exposure rate cap (max 25% of students see any single item)
// 3. Reserved pool exclusion (exam simulation items never shown in practice)
// 4. PP-011: Calibration confidence weighting (prefer well-calibrated items)
//
// Exposure control: Sympson-Hetter method — each item has a probability
// of being administered even when selected (exposure parameter 0.0-1.0).
// =============================================================================

using Cena.Actors.Services;

namespace Cena.Actors.Assessment;

/// <summary>
/// IRT-003: Constrained CAT item selector with exposure control.
/// </summary>
public sealed class ConstrainedCatAlgorithm
{
    /// <summary>Maximum exposure rate for any single item.</summary>
    public const double MaxExposureRate = 0.25;

    /// <summary>
    /// Selects the next item for a CAT session, subject to constraints.
    /// </summary>
    /// <param name="theta">Current ability estimate.</param>
    /// <param name="availableItems">Items not yet administered in this session.</param>
    /// <param name="constraints">Content balance + exposure constraints.</param>
    /// <returns>Selected item ID, or null if no eligible items remain.</returns>
    public CatItemSelection? SelectNextItem(
        double theta,
        IReadOnlyList<CatItemCandidate> availableItems,
        CatConstraints constraints)
    {
        if (availableItems.Count == 0) return null;

        // Step 1: Filter by content constraints
        var eligible = availableItems
            .Where(item => !constraints.ReservedPoolIds.Contains(item.ItemId))
            .Where(item => MeetsContentBalance(item, constraints))
            .ToList();

        if (eligible.Count == 0)
        {
            // Relax content constraint if no items match
            eligible = availableItems
                .Where(item => !constraints.ReservedPoolIds.Contains(item.ItemId))
                .ToList();
        }

        if (eligible.Count == 0) return null;

        // Step 2: Compute Fisher information at theta for each item
        // PP-011: Weight by calibration confidence so well-calibrated items
        // are preferred for ability estimation.
        // RDY-007: Weight by DIF category — Category C items are heavily
        // penalized when student is in the focal group (Arabic speakers).
        var scored = eligible
            .Select(item => new
            {
                Item = item,
                Information = ComputeFisherInformation(theta, item.DifficultyElo, item.Discrimination)
                    * ConfidenceWeight(item.Confidence)
                    * DifWeight(item.DifCategory, constraints.StudentLocale),
            })
            .OrderByDescending(x => x.Information)
            .ToList();

        // Step 3: A-stratified selection with Sympson-Hetter exposure control
        foreach (var candidate in scored)
        {
            // Sympson-Hetter: accept with probability = exposure parameter
            var exposureParam = ComputeExposureParameter(
                candidate.Item.ExposureRate, MaxExposureRate);

            if (Random.Shared.NextDouble() <= exposureParam)
            {
                return new CatItemSelection(
                    candidate.Item.ItemId,
                    candidate.Information,
                    candidate.Item.TopicCluster,
                    exposureParam);
            }
        }

        // Fallback: select the top item ignoring exposure control
        var top = scored.First();
        return new CatItemSelection(top.Item.ItemId, top.Information, top.Item.TopicCluster, 1.0);
    }

    /// <summary>
    /// Fisher information for a 2PL IRT model at the given theta.
    /// I(θ) = a² * P(θ) * Q(θ), where P = 1/(1+exp(-a(θ-b)))
    /// </summary>
    private static double ComputeFisherInformation(double theta, double difficulty, double discrimination)
    {
        var a = discrimination;
        var b = EloToDifficulty(difficulty);
        var exponent = -a * (theta - b);
        var p = 1.0 / (1.0 + Math.Exp(exponent));
        var q = 1.0 - p;
        return a * a * p * q;
    }

    /// <summary>
    /// Converts Elo rating to IRT difficulty scale (roughly: Elo 1500 → b=0).
    /// </summary>
    private static double EloToDifficulty(double elo) => (elo - 1500.0) / 200.0;

    /// <summary>
    /// Sympson-Hetter exposure parameter: P(administer | selected).
    /// If current exposure rate exceeds max, reduce probability proportionally.
    /// </summary>
    private static double ComputeExposureParameter(double currentRate, double maxRate)
    {
        if (currentRate <= 0) return 1.0;
        if (currentRate >= maxRate) return maxRate / currentRate;
        return 1.0;
    }

    /// <summary>
    /// PP-011: Confidence weight for item selection.
    /// Uncalibrated items are heavily penalized; Production items get full weight.
    /// </summary>
    private static double ConfidenceWeight(CalibrationConfidence confidence) => confidence switch
    {
        CalibrationConfidence.Production => 1.0,
        CalibrationConfidence.High => 0.95,
        CalibrationConfidence.Moderate => 0.80,
        CalibrationConfidence.LowConfidence => 0.50,
        _ => 0.10 // Default/uncalibrated — rarely selected for ability estimation
    };

    /// <summary>
    /// RDY-007: DIF weight for item selection.
    /// Category C items are heavily penalized when the student is in the focal
    /// group (Arabic speakers). For reference group or when locale is unknown,
    /// no penalty is applied — the item's DIF status doesn't affect Hebrew students.
    /// </summary>
    private static double DifWeight(DifCategory difCategory, string? studentLocale)
    {
        // DIF only matters for the focal group (Arabic speakers)
        if (studentLocale is not "ar") return 1.0;

        return difCategory switch
        {
            DifCategory.C => 0.05,       // Effectively removed from selection
            DifCategory.B => 0.60,       // Moderate penalty
            DifCategory.A => 1.0,        // No penalty — item is fair
            DifCategory.Pending => 0.80, // Mild caution until analyzed
            _ => 1.0
        };
    }

    private static bool MeetsContentBalance(CatItemCandidate item, CatConstraints constraints)
    {
        if (constraints.TopicQuotas.Count == 0) return true;

        if (!constraints.TopicQuotas.TryGetValue(item.TopicCluster, out var quota))
            return true; // No quota for this topic — allow

        return quota.Current < quota.Target;
    }
}

/// <summary>
/// An item candidate for CAT selection with IRT parameters.
/// </summary>
public sealed record CatItemCandidate(
    string ItemId,
    double DifficultyElo,
    double Discrimination,
    string TopicCluster,
    double ExposureRate,
    CalibrationConfidence Confidence = CalibrationConfidence.Moderate,
    /// <summary>RDY-007: DIF category for this item. Category C items are deprioritized for focal-group students.</summary>
    DifCategory DifCategory = DifCategory.Pending);

/// <summary>
/// Content balance + exposure constraints for CAT.
/// </summary>
public sealed record CatConstraints
{
    /// <summary>Per-topic quotas for content balance.</summary>
    public IReadOnlyDictionary<string, TopicQuota> TopicQuotas { get; init; } =
        new Dictionary<string, TopicQuota>();

    /// <summary>Item IDs reserved for exam simulation — never shown in practice.</summary>
    public IReadOnlySet<string> ReservedPoolIds { get; init; } = new HashSet<string>();

    /// <summary>RDY-007: Student locale for DIF-aware item selection. Null = ignore DIF.</summary>
    public string? StudentLocale { get; init; }
}

public sealed record TopicQuota(int Target, int Current);

/// <summary>
/// Result of CAT item selection.
/// </summary>
public sealed record CatItemSelection(
    string ItemId,
    double FisherInformation,
    string TopicCluster,
    double ExposureParameter);
