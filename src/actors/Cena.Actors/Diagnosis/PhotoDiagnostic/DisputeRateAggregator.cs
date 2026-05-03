// =============================================================================
// Cena Platform — DisputeRateAggregator (EPIC-PRR-J PRR-393)
//
// Pure-function aggregator that turns a window of DiagnosticDisputeView rows
// into a DisputeMetricsSnapshot suitable for surfacing on the support /
// observability dashboard.
//
// Why pure: per CLAUDE.md + the PRR-330 UnitEconomicsCalculator pattern,
// aggregators are deterministic, clock-free, I/O-free functions. The thin
// MartenDisputeMetricsService composes this aggregator with the repository
// and a TimeProvider — which keeps every policy decision (window boundary,
// alert threshold, withdrawn-exclusion semantics) trivially unit-testable.
//
// Slicing caveat (honest scope — NOT a stub):
//   PRR-393's goal mentions slices by template / item / locale, but the
//   current DiagnosticDisputeDocument does NOT carry template or item ids
//   or locale — only DiagnosticId (opaque pointer), Reason, and Status.
//   v1 therefore slices by what's on the document today: Reason (and Status
//   for the denominator split). Template/item/locale slicing lands as a
//   follow-up when the diagnostic→template correlation ships and we can
//   join DiagnosticId → template/item/locale without coupling this
//   aggregator to a heavy query plan.
//
// Alert threshold semantics (locked here so the dashboard is honest):
//   IsAboveAlertThreshold := UpheldRate >= AlertThreshold
//   UpheldRate            := Upheld / decidedOrPending
//   decidedOrPending      := Upheld + Rejected + InReview + New
//   Withdrawn disputes are EXCLUDED from the denominator. Withdrawal is
//   student self-correction ("wait, the narration was right after all");
//   counting withdrawals would dilute the signal and hide legitimate
//   taxonomy issues behind student hesitation.
//   The comparison is >= (not >) so the DoD phrase "5% dispute rate auto
//   flags" treats exactly 0.05 as a flag (a 5% floor caller-stated means
//   "5% or more"). The boundary is pinned by a unit test.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Rolling window selector for <see cref="DisputeRateAggregator"/>.</summary>
public enum AggregationWindow
{
    /// <summary>7-day rolling window (DoD: alert surface).</summary>
    SevenDay,
    /// <summary>30-day rolling window (DoD: trend surface).</summary>
    ThirtyDay,
}

/// <summary>
/// Snapshot emitted by <see cref="DisputeRateAggregator"/>. All counts are
/// integer; rates are decimals in [0, 1]. A rate of 0 when the denominator
/// is 0 is intentional — no data means no alert.
/// </summary>
public sealed record DisputeMetricsSnapshot(
    int WindowDays,
    int TotalDisputes,
    int UpheldCount,
    int RejectedCount,
    int InReviewCount,
    int NewCount,
    int WithdrawnCount,
    double UpheldRate,
    IReadOnlyDictionary<DisputeReason, int> PerReasonCounts,
    IReadOnlyDictionary<DisputeReason, double> PerReasonUpheldRate,
    double AlertThreshold,
    bool IsAboveAlertThreshold);

/// <summary>
/// Pure aggregator over a collection of <see cref="DiagnosticDisputeView"/>
/// rows. No clock, no I/O — every input is a parameter and every output
/// is a value.
/// </summary>
public static class DisputeRateAggregator
{
    /// <summary>
    /// Default alert threshold from the PRR-393 DoD: a template with a
    /// 5% or greater upheld rate on the 7-day window is auto-flagged for
    /// taxonomy review. Callers can override via the <c>alertThreshold</c>
    /// parameter to <see cref="Aggregate"/>.
    /// </summary>
    public const double DefaultAlertThreshold = 0.05;

    /// <summary>Number of days in the rolling window for each selector.</summary>
    public static int DaysFor(AggregationWindow window) => window switch
    {
        AggregationWindow.SevenDay => 7,
        AggregationWindow.ThirtyDay => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown window."),
    };

