// =============================================================================
// Cena Platform — Item Exposure Document (RDY-018)
// Cumulative exposure tracking for Sympson-Hetter exposure control.
//
// Each document tracks a single item's exposure statistics across all students.
// Updated on every item administration. Used by ConstrainedCatAlgorithm to
// compute per-item exposure parameters.
//
// Cite: Sympson & Hetter (1985) — Controlling item-exposure rates in
// computerized adaptive testing. Proceedings of the 27th annual meeting
// of the Military Testing Association.
// =============================================================================

namespace Cena.Infrastructure.Assessment;

/// <summary>
/// RDY-018: Per-item exposure tracking for Sympson-Hetter exposure control.
/// Keyed by ItemId. One document per item in the bank.
/// </summary>
public sealed class ItemExposureDocument
{
    /// <summary>Marten document identity — same as <see cref="ItemId"/>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Question bank item ID.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Number of times this item was administered to any student.</summary>
    public long TotalAdministrations { get; set; }

    /// <summary>
    /// Number of times this item was selected by CAT (before S-H filtering).
    /// TotalSelections >= TotalAdministrations because some selections are rejected.
    /// </summary>
    public long TotalSelections { get; set; }

    /// <summary>
    /// Total number of eligible students who could have seen this item.
    /// Used to compute the raw exposure rate: TotalAdministrations / TotalEligible.
    /// </summary>
    public long TotalEligible { get; set; }

    /// <summary>
    /// Current Sympson-Hetter exposure control parameter (0 &lt; p_i &lt;= 1).
    /// When CAT selects this item, it is administered with probability p_i.
    /// Recalibrated periodically to maintain the target exposure rate.
    /// </summary>
    public double ExposureParameter { get; set; } = 1.0;

    /// <summary>
    /// Observed exposure rate: TotalAdministrations / TotalEligible.
    /// Cached for fast dashboard queries. Updated on each administration.
    /// </summary>
    public double ObservedExposureRate { get; set; }

    /// <summary>Target exposure rate for this item (default 0.20).</summary>
    public double TargetExposureRate { get; set; } = 0.20;

    /// <summary>Last time the exposure parameter was recalibrated.</summary>
    public DateTimeOffset LastCalibrationAt { get; set; }

    /// <summary>Last time this item was administered.</summary>
    public DateTimeOffset LastAdministeredAt { get; set; }

    /// <summary>
    /// Records an administration of this item.
    /// Call after the item is actually shown to a student.
    /// </summary>
    public void RecordAdministration()
    {
        TotalAdministrations++;
        LastAdministeredAt = DateTimeOffset.UtcNow;
        RecalculateObservedRate();
    }

    /// <summary>
    /// Records that the item was selected by CAT (may or may not be administered).
    /// </summary>
    public void RecordSelection()
    {
        TotalSelections++;
    }

    /// <summary>
    /// Records that a student was eligible to see this item (had it in their pool).
    /// Call once per session for each item in the available pool.
    /// </summary>
    public void RecordEligible()
    {
        TotalEligible++;
        RecalculateObservedRate();
    }

    /// <summary>
    /// Recalibrates the Sympson-Hetter exposure parameter to drive the
    /// observed exposure rate toward the target.
    ///
    /// Algorithm: p_i(new) = p_i(old) * (targetRate / observedRate)
    /// Clamped to [0.05, 1.0] to prevent items from being permanently removed
    /// or always selected.
    /// </summary>
    public void RecalibrateExposureParameter()
    {
        if (ObservedExposureRate <= 0 || TotalEligible < 10)
        {
            // Not enough data to calibrate — keep current parameter
            return;
        }

        var ratio = TargetExposureRate / ObservedExposureRate;
        ExposureParameter = Math.Clamp(ExposureParameter * ratio, 0.05, 1.0);
        LastCalibrationAt = DateTimeOffset.UtcNow;
    }

    private void RecalculateObservedRate()
    {
        ObservedExposureRate = TotalEligible > 0
            ? (double)TotalAdministrations / TotalEligible
            : 0;
    }
}

/// <summary>
/// RDY-018: Exposure analytics for the item bank health dashboard.
/// </summary>
public sealed record ExposureAnalyticsReport
{
    /// <summary>Total items with exposure tracking data.</summary>
    public int TotalTrackedItems { get; init; }

    /// <summary>Items exceeding 2x their target exposure rate.</summary>
    public IReadOnlyList<OverExposedItem> OverExposedItems { get; init; } = Array.Empty<OverExposedItem>();

    /// <summary>Items never administered to any student.</summary>
    public IReadOnlyList<string> UnusedItemIds { get; init; } = Array.Empty<string>();

    /// <summary>Exposure rate distribution across all items.</summary>
    public ExposureRateDistribution Distribution { get; init; } = new();

    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record OverExposedItem(
    string ItemId,
    double ObservedRate,
    double TargetRate,
    long TotalAdministrations);

public sealed record ExposureRateDistribution
{
    public double Min { get; init; }
    public double P25 { get; init; }
    public double Median { get; init; }
    public double P75 { get; init; }
    public double Max { get; init; }
    public double Mean { get; init; }
}
