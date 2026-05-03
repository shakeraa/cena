// =============================================================================
// Cena Platform — SubscriptionAggregate behavior tests (EPIC-PRR-I, ADR-0057)
//
// Locks in the lifecycle and command-rule behavior. These tests also
// function as executable documentation of the ADR-0057 §2/§3 invariants.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SubscriptionAggregateTests
{
    private const string ParentId = "enc::parent::abc";
    private const string PrimaryStudentId = "enc::student::primary";
    private const string SiblingStudentId = "enc::student::sibling-1";
    private const string PaymentTxnId = "enc::payment::txn-001";

    [Fact]
    public void Fresh_aggregate_starts_unsubscribed_with_no_students()
    {
        var agg = new SubscriptionAggregate();
        Assert.Equal(SubscriptionStatus.Unsubscribed, agg.State.Status);
        Assert.Equal(SubscriptionTier.Unsubscribed, agg.State.CurrentTier);
        Assert.Empty(agg.State.LinkedStudents);
    }

    [Fact]
    public void Activation_produces_active_state_with_primary_at_ordinal_zero()
    {
        var agg = new SubscriptionAggregate();
        var now = DateTimeOffset.UtcNow;
        var evt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Monthly,
            PaymentTxnId, now);
        agg.Apply(evt);

        Assert.Equal(SubscriptionStatus.Active, agg.State.Status);
        Assert.Equal(SubscriptionTier.Premium, agg.State.CurrentTier);
        Assert.Equal(BillingCycle.Monthly, agg.State.CurrentCycle);
        Assert.Single(agg.State.LinkedStudents);
        Assert.Equal(0, agg.State.LinkedStudents[0].Ordinal);
        Assert.Equal(PrimaryStudentId, agg.State.LinkedStudents[0].StudentSubjectIdEncrypted);
        Assert.Equal(now.AddMonths(1), agg.State.RenewsAt);
    }

    [Fact]
    public void Annual_activation_sets_renewal_one_year_out()
    {
        var agg = new SubscriptionAggregate();
        var now = DateTimeOffset.UtcNow;
        var evt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Annual, PaymentTxnId, now);
        agg.Apply(evt);
        Assert.Equal(now.AddYears(1), agg.State.RenewsAt);
    }

    [Fact]
    public void Double_activation_throws()
    {
        var agg = ActiveAggregate();
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.Activate(
                agg.State, ParentId, PrimaryStudentId,
                SubscriptionTier.Basic, BillingCycle.Monthly, PaymentTxnId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Sibling_link_assigns_ordinal_one_and_applies_sibling_rate()
    {
        var agg = ActiveAggregate();
        var siblingEvt = SubscriptionCommands.LinkSibling(
            agg.State, SiblingStudentId, SubscriptionTier.Premium, DateTimeOffset.UtcNow);
        agg.Apply(siblingEvt);

        Assert.Equal(2, agg.State.LinkedStudents.Count);
        Assert.Equal(1, agg.State.LinkedStudents[1].Ordinal);
        Assert.Equal(14_900L, siblingEvt.SiblingMonthlyAgorot);
    }

    [Fact]
    public void Third_sibling_gets_household_cap_rate()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.LinkSibling(agg.State, "sib-1", SubscriptionTier.Premium, DateTimeOffset.UtcNow));
        agg.Apply(SubscriptionCommands.LinkSibling(agg.State, "sib-2", SubscriptionTier.Premium, DateTimeOffset.UtcNow));
        var thirdEvt = SubscriptionCommands.LinkSibling(
            agg.State, "sib-3", SubscriptionTier.Premium, DateTimeOffset.UtcNow);

        Assert.Equal(3, thirdEvt.SiblingOrdinal);
        Assert.Equal(9_900L, thirdEvt.SiblingMonthlyAgorot);
    }

    [Fact]
    public void Duplicate_sibling_link_throws()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.LinkSibling(
            agg.State, SiblingStudentId, SubscriptionTier.Premium, DateTimeOffset.UtcNow));
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.LinkSibling(
                agg.State, SiblingStudentId, SubscriptionTier.Premium, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Downgrade_applies_at_renewal_upgrade_applies_immediately()
    {
        var agg = ActiveAggregate();   // Premium
        var now = DateTimeOffset.UtcNow;
        var renewsAt = agg.State.RenewsAt!.Value;

        // downgrade Premium → Basic
        var evt = SubscriptionCommands.ChangeTier(agg.State, SubscriptionTier.Basic, now);
        Assert.Equal(renewsAt, evt.EffectiveAt);

        // apply, then upgrade Basic → Premium
        agg.Apply(evt);
        var upEvt = SubscriptionCommands.ChangeTier(agg.State, SubscriptionTier.Premium, now);
        Assert.Equal(now, upEvt.EffectiveAt);
    }

    [Fact]
    public void Refund_within_30_days_succeeds()
    {
        var agg = ActiveAggregate();
        var now = agg.State.ActivatedAt!.Value.AddDays(10);
        var evt = SubscriptionCommands.Refund(
            agg.State, 24_900L, "customer-request", now);
        agg.Apply(evt);
        Assert.Equal(SubscriptionStatus.Refunded, agg.State.Status);
    }

    [Fact]
    public void Refund_after_30_days_throws()
    {
        var agg = ActiveAggregate();
        var now = agg.State.ActivatedAt!.Value.AddDays(31);
        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.Refund(agg.State, 24_900L, "late", now));
    }

    [Fact]
    public void Cancel_is_terminal_cannot_reactivate_on_same_stream()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.Cancel(agg.State, "user-cancel", "parent", DateTimeOffset.UtcNow));
        Assert.Equal(SubscriptionStatus.Cancelled, agg.State.Status);

        Assert.Throws<SubscriptionCommandException>(() =>
            SubscriptionCommands.Activate(
                agg.State, ParentId, PrimaryStudentId,
                SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Replay_reconstructs_state_exactly()
    {
        var agg = ActiveAggregate();
        agg.Apply(SubscriptionCommands.LinkSibling(agg.State, SiblingStudentId, SubscriptionTier.Premium, DateTimeOffset.UtcNow));

        // Build the event history that was applied above.
        var activateEvt = SubscriptionCommands.Activate(
            new SubscriptionState(), ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Monthly, PaymentTxnId, DateTimeOffset.UtcNow);
        var linkEvt = new SiblingEntitlementLinked_V1(
            ParentSubjectIdEncrypted: ParentId,
            SiblingStudentSubjectIdEncrypted: SiblingStudentId,
            SiblingOrdinal: 1,
            Tier: SubscriptionTier.Premium,
            SiblingMonthlyAgorot: 14_900L,
            LinkedAt: DateTimeOffset.UtcNow);

        var replayed = SubscriptionAggregate.ReplayFrom(new object[] { activateEvt, linkEvt });
        Assert.Equal(SubscriptionStatus.Active, replayed.State.Status);
        Assert.Equal(2, replayed.State.LinkedStudents.Count);
    }

    [Fact]
    public void Stream_key_has_parent_prefix()
    {
        var key = SubscriptionAggregate.StreamKey("parent-abc");
        Assert.Equal("subscription-parent-abc", key);
    }

    [Fact]
    public void Stream_key_rejects_empty_parent_id()
    {
        Assert.Throws<ArgumentException>(() => SubscriptionAggregate.StreamKey(""));
    }

    private static SubscriptionAggregate ActiveAggregate()
    {
        var agg = new SubscriptionAggregate();
        var evt = SubscriptionCommands.Activate(
            agg.State, ParentId, PrimaryStudentId,
            SubscriptionTier.Premium, BillingCycle.Monthly,
            PaymentTxnId, DateTimeOffset.UtcNow);
        agg.Apply(evt);
        return agg;
    }
}