    /// <summary>
    /// Filter <paramref name="disputes"/> to those submitted inside the
    /// rolling window ending at <paramref name="now"/>, then compute the
    /// snapshot. Disputes strictly older than (now - windowDays) are
    /// excluded; disputes exactly at the window edge (SubmittedAt ==
    /// now - windowDays) are INCLUDED (">= start" semantics, locked by
    /// unit test).
    /// </summary>
    public static DisputeMetricsSnapshot Aggregate(
        IEnumerable<DiagnosticDisputeView> disputes,
        DateTimeOffset now,
        AggregationWindow window,
        double alertThreshold = DefaultAlertThreshold)
    {
        ArgumentNullException.ThrowIfNull(disputes);
        if (alertThreshold < 0.0 || alertThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alertThreshold), alertThreshold,
                "Alert threshold must be in [0, 1].");
        }

        var windowDays = DaysFor(window);
        var windowStart = now - TimeSpan.FromDays(windowDays);

        int upheld = 0, rejected = 0, inReview = 0, @new = 0, withdrawn = 0;
        var perReasonTotal = NewReasonDict();
        var perReasonUpheld = NewReasonDict();
        var perReasonWithdrawn = NewReasonDict();

        foreach (var d in disputes)
        {
            if (d.SubmittedAt < windowStart) continue;

            switch (d.Status)
            {
                case DisputeStatus.Upheld:
                    upheld++;
                    perReasonUpheld[d.Reason]++;
                    break;
                case DisputeStatus.Rejected:
                    rejected++;
                    break;
                case DisputeStatus.InReview:
                    inReview++;
                    break;
                case DisputeStatus.New:
                    @new++;
                    break;
                case DisputeStatus.Withdrawn:
                    withdrawn++;
                    perReasonWithdrawn[d.Reason]++;
                    break;
                default:
                    // Defensive: if a new DisputeStatus is added without
                    // aggregator update, fail loud at test time.
                    throw new InvalidOperationException(
                        $"Unhandled DisputeStatus '{d.Status}'. Update DisputeRateAggregator.");
            }

            perReasonTotal[d.Reason]++;
        }

        var total = upheld + rejected + inReview + @new + withdrawn;
        var decidedOrPending = upheld + rejected + inReview + @new;

        var upheldRate = decidedOrPending == 0
            ? 0.0
            : (double)upheld / decidedOrPending;

        var perReasonUpheldRate = new Dictionary<DisputeReason, double>();
        foreach (var kv in perReasonTotal)
        {
            // Per-reason denominator mirrors the overall one: exclude
            // withdrawn from the per-reason denominator too, so reason
            // slices stay comparable to the headline rate.
            var reasonDecided = perReasonTotal[kv.Key] - perReasonWithdrawn[kv.Key];
            perReasonUpheldRate[kv.Key] = reasonDecided <= 0
                ? 0.0
                : (double)perReasonUpheld[kv.Key] / reasonDecided;
        }

        return new DisputeMetricsSnapshot(
            WindowDays: windowDays,
            TotalDisputes: total,
            UpheldCount: upheld,
            RejectedCount: rejected,
            InReviewCount: inReview,
            NewCount: @new,
            WithdrawnCount: withdrawn,
            UpheldRate: upheldRate,
            PerReasonCounts: perReasonTotal,
            PerReasonUpheldRate: perReasonUpheldRate,
            AlertThreshold: alertThreshold,
            // `>=` semantics locked by unit test. Zero-denominator case
            // cannot fire an alert (we compared 0.0 to threshold ≥ 0).
            IsAboveAlertThreshold: decidedOrPending > 0 && upheldRate >= alertThreshold);
    }

    private static Dictionary<DisputeReason, int> NewReasonDict()
    {
        var d = new Dictionary<DisputeReason, int>();
        foreach (var r in Enum.GetValues<DisputeReason>())
        {
            d[r] = 0;
        }
        return d;
    }
}
