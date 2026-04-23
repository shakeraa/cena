// =============================================================================
// Cena Platform — Annual prepay behaviour tests (EPIC-PRR-I PRR-292)
//
// PRR-292 scattered code across multiple files and prior commits:
//   - BillingCycle.Annual enum (shipped)
//   - TierCatalog AnnualPrice per retail tier (shipped; 10-for-12 ratio)
//   - SubscriptionCommands.Activate honors cycle → picks AnnualPrice
//   - SubscriptionCommands.ComputeNextRenewal(from, Annual) = from.AddYears(1)
//   - RefundPolicy pro-rata annual (shipped by PRR-306 with its own tests)
//   - BillingCycleChanged_V1 event + aggregate application (shipped)
//
// These tests lock the PRR-292 DoD contract:
//   · Annual activation charges AnnualPrice, not MonthlyPrice
//   · Annual entitlement = 365 days from activation (RenewsAt math)
//   · Savings-label math honest: AnnualPrice = MonthlyPrice × 10, never
//     some fake-discounted ₪ value
//   · Cycle downgrade (Annual → Monthly) at renewal works via the
//     BillingCycleChanged_V1 event
//   · Pro-rata refund covered by the existing PRR-306 suite
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class AnnualPrepayTests
{
    [Theory]
    [InlineData(SubscriptionTier.Basic)]
    [InlineData(SubscriptionTier.Plus)]
    [InlineData(SubscriptionTier.Premium)]
    public void Annual_price_is_ten_times_monthly_two_months_free(SubscriptionTier tier)
    {
        // Savings label rendered on the pricing page says "save ₪498"
        // (for Premium: 249×12 − 2490 = 498). That promise only holds if
        // the catalog's annual is exactly 10× monthly. The PRR-292 DoD
        // "savings badge honest (no fake anchors)" is a code-constant
        // invariant on TierCatalog — lock it here so finance cannot
        // retune only one side and ship an inconsistent number.
        var def = TierCatalog.Get(tier);
        var monthly = def.MonthlyPrice.Amount;
        var annual = def.AnnualPrice.Amount;
        Assert.Equal(monthly * 10L, annual);
    }

    [Fact]
    public void Activating_with_Annual_cycle_picks_AnnualPrice_gross()
    {
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var evt = SubscriptionCommands.Activate(
            currentState: new SubscriptionState(),
            parentSubjectIdEncrypted: "enc::parent",
            primaryStudentSubjectIdEncrypted: "enc::student",
            tier: SubscriptionTier.Premium,
            cycle: BillingCycle.Annual,
            paymentTransactionIdEncrypted: "txn",
            activatedAt: activatedAt);

        var expectedAnnual = TierCatalog.Get(SubscriptionTier.Premium).AnnualPrice.Amount;
        Assert.Equal(expectedAnnual, evt.GrossAmountAgorot);
        Assert.Equal(BillingCycle.Annual, evt.Cycle);
    }

    [Fact]
    public void Annual_RenewsAt_is_one_year_from_activation()
    {
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var evt = SubscriptionCommands.Activate(
            new SubscriptionState(),
            "enc::parent", "enc::student",
            SubscriptionTier.Premium, BillingCycle.Annual,
            "txn", activatedAt);

        // DateTime.AddYears handles leap years by preserving day-of-month;
        // May 1 → May 1 next year. Lock that's what the renewal anchor is.
        var expectedRenews = new DateTimeOffset(2027, 5, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expectedRenews, evt.RenewsAt);
    }

    [Fact]
    public void Monthly_RenewsAt_is_one_month_from_activation()
    {
        // Mirror the annual test so the ComputeNextRenewal math matrix
        // is fully locked: Monthly adds one month, Annual adds one year.
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        var evt = SubscriptionCommands.Activate(
            new SubscriptionState(),
            "enc::parent", "enc::student",
            SubscriptionTier.Premium, BillingCycle.Monthly,
            "txn", activatedAt);

        Assert.Equal(
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            evt.RenewsAt);
    }

    [Fact]
    public void IsActiveAsOf_returns_true_at_11_months_after_annual_activation()
    {
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var state = BuildActiveState(SubscriptionTier.Premium, BillingCycle.Annual, activatedAt);

        // 11 months in → well within the 1-year entitlement.
        var checkAt = activatedAt.AddMonths(11);
        Assert.True(state.IsActiveAsOf(checkAt));
    }

    [Fact]
    public void IsActiveAsOf_returns_false_at_exactly_one_year_after_activation()
    {
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var state = BuildActiveState(SubscriptionTier.Premium, BillingCycle.Annual, activatedAt);

        // At the renewal boundary the active flag flips off — the
        // renewal event is what re-activates it for the next period.
        // IsActiveAsOf is `Status == Active && RenewsAt > now` so
        // now == RenewsAt returns false.
        var renewsAt = state.RenewsAt!.Value;
        Assert.False(state.IsActiveAsOf(renewsAt));
    }

    [Fact]
    public void Cycle_downgrade_Annual_to_Monthly_is_applied_to_state()
    {
        // At renewal boundary the parent can flip to Monthly. Emits
        // BillingCycleChanged_V1; aggregate state reflects the new cycle.
        var activatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Annual,
            GrossAmountAgorot: TierCatalog.Get(SubscriptionTier.Premium).AnnualPrice.Amount,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: activatedAt,
            RenewsAt: activatedAt.AddYears(1)));

        Assert.Equal(BillingCycle.Annual, aggregate.State.CurrentCycle);

        var changeAt = activatedAt.AddYears(1);
        aggregate.Apply(new BillingCycleChanged_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            FromCycle: BillingCycle.Annual,
            ToCycle: BillingCycle.Monthly,
            ChangedAt: changeAt,
            EffectiveAt: changeAt));

        Assert.Equal(BillingCycle.Monthly, aggregate.State.CurrentCycle);
    }

    [Fact]
    public void ComputeNextRenewal_rejects_None_cycle()
    {
        var from = DateTimeOffset.UtcNow;
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.ComputeNextRenewal(from, BillingCycle.None));
    }

    [Fact]
    public void Savings_label_two_months_free_holds_at_all_retail_tiers()
    {
        // Alternate framing of the annual ratio — the pricing page
        // renders "save X months" per tier. All retail tiers must
        // share the same 2-months-free ratio (DoD "same 2-months-free
        // ratio").
        foreach (var tier in new[]
        {
            SubscriptionTier.Basic, SubscriptionTier.Plus, SubscriptionTier.Premium,
        })
        {
            var def = TierCatalog.Get(tier);
            var savedAgorot = (def.MonthlyPrice.Amount * 12L) - def.AnnualPrice.Amount;
            var savedMonths = (double)savedAgorot / def.MonthlyPrice.Amount;
            Assert.InRange(savedMonths, 1.99, 2.01);
        }
    }

    private static SubscriptionState BuildActiveState(
        SubscriptionTier tier, BillingCycle cycle, DateTimeOffset activatedAt)
    {
        var renewsAt = cycle == BillingCycle.Annual
            ? activatedAt.AddYears(1)
            : activatedAt.AddMonths(1);
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: tier,
            Cycle: cycle,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: activatedAt,
            RenewsAt: renewsAt));
        return aggregate.State;
    }
}
