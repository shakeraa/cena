// =============================================================================
// Cena Platform — MoneyBackGuaranteeWindow tests (EPIC-PRR-I PRR-294)
//
// Locks the 30-day money-back-guarantee CTA visibility rules:
//   - Never-activated → CTA hidden, reason not_activated
//   - Active within window → CTA visible, days_remaining > 0, reason
//     active_within_window
//   - Active at exactly the boundary → CTA hidden, reason expired
//     (refund endpoint uses strict `now > windowEnd` in RefundPolicy;
//     window checker mirrors with `now >= windowEnd`)
//   - PastDue → CTA hidden even inside the window, reason past_due
//   - Cancelled / Refunded → CTA hidden, reason terminal_state, window
//     end still reported for "your window closed on X" UI
//   - DaysRemaining rounds UP (ceiling) so the student never sees a
//     falsely-low number near the boundary
//   - WindowEndsAtUtc is always the activatedAt + windowDays anchor —
//     stable across the statuses above so the UI can render the same
//     absolute instant regardless of branch
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class MoneyBackGuaranteeWindowTests
{
    private static readonly DateTimeOffset ActivatedAt =
        new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    private static SubscriptionAggregate BuildActive(
        SubscriptionTier tier = SubscriptionTier.Premium,
        BillingCycle cycle = BillingCycle.Monthly)
    {
        var agg = new SubscriptionAggregate();
        var renewsAt = cycle == BillingCycle.Annual
            ? ActivatedAt.AddYears(1)
            : ActivatedAt.AddMonths(1);
        agg.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: tier,
            Cycle: cycle,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: ActivatedAt,
            RenewsAt: renewsAt));
        return agg;
    }

    [Fact]
    public void NeverActivated_hides_CTA_with_reason_not_activated()
    {
        var state = new SubscriptionState();
        var status = MoneyBackGuaranteeWindow.Evaluate(state, ActivatedAt);
        Assert.False(status.IsWithinWindow);
        Assert.Equal(0, status.DaysRemaining);
        Assert.Null(status.WindowEndsAtUtc);
        Assert.Equal(MoneyBackGuaranteeWindowReason.NotActivated, status.Reason);
    }

    [Fact]
    public void Active_on_activation_day_shows_CTA_with_full_window()
    {
        var agg = BuildActive();
        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt);
        Assert.True(status.IsWithinWindow);
        Assert.Equal(30, status.DaysRemaining);
        Assert.Equal(
            ActivatedAt.AddDays(30),
            status.WindowEndsAtUtc);
        Assert.Equal(MoneyBackGuaranteeWindowReason.ActiveWithinWindow, status.Reason);
    }

    [Fact]
    public void Active_at_15_days_shows_CTA_with_15_days_remaining()
    {
        var agg = BuildActive();
        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(15));
        Assert.True(status.IsWithinWindow);
        Assert.Equal(15, status.DaysRemaining);
    }

    [Fact]
    public void DaysRemaining_rounds_up_so_sub_day_remaining_reads_as_1()
    {
        // 29 days + 23 hours in — only 1 hour left. Whole-day ceiling
        // surfaces that as "1 day remaining" rather than "0 days". The
        // honest framing: we don't tell a parent they're out of time
        // when they still have time.
        var agg = BuildActive();
        var almostExpired = ActivatedAt.AddDays(29).AddHours(23);
        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, almostExpired);
        Assert.True(status.IsWithinWindow);
        Assert.Equal(1, status.DaysRemaining);
    }

    [Fact]
    public void At_exact_window_boundary_hides_CTA_with_reason_expired()
    {
        // At activatedAt + 30.days exactly: windowEnd has arrived; the
        // window is closed. Mirrors RefundPolicy.Evaluate which uses
        // `now > windowEnd` for strict "after" — both checks agree that
        // the window is inclusive of the instant just before but not
        // the boundary itself. A parent trying to refund at this instant
        // would be denied by RefundPolicy, so hiding the CTA here is
        // honest (never a false-positive click).
        var agg = BuildActive();
        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(30));
        Assert.False(status.IsWithinWindow);
        Assert.Equal(0, status.DaysRemaining);
        Assert.Equal(ActivatedAt.AddDays(30), status.WindowEndsAtUtc);
        Assert.Equal(MoneyBackGuaranteeWindowReason.Expired, status.Reason);
    }

    [Fact]
    public void Past_the_window_hides_CTA_with_reason_expired()
    {
        var agg = BuildActive();
        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(45));
        Assert.False(status.IsWithinWindow);
        Assert.Equal(MoneyBackGuaranteeWindowReason.Expired, status.Reason);
        // WindowEndsAtUtc still reported so UI can show "your window closed on X".
        Assert.Equal(ActivatedAt.AddDays(30), status.WindowEndsAtUtc);
    }

    [Fact]
    public void PastDue_inside_window_suppresses_CTA_with_reason_past_due()
    {
        // Payment failed at day 10 → PastDue. Window is still technically
        // open, but the CTA is suppressed because the refund flow needs
        // a successful charge to reverse. Parent must resolve payment
        // first; once payment succeeds (status → Active) the CTA lights
        // back up for the remaining window.
        var agg = BuildActive();
        agg.Apply(new PaymentFailed_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            Reason: "card_declined",
            AttemptNumber: 1,
            FailedAt: ActivatedAt.AddDays(10)));

        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(12));
        Assert.False(status.IsWithinWindow);
        Assert.Equal(MoneyBackGuaranteeWindowReason.PastDue, status.Reason);
        Assert.Equal(ActivatedAt.AddDays(30), status.WindowEndsAtUtc);
    }

    [Fact]
    public void Cancelled_hides_CTA_with_reason_terminal_state()
    {
        var agg = BuildActive();
        agg.Apply(new SubscriptionCancelled_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            Reason: "user_requested",
            Initiator: "self",
            CancelledAt: ActivatedAt.AddDays(5)));

        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(6));
        Assert.False(status.IsWithinWindow);
        Assert.Equal(MoneyBackGuaranteeWindowReason.TerminalState, status.Reason);
        Assert.Equal(ActivatedAt.AddDays(30), status.WindowEndsAtUtc);
    }

    [Fact]
    public void Refunded_hides_CTA_with_reason_terminal_state()
    {
        var agg = BuildActive();
        agg.Apply(new SubscriptionRefunded_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            RefundedAmountAgorot: 24_900L,
            Reason: "user_requested",
            RefundedAt: ActivatedAt.AddDays(7)));

        var status = MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt.AddDays(8));
        Assert.False(status.IsWithinWindow);
        Assert.Equal(MoneyBackGuaranteeWindowReason.TerminalState, status.Reason);
        Assert.Equal(ActivatedAt.AddDays(30), status.WindowEndsAtUtc);
    }

    [Fact]
    public void Custom_window_days_is_respected()
    {
        // Finance could tune the window via the RefundPolicyOptions knob;
        // the checker must use the same knob so the CTA and the refund
        // endpoint stay coherent. Verify a custom 14-day window.
        var agg = BuildActive();
        var at13 = ActivatedAt.AddDays(13);
        var at14 = ActivatedAt.AddDays(14);

        var inside = MoneyBackGuaranteeWindow.Evaluate(agg.State, at13, windowDays: 14);
        Assert.True(inside.IsWithinWindow);
        Assert.Equal(1, inside.DaysRemaining);
        Assert.Equal(ActivatedAt.AddDays(14), inside.WindowEndsAtUtc);

        var outside = MoneyBackGuaranteeWindow.Evaluate(agg.State, at14, windowDays: 14);
        Assert.False(outside.IsWithinWindow);
        Assert.Equal(MoneyBackGuaranteeWindowReason.Expired, outside.Reason);
    }

    [Fact]
    public void Zero_or_negative_window_days_throws()
    {
        var agg = BuildActive();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt, windowDays: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MoneyBackGuaranteeWindow.Evaluate(agg.State, ActivatedAt, windowDays: -1));
    }

    [Fact]
    public void Null_state_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MoneyBackGuaranteeWindow.Evaluate(null!, ActivatedAt));
    }

    [Fact]
    public void DefaultWindowDays_equals_RefundPolicy_GuaranteeWindowDays()
    {
        // Contract guard: the CTA window and the refund-eligibility window
        // must stay the same number. A typo in one side would silently
        // drift the two apart and cause "your CTA said 30 but the refund
        // says 28" support tickets.
        Assert.Equal(
            RefundPolicyOptions.Default.GuaranteeWindowDays,
            MoneyBackGuaranteeWindow.DefaultWindowDays);
    }
}
