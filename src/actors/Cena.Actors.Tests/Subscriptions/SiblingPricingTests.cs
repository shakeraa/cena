// =============================================================================
// Cena Platform — Sibling pricing + pro-rata unlink tests (EPIC-PRR-I PRR-293)
//
// Covers the PRR-293 DoD:
//   - Adding 1st sibling = +₪149 on next invoice
//   - Adding 3rd sibling = +₪99 (not ₪149)
//   - Removal pro-rates correctly
// The TierCatalog.SiblingMonthlyPrice ordinal-discount tier is locked
// separately in TierCatalogTests; here we lock the COMMAND-level
// behaviour so a future refactor to the aggregate cannot silently
// charge the wrong ordinal's price or miscompute the pro-rata.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SiblingPricingTests
{
    private const long FirstSecondSiblingAgorot = 14_900L;  // ₪149
    private const long ThirdPlusSiblingAgorot = 9_900L;     // ₪99

    [Fact]
    public void First_sibling_added_carries_149_ils_price()
    {
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);

        var evt = SubscriptionCommands.LinkSibling(
            aggregate.State,
            siblingStudentSubjectIdEncrypted: "enc::student::sib1",
            siblingTier: SubscriptionTier.Premium,
            now: activatedAt.AddHours(1));

        Assert.Equal(1, evt.SiblingOrdinal);
        Assert.Equal(FirstSecondSiblingAgorot, evt.SiblingMonthlyAgorot);
    }

    [Fact]
    public void Second_sibling_still_carries_149_ils_price()
    {
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1", SubscriptionTier.Premium, activatedAt.AddHours(1)));

        var second = SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib2",
            SubscriptionTier.Premium, activatedAt.AddHours(2));

        Assert.Equal(2, second.SiblingOrdinal);
        Assert.Equal(FirstSecondSiblingAgorot, second.SiblingMonthlyAgorot);
    }

    [Fact]
    public void Third_sibling_drops_to_99_ils_price()
    {
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1", SubscriptionTier.Premium, activatedAt.AddHours(1)));
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib2", SubscriptionTier.Premium, activatedAt.AddHours(2)));

        var third = SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib3",
            SubscriptionTier.Premium, activatedAt.AddHours(3));

        Assert.Equal(3, third.SiblingOrdinal);
        Assert.Equal(ThirdPlusSiblingAgorot, third.SiblingMonthlyAgorot);
    }

    [Fact]
    public void Two_sibling_household_total_is_249_plus_149_plus_149()
    {
        // Premium monthly = 24,900 agorot (₪249). Two siblings at ordinal
        // 1 + 2 = 14,900 + 14,900 = 29,800 agorot. Household total
        // monthly invoice = 24,900 + 29,800 = 54,700 agorot (₪547).
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1", SubscriptionTier.Premium, activatedAt.AddHours(1)));
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib2", SubscriptionTier.Premium, activatedAt.AddHours(2)));

        var siblings = aggregate.State.LinkedStudents
            .Where(s => s.Ordinal > 0)
            .ToArray();

        Assert.Equal(2, siblings.Length);
        var siblingMonthly = siblings.Sum(s =>
            TierCatalog.SiblingMonthlyPrice(s.Ordinal).Amount);
        var primaryMonthly = TierCatalog
            .Get(aggregate.State.CurrentTier).MonthlyPrice.Amount;
        Assert.Equal(24_900L + 14_900L + 14_900L, primaryMonthly + siblingMonthly);
    }

    [Fact]
    public void Three_sibling_household_total_includes_99_ils_third()
    {
        // Premium + 3 siblings = 24,900 + 14,900 + 14,900 + 9,900 =
        // 64,600 agorot (₪646). Third sibling at the lower tier is the
        // household-cap design (persona #5).
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1", SubscriptionTier.Premium, activatedAt.AddHours(1)));
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib2", SubscriptionTier.Premium, activatedAt.AddHours(2)));
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib3", SubscriptionTier.Premium, activatedAt.AddHours(3)));

        var siblingMonthly = aggregate.State.LinkedStudents
            .Where(s => s.Ordinal > 0)
            .Sum(s => TierCatalog.SiblingMonthlyPrice(s.Ordinal).Amount);
        var primaryMonthly = TierCatalog
            .Get(aggregate.State.CurrentTier).MonthlyPrice.Amount;
        Assert.Equal(24_900L + 14_900L + 14_900L + 9_900L,
            primaryMonthly + siblingMonthly);
    }

    [Fact]
    public void Sibling_unlink_halfway_through_monthly_cycle_credits_half_the_price()
    {
        // A monthly sibling at ₪149 unlinked exactly 15 days into a
        // 30-day cycle credits ~half — integer truncation may yield a
        // value a few seconds shy of half, so we use a generous bound.
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-15);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1",
            SubscriptionTier.Premium, activatedAt.AddSeconds(1)));

        var now = DateTimeOffset.UtcNow;  // 15 days later
        var evt = SubscriptionCommands.UnlinkSibling(
            aggregate.State, "enc::sib1", now);

        // Expected credit ≈ 14900 × 15/30 = 7450 ± clock slippage.
        Assert.True(evt.ProRataCreditAgorot >= 7_300L,
            $"Credit too low: {evt.ProRataCreditAgorot}");
        Assert.True(evt.ProRataCreditAgorot <= 7_500L,
            $"Credit too high: {evt.ProRataCreditAgorot}");
    }

    [Fact]
    public void Sibling_unlink_at_cycle_end_credits_zero()
    {
        // Unlink at the exact renewal boundary → zero remainder → zero credit.
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1",
            SubscriptionTier.Premium, activatedAt.AddSeconds(1)));

        var renewsAt = aggregate.State.RenewsAt!.Value;
        var evt = SubscriptionCommands.UnlinkSibling(
            aggregate.State, "enc::sib1", renewsAt);
        Assert.Equal(0L, evt.ProRataCreditAgorot);
    }

    [Fact]
    public void Sibling_unlink_on_day_one_credits_nearly_full_monthly()
    {
        // Unlink one hour after link at cycle start → ≥ 99% of the
        // monthly price credited.
        var activatedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);
        Apply(aggregate, SubscriptionCommands.LinkSibling(
            aggregate.State, "enc::sib1",
            SubscriptionTier.Premium, activatedAt.AddSeconds(1)));

        var evt = SubscriptionCommands.UnlinkSibling(
            aggregate.State, "enc::sib1", DateTimeOffset.UtcNow);
        Assert.True(evt.ProRataCreditAgorot >= 14_700L,
            $"Expected near-full monthly credit; got {evt.ProRataCreditAgorot}");
    }

    [Fact]
    public void Unlinking_primary_student_throws()
    {
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-5);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);

        var ex = Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.UnlinkSibling(
                aggregate.State,
                "enc::student::primary",
                DateTimeOffset.UtcNow));
        Assert.Contains("Primary student", ex.Message);
    }

    [Fact]
    public void Unlinking_unknown_sibling_throws()
    {
        var activatedAt = DateTimeOffset.UtcNow.AddDays(-5);
        var aggregate = BuildPremiumMonthlyActive(activatedAt);

        var ex = Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.UnlinkSibling(
                aggregate.State,
                "enc::student::nonexistent",
                DateTimeOffset.UtcNow));
        Assert.Contains("not linked", ex.Message);
    }

    private static SubscriptionAggregate BuildPremiumMonthlyActive(DateTimeOffset activatedAt)
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent::test",
            PrimaryStudentSubjectIdEncrypted: "enc::student::primary",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn_test",
            ActivatedAt: activatedAt,
            RenewsAt: activatedAt.AddDays(30)));
        return aggregate;
    }

    private static void Apply(SubscriptionAggregate aggregate, object evt)
    {
        aggregate.Apply(evt);
    }
}
