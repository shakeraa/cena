// =============================================================================
// Cena Platform — RefundPolicy tests (EPIC-PRR-I PRR-306)
//
// Covers the three DoD branches:
//   1. within-window auto-approve (monthly)
//   2. annual pro-rata math
//   3. abuse-flagged denial (diagnostics AND hints, both thresholds)
// Plus defensive branches: never-activated, outside-window.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class RefundPolicyTests
{
    private const long BasicMonthly = 3_000L;     // 30 ILS
    private const long PremiumAnnual = 600_000L;  // 6_000 ILS

    [Fact]
    public void Never_activated_is_denied()
    {
        var state = new SubscriptionState();
        var decision = RefundPolicy.Evaluate(
            state, now: DateTimeOffset.UtcNow,
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 0, usedHintRequests: 0);

        Assert.False(decision.Allowed);
        Assert.Equal("never_activated", decision.DenialReason);
        Assert.Equal(0, decision.RefundAmountAgorot);
    }

    [Fact]
    public void Within_window_monthly_approves_full_amount()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-5);
        var state = BuildState(activated, BillingCycle.Monthly);
        var decision = RefundPolicy.Evaluate(
            state, now: activated.AddDays(5),
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 10, usedHintRequests: 5);

        Assert.True(decision.Allowed);
        Assert.Equal(BasicMonthly, decision.RefundAmountAgorot);
        Assert.Null(decision.DenialReason);
    }

    [Fact]
    public void Annual_cycle_returns_pro_rata_refund()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-15);
        var state = BuildState(activated, BillingCycle.Annual);
        // 15 days of a 365-day period used on a ₪6,000 annual charge.
        // daily = 600_000 / 365 = 1643 (integer trunc).
        // consumed = 1643 * 15 = 24_645.
        // refund = 600_000 - 24_645 = 575_355.
        var decision = RefundPolicy.Evaluate(
            state, now: activated.AddDays(15),
            fullChargeAmountAgorot: PremiumAnnual,
            usedDiagnosticUploads: 0, usedHintRequests: 0);

        Assert.True(decision.Allowed);
        Assert.Equal(575_355L, decision.RefundAmountAgorot);
    }

    [Fact]
    public void Annual_cycle_on_day_one_returns_nearly_full_refund()
    {
        var activated = DateTimeOffset.UtcNow;
        var state = BuildState(activated, BillingCycle.Annual);
        // daysUsed = 0 → refund = full charge.
        var decision = RefundPolicy.Evaluate(
            state, now: activated.AddHours(1),
            fullChargeAmountAgorot: PremiumAnnual,
            usedDiagnosticUploads: 0, usedHintRequests: 0);

        Assert.True(decision.Allowed);
        Assert.Equal(PremiumAnnual, decision.RefundAmountAgorot);
    }

    [Fact]
    public void Outside_30_day_window_is_denied()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-40);
        var state = BuildState(activated, BillingCycle.Monthly);
        var decision = RefundPolicy.Evaluate(
            state, now: DateTimeOffset.UtcNow,
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 0, usedHintRequests: 0);

        Assert.False(decision.Allowed);
        Assert.Equal("outside_window", decision.DenialReason);
    }

    [Fact]
    public void Excessive_diagnostic_uploads_denies_as_abuse()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-10);
        var state = BuildState(activated, BillingCycle.Monthly);
        var decision = RefundPolicy.Evaluate(
            state, now: DateTimeOffset.UtcNow,
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 501,    // one over threshold
            usedHintRequests: 10);

        Assert.False(decision.Allowed);
        Assert.Equal("abuse_diagnostic_uploads", decision.DenialReason);
    }

    [Fact]
    public void Excessive_hint_requests_denies_as_abuse()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-10);
        var state = BuildState(activated, BillingCycle.Monthly);
        var decision = RefundPolicy.Evaluate(
            state, now: DateTimeOffset.UtcNow,
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 100,
            usedHintRequests: 51);        // one over threshold

        Assert.False(decision.Allowed);
        Assert.Equal("abuse_hint_requests", decision.DenialReason);
    }

    [Fact]
    public void Abuse_threshold_boundary_equals_is_approved()
    {
        // Exactly-at-threshold is allowed; strictly-greater is denied.
        // This boundary matters for finance — "refund denied at 500
        // uploads" vs "refund denied at 501 uploads" is a 1-bit decision
        // the CS team will be asked to defend on edge cases.
        var activated = DateTimeOffset.UtcNow.AddDays(-5);
        var state = BuildState(activated, BillingCycle.Monthly);
        var decision = RefundPolicy.Evaluate(
            state, now: DateTimeOffset.UtcNow,
            fullChargeAmountAgorot: BasicMonthly,
            usedDiagnosticUploads: 500,
            usedHintRequests: 50);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Pro_rata_helper_matches_inline_math()
    {
        // Sanity check of the public helper — daily-rate rounding behaviour
        // is integer-agorot truncating, refund never exceeds charge.
        var activated = DateTimeOffset.UtcNow.AddDays(-30);
        var now = activated.AddDays(30);
        var refund = RefundPolicy.ComputeAnnualProRata(
            365_000L, activated, now, periodDays: 365);
        // daily = 1000; consumed = 30_000; refund = 335_000.
        Assert.Equal(335_000L, refund);
    }

    [Fact]
    public void Pro_rata_helper_zero_for_fully_consumed_period()
    {
        var activated = DateTimeOffset.UtcNow.AddDays(-400);
        var now = DateTimeOffset.UtcNow;
        var refund = RefundPolicy.ComputeAnnualProRata(
            365_000L, activated, now, periodDays: 365);
        Assert.Equal(0L, refund);
    }

    private static SubscriptionState BuildState(DateTimeOffset activatedAt, BillingCycle cycle)
    {
        // Build a realistic activated state by applying the activation
        // event; this keeps the test honest about the state machine
        // rather than reaching into private fields.
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent::test",
            PrimaryStudentSubjectIdEncrypted: "enc::student::test",
            Tier: SubscriptionTier.Premium,
            Cycle: cycle,
            GrossAmountAgorot: cycle == BillingCycle.Annual ? PremiumAnnual : BasicMonthly,
            PaymentTransactionIdEncrypted: "txn_test",
            ActivatedAt: activatedAt,
            RenewsAt: cycle == BillingCycle.Annual
                ? activatedAt.AddYears(1) : activatedAt.AddMonths(1)));
        return aggregate.State;
    }
}
