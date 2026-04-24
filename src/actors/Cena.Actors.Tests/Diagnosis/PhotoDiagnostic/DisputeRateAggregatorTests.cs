// =============================================================================
// Cena Platform — DisputeRateAggregator tests (EPIC-PRR-J PRR-393)
//
// Locks the PRR-393 DoD contract:
//   · empty input → zero counts, no alert
//   · 7-day window excludes disputes older than 7 days
//   · 30-day window includes 8-day-old but not 35-day-old
//   · UpheldRate math is honest (integer over integer, not lossy)
//   · AlertThreshold boundary: exactly threshold FIRES (>= semantics)
//   · Withdrawn excluded from the rate denominator
//   · PerReasonCounts aggregation correct for all 4 enum values
//   · All 5 DisputeStatus values accounted for in the totals
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DisputeRateAggregatorTests
{
    // Arbitrary fixed now so window math is deterministic.
    private static readonly DateTimeOffset Now =
        new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticDisputeView View(
        DateTimeOffset submittedAt,
        DisputeStatus status,
        DisputeReason reason = DisputeReason.WrongNarration) =>
        new(
            DisputeId: Guid.NewGuid().ToString("D"),
            DiagnosticId: "diag-" + Guid.NewGuid().ToString("N")[..8],
            StudentSubjectIdHash: "student-hash",
            Reason: reason,
            StudentComment: null,
            Status: status,
            SubmittedAt: submittedAt,
            ReviewedAt: status is DisputeStatus.New or DisputeStatus.InReview
                ? null
                : submittedAt.AddMinutes(5),
            ReviewerNote: null);

    [Fact]
    public void Empty_input_produces_zero_counts_and_no_alert()
    {
        var snap = DisputeRateAggregator.Aggregate(
            Array.Empty<DiagnosticDisputeView>(), Now, AggregationWindow.SevenDay);

        Assert.Equal(7, snap.WindowDays);
        Assert.Equal(0, snap.TotalDisputes);
        Assert.Equal(0, snap.UpheldCount);
        Assert.Equal(0, snap.RejectedCount);
        Assert.Equal(0, snap.InReviewCount);
        Assert.Equal(0, snap.NewCount);
        Assert.Equal(0, snap.WithdrawnCount);
        Assert.Equal(0.0, snap.UpheldRate);
        Assert.False(snap.IsAboveAlertThreshold);
        Assert.Equal(
            DisputeRateAggregator.DefaultAlertThreshold,
            snap.AlertThreshold);
    }

    [Fact]
    public void Seven_day_window_excludes_disputes_older_than_seven_days()
    {
        var inside = View(Now.AddDays(-6), DisputeStatus.Upheld);
        var outside = View(Now.AddDays(-8), DisputeStatus.Upheld);

        var snap = DisputeRateAggregator.Aggregate(
            new[] { inside, outside }, Now, AggregationWindow.SevenDay);

        Assert.Equal(1, snap.TotalDisputes);
        Assert.Equal(1, snap.UpheldCount);
    }

    [Fact]
    public void Thirty_day_window_includes_eight_day_old_but_not_thirty_five()
    {
        var eightDays = View(Now.AddDays(-8), DisputeStatus.Upheld);
        var thirtyFiveDays = View(Now.AddDays(-35), DisputeStatus.Upheld);

        var snap = DisputeRateAggregator.Aggregate(
            new[] { eightDays, thirtyFiveDays }, Now, AggregationWindow.ThirtyDay);

        Assert.Equal(30, snap.WindowDays);
        Assert.Equal(1, snap.TotalDisputes);
        Assert.Equal(1, snap.UpheldCount);
    }

    [Fact]
    public void UpheldRate_math_is_correct_three_upheld_over_ten_decided()
    {
        // 3 upheld, 5 rejected, 1 inReview, 1 new → 10 decidedOrPending.
        // Expected UpheldRate = 3/10 = 0.3.
        var views = new List<DiagnosticDisputeView>
        {
            View(Now.AddDays(-1), DisputeStatus.Upheld),
            View(Now.AddDays(-1), DisputeStatus.Upheld),
            View(Now.AddDays(-1), DisputeStatus.Upheld),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.InReview),
            View(Now.AddDays(-1), DisputeStatus.New),
        };

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.Equal(10, snap.TotalDisputes);
        Assert.Equal(3, snap.UpheldCount);
        Assert.Equal(0.3, snap.UpheldRate, 9);
    }

    [Fact]
    public void Alert_threshold_boundary_fires_at_exactly_five_percent()
    {
        // Exactly 5% upheld rate: 1 upheld + 19 rejected → 1/20 = 0.05.
        // The DoD phrase "5% dispute rate auto-flags" means "≥ 5%", so
        // the comparison is `>=`. Lock that semantic here.
        var views = new List<DiagnosticDisputeView> { View(Now.AddDays(-1), DisputeStatus.Upheld) };
        for (var i = 0; i < 19; i++)
        {
            views.Add(View(Now.AddDays(-1), DisputeStatus.Rejected));
        }

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.Equal(0.05, snap.UpheldRate, 9);
        Assert.True(snap.IsAboveAlertThreshold,
            "Exactly the threshold should FIRE the alert (>= semantics).");
    }

    [Fact]
    public void Alert_does_not_fire_below_threshold()
    {
        // 4.5% rate: 1 upheld + 21 rejected → 1/22 ≈ 0.0454.
        var views = new List<DiagnosticDisputeView> { View(Now.AddDays(-1), DisputeStatus.Upheld) };
        for (var i = 0; i < 21; i++)
        {
            views.Add(View(Now.AddDays(-1), DisputeStatus.Rejected));
        }

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.True(snap.UpheldRate < DisputeRateAggregator.DefaultAlertThreshold);
        Assert.False(snap.IsAboveAlertThreshold);
    }

    [Fact]
    public void Withdrawn_disputes_are_excluded_from_the_rate_denominator()
    {
        // Without withdrawn: 1/2 = 0.5 upheld rate.
        // If we incorrectly included 10 withdrawn in the denominator,
        // the rate would collapse to 1/12 ≈ 0.083, making the signal
        // invisible. Withdrawal is self-correction, not content signal.
        var views = new List<DiagnosticDisputeView>
        {
            View(Now.AddDays(-1), DisputeStatus.Upheld),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
        };
        for (var i = 0; i < 10; i++)
        {
            views.Add(View(Now.AddDays(-1), DisputeStatus.Withdrawn));
        }

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.Equal(12, snap.TotalDisputes);
        Assert.Equal(10, snap.WithdrawnCount);
        Assert.Equal(0.5, snap.UpheldRate, 9);
        Assert.True(snap.IsAboveAlertThreshold);
    }

    [Fact]
    public void PerReasonCounts_aggregates_every_reason_independently()
    {
        var views = new List<DiagnosticDisputeView>
        {
            View(Now.AddDays(-1), DisputeStatus.Upheld, DisputeReason.WrongNarration),
            View(Now.AddDays(-1), DisputeStatus.Upheld, DisputeReason.WrongNarration),
            View(Now.AddDays(-1), DisputeStatus.Rejected, DisputeReason.WrongStepIdentified),
            View(Now.AddDays(-1), DisputeStatus.Upheld, DisputeReason.OcrMisread),
            View(Now.AddDays(-1), DisputeStatus.Rejected, DisputeReason.Other),
            View(Now.AddDays(-1), DisputeStatus.Rejected, DisputeReason.Other),
            View(Now.AddDays(-1), DisputeStatus.Rejected, DisputeReason.Other),
        };

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.Equal(2, snap.PerReasonCounts[DisputeReason.WrongNarration]);
        Assert.Equal(1, snap.PerReasonCounts[DisputeReason.WrongStepIdentified]);
        Assert.Equal(1, snap.PerReasonCounts[DisputeReason.OcrMisread]);
        Assert.Equal(3, snap.PerReasonCounts[DisputeReason.Other]);

        // Per-reason upheld rates:
        //   WrongNarration: 2 upheld / 2 decided = 1.0
        //   WrongStep: 0 / 1 = 0.0
        //   OcrMisread: 1 / 1 = 1.0
        //   Other: 0 / 3 = 0.0
        Assert.Equal(1.0, snap.PerReasonUpheldRate[DisputeReason.WrongNarration], 9);
        Assert.Equal(0.0, snap.PerReasonUpheldRate[DisputeReason.WrongStepIdentified], 9);
        Assert.Equal(1.0, snap.PerReasonUpheldRate[DisputeReason.OcrMisread], 9);
        Assert.Equal(0.0, snap.PerReasonUpheldRate[DisputeReason.Other], 9);
    }

    [Fact]
    public void All_five_DisputeStatus_values_are_accounted_for_in_totals()
    {
        // One of each status — the total must equal 5 and each bucket
        // must hold exactly 1. If a new DisputeStatus is added without
        // updating the aggregator, this test will fail at enumeration
        // time because the switch throws InvalidOperationException.
        var views = new List<DiagnosticDisputeView>();
        foreach (var status in Enum.GetValues<DisputeStatus>())
        {
            views.Add(View(Now.AddDays(-1), status));
        }

        var snap = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay);

        Assert.Equal(Enum.GetValues<DisputeStatus>().Length, snap.TotalDisputes);
        Assert.Equal(1, snap.UpheldCount);
        Assert.Equal(1, snap.RejectedCount);
        Assert.Equal(1, snap.InReviewCount);
        Assert.Equal(1, snap.NewCount);
        Assert.Equal(1, snap.WithdrawnCount);

        // Sum of the per-status counts MUST equal TotalDisputes — this
        // is the "nothing hidden" invariant.
        var sum = snap.UpheldCount + snap.RejectedCount + snap.InReviewCount
            + snap.NewCount + snap.WithdrawnCount;
        Assert.Equal(snap.TotalDisputes, sum);
    }

    [Fact]
    public void Custom_alert_threshold_is_honoured()
    {
        // 20% upheld rate: 1 upheld / 5 decided.
        // With threshold 0.25 (25%) it should NOT fire.
        // With threshold 0.20 (20%) it should fire (>=).
        var views = new List<DiagnosticDisputeView>
        {
            View(Now.AddDays(-1), DisputeStatus.Upheld),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
            View(Now.AddDays(-1), DisputeStatus.Rejected),
        };

        var lenient = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay, alertThreshold: 0.25);
        var strict = DisputeRateAggregator.Aggregate(
            views, Now, AggregationWindow.SevenDay, alertThreshold: 0.20);

        Assert.Equal(0.2, lenient.UpheldRate, 9);
        Assert.False(lenient.IsAboveAlertThreshold);
        Assert.True(strict.IsAboveAlertThreshold);
    }

    [Fact]
    public void Invalid_alert_threshold_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisputeRateAggregator.Aggregate(
                Array.Empty<DiagnosticDisputeView>(),
                Now, AggregationWindow.SevenDay, alertThreshold: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DisputeRateAggregator.Aggregate(
                Array.Empty<DiagnosticDisputeView>(),
                Now, AggregationWindow.SevenDay, alertThreshold: 1.01));
    }

    [Fact]
    public void Window_edge_disputes_are_included()
    {
        // SubmittedAt exactly at (now - 7 days) is INCLUDED (>= semantics).
        var atEdge = View(Now.AddDays(-7), DisputeStatus.Upheld);
        var justOutside = View(Now.AddDays(-7).AddTicks(-1), DisputeStatus.Upheld);

        var snap = DisputeRateAggregator.Aggregate(
            new[] { atEdge, justOutside }, Now, AggregationWindow.SevenDay);

        Assert.Equal(1, snap.TotalDisputes);
    }
}
