// =============================================================================
// Cena Platform — DiscountAssignment + Commands tests (per-user discount-codes)
//
// Coverage matrix:
//   DiscountAssignmentCommands.Issue
//     - Validation: percent in [1,10000], amount > 0, amount ≤ tier-cap,
//       duration in [1,36], reason required + length-bounded, kind valid,
//       email normalized non-empty, admin id non-empty, assignment id non-empty
//     - Happy path returns DiscountIssued_V1 with the right fields
//   DiscountAssignmentCommands.Redeem
//     - Rejects already-redeemed / already-revoked / not-issued
//     - Happy path returns DiscountRedeemed_V1 with carried fields
//   DiscountAssignmentCommands.Revoke
//     - Rejects already-redeemed / already-revoked / not-issued
//     - Defaults blank reason to "admin_revoked"
//     - Happy path returns DiscountRevoked_V1
//   DiscountAssignment aggregate
//     - Apply DiscountIssued moves status None→Issued
//     - Apply DiscountRedeemed moves Issued→Redeemed
//     - Apply DiscountRevoked moves Issued→Revoked
//     - Replay event sequence rebuilds canonical state
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class DiscountAssignmentTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    // ---- Issue happy path -------------------------------------------------

    [Fact]
    public void Issue_happy_path_produces_event_with_all_fields()
    {
        var evt = DiscountAssignmentCommands.Issue(
            assignmentId: "da_abc",
            targetEmailNormalized: "alice@gmail.com",
            kind: DiscountKind.PercentOff,
            value: 5_000,
            durationMonths: 3,
            tierAnnualPriceAgorotForAmountOffCheck: 249_000,
            issuedByAdminSubjectIdEncrypted: "enc::admin",
            reason: "loyalty discount",
            stripeCouponId: "cou_test",
            stripePromotionCodeId: "promo_test",
            issuedAt: NowUtc);

        Assert.Equal("da_abc", evt.AssignmentId);
        Assert.Equal("alice@gmail.com", evt.TargetEmailNormalized);
        Assert.Equal(DiscountKind.PercentOff, evt.DiscountKind);
        Assert.Equal(5_000, evt.DiscountValue);
        Assert.Equal(3, evt.DurationMonths);
        Assert.Equal("enc::admin", evt.IssuedByAdminSubjectIdEncrypted);
        Assert.Equal("loyalty discount", evt.Reason);
        Assert.Equal("cou_test", evt.StripeCouponId);
        Assert.Equal("promo_test", evt.StripePromotionCodeId);
        Assert.Equal(NowUtc, evt.IssuedAt);
    }

    // ---- Issue validation -------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Issue_rejects_empty_assignment_id(string? assignmentId)
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                assignmentId: assignmentId!,
                targetEmailNormalized: "alice@gmail.com",
                kind: DiscountKind.PercentOff, value: 1_000, durationMonths: 3,
                tierAnnualPriceAgorotForAmountOffCheck: 249_000,
                issuedByAdminSubjectIdEncrypted: "enc::admin",
                reason: "x",
                stripeCouponId: "c", stripePromotionCodeId: "p",
                issuedAt: NowUtc));
        Assert.Equal("invalid_assignment_id", ex.ReasonCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Issue_rejects_invalid_percent_basis_points(int value)
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com",
                DiscountKind.PercentOff, value, 3,
                249_000, "enc::admin", "x", "c", "p", NowUtc));
        Assert.Equal("invalid_percent_value", ex.ReasonCode);
        Assert.Equal("discountValue", ex.Field);
    }

    [Fact]
    public void Issue_accepts_percent_at_boundary()
    {
        // 1 basis point and 10_000 basis points are both valid.
        DiscountAssignmentCommands.Issue(
            "da_x", "alice@gmail.com", DiscountKind.PercentOff,
            1, 1, 249_000, "enc::admin", "r", "c", "p", NowUtc);
        DiscountAssignmentCommands.Issue(
            "da_x", "alice@gmail.com", DiscountKind.PercentOff,
            10_000, 36, 249_000, "enc::admin", "r", "c", "p", NowUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Issue_rejects_amount_off_non_positive(int value)
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", DiscountKind.AmountOff,
                value, 3, 249_000, "enc::admin", "x", "c", "p", NowUtc));
        Assert.Equal("invalid_amount_value", ex.ReasonCode);
    }

    [Fact]
    public void Issue_rejects_amount_off_exceeding_tier_cap()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", DiscountKind.AmountOff,
                value: 250_000, durationMonths: 3,
                tierAnnualPriceAgorotForAmountOffCheck: 249_000,
                issuedByAdminSubjectIdEncrypted: "enc::admin",
                reason: "x", stripeCouponId: "c", stripePromotionCodeId: "p",
                issuedAt: NowUtc));
        Assert.Equal("amount_exceeds_tier_price", ex.ReasonCode);
        Assert.Equal("discountValue", ex.Field);
    }

    [Fact]
    public void Issue_amount_off_at_cap_is_accepted()
    {
        DiscountAssignmentCommands.Issue(
            "da_x", "alice@gmail.com", DiscountKind.AmountOff,
            value: 249_000, durationMonths: 3,
            tierAnnualPriceAgorotForAmountOffCheck: 249_000,
            issuedByAdminSubjectIdEncrypted: "enc::admin",
            reason: "x", stripeCouponId: "c", stripePromotionCodeId: "p",
            issuedAt: NowUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(37)]
    public void Issue_rejects_invalid_duration(int months)
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", DiscountKind.PercentOff,
                1_000, months, 249_000, "enc::admin", "x", "c", "p", NowUtc));
        Assert.Equal("invalid_duration", ex.ReasonCode);
    }

    [Fact]
    public void Issue_rejects_blank_reason()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", DiscountKind.PercentOff,
                1_000, 3, 249_000, "enc::admin", " ", "c", "p", NowUtc));
        Assert.Equal("invalid_reason", ex.ReasonCode);
    }

    [Fact]
    public void Issue_rejects_overly_long_reason()
    {
        var longReason = new string('x', DiscountAssignmentCommands.MaxReasonLength + 1);
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", DiscountKind.PercentOff,
                1_000, 3, 249_000, "enc::admin", longReason, "c", "p", NowUtc));
        Assert.Equal("reason_too_long", ex.ReasonCode);
    }

    [Fact]
    public void Issue_rejects_invalid_kind()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "alice@gmail.com", (DiscountKind)99,
                1_000, 3, 249_000, "enc::admin", "r", "c", "p", NowUtc));
        Assert.Equal("invalid_discount_kind", ex.ReasonCode);
    }

    [Fact]
    public void Issue_rejects_blank_email()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Issue(
                "da_x", "", DiscountKind.PercentOff,
                1_000, 3, 249_000, "enc::admin", "r", "c", "p", NowUtc));
        Assert.Equal("invalid_email", ex.ReasonCode);
    }

    // ---- Redeem -----------------------------------------------------------

    [Fact]
    public void Redeem_happy_path()
    {
        var state = NewIssuedState();
        var evt = DiscountAssignmentCommands.Redeem(
            state, "enc::parent", "sub_abc", NowUtc.AddDays(2));
        Assert.Equal(state.AssignmentId, evt.AssignmentId);
        Assert.Equal(state.TargetEmailNormalized, evt.TargetEmailNormalized);
        Assert.Equal("enc::parent", evt.ParentSubjectIdEncrypted);
        Assert.Equal("sub_abc", evt.StripeSubscriptionId);
        Assert.Equal(NowUtc.AddDays(2), evt.RedeemedAt);
    }

    [Fact]
    public void Redeem_rejects_after_redeem()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRedeemed_V1(
            "da_x", "alice@gmail.com", "enc::p", "sub_a", NowUtc));

        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Redeem(
                aggregate.State, "enc::p", "sub_b", NowUtc.AddDays(1)));
        Assert.Equal("already_redeemed", ex.ReasonCode);
    }

    [Fact]
    public void Redeem_rejects_after_revoke()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRevoked_V1(
            "da_x", "alice@gmail.com", "enc::admin", "x", NowUtc));

        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Redeem(
                aggregate.State, "enc::p", "sub_a", NowUtc.AddDays(1)));
        Assert.Equal("already_revoked", ex.ReasonCode);
    }

    [Fact]
    public void Redeem_rejects_blank_parent()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Redeem(
                NewIssuedState(), " ", "sub_a", NowUtc.AddDays(1)));
        Assert.Equal("invalid_parent", ex.ReasonCode);
    }

    [Fact]
    public void Redeem_rejects_when_not_issued()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Redeem(
                new DiscountAssignmentState(), "enc::p", "sub_a", NowUtc));
        Assert.Equal("not_issued", ex.ReasonCode);
    }

    // ---- Revoke -----------------------------------------------------------

    [Fact]
    public void Revoke_happy_path()
    {
        var state = NewIssuedState();
        var evt = DiscountAssignmentCommands.Revoke(
            state, "enc::admin-2", "user requested", NowUtc.AddDays(1));
        Assert.Equal("enc::admin-2", evt.RevokedByAdminSubjectIdEncrypted);
        Assert.Equal("user requested", evt.Reason);
        Assert.Equal(NowUtc.AddDays(1), evt.RevokedAt);
    }

    [Fact]
    public void Revoke_defaults_blank_reason()
    {
        var state = NewIssuedState();
        var evt = DiscountAssignmentCommands.Revoke(
            state, "enc::admin-2", " ", NowUtc.AddDays(1));
        Assert.Equal("admin_revoked", evt.Reason);
    }

    [Fact]
    public void Revoke_rejects_after_redeem()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRedeemed_V1(
            "da_x", "alice@gmail.com", "enc::p", "sub_a", NowUtc));

        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Revoke(
                aggregate.State, "enc::admin", "x", NowUtc.AddDays(1)));
        Assert.Equal("already_redeemed", ex.ReasonCode);
    }

    [Fact]
    public void Revoke_rejects_double_revoke()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRevoked_V1(
            "da_x", "alice@gmail.com", "enc::admin", "x", NowUtc));

        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Revoke(
                aggregate.State, "enc::admin-2", "y", NowUtc.AddDays(1)));
        Assert.Equal("already_revoked", ex.ReasonCode);
    }

    [Fact]
    public void Revoke_rejects_blank_admin()
    {
        var ex = Assert.Throws<DiscountCommandException>(() =>
            DiscountAssignmentCommands.Revoke(
                NewIssuedState(), " ", "x", NowUtc));
        Assert.Equal("invalid_admin", ex.ReasonCode);
    }

    // ---- Aggregate apply / replay -----------------------------------------

    [Fact]
    public void Aggregate_apply_issued_moves_to_Issued()
    {
        var aggregate = new DiscountAssignment();
        Assert.Equal(DiscountStatus.None, aggregate.State.Status);
        aggregate.Apply(NewIssuedEvent());
        Assert.Equal(DiscountStatus.Issued, aggregate.State.Status);
        Assert.Equal("alice@gmail.com", aggregate.State.TargetEmailNormalized);
        Assert.Equal(DiscountKind.PercentOff, aggregate.State.Kind);
        Assert.Equal(5_000, aggregate.State.Value);
    }

    [Fact]
    public void Aggregate_apply_redeemed_moves_to_Redeemed()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRedeemed_V1(
            "da_x", "alice@gmail.com", "enc::p", "sub_a", NowUtc.AddDays(1)));
        Assert.Equal(DiscountStatus.Redeemed, aggregate.State.Status);
        Assert.Equal("enc::p", aggregate.State.RedeemedByParentSubjectIdEncrypted);
    }

    [Fact]
    public void Aggregate_apply_revoked_moves_to_Revoked()
    {
        var aggregate = new DiscountAssignment();
        aggregate.Apply(NewIssuedEvent());
        aggregate.Apply(new DiscountRevoked_V1(
            "da_x", "alice@gmail.com", "enc::admin-z", "x", NowUtc.AddDays(2)));
        Assert.Equal(DiscountStatus.Revoked, aggregate.State.Status);
        Assert.Equal("enc::admin-z", aggregate.State.RevokedByAdminSubjectIdEncrypted);
    }

    [Fact]
    public void Aggregate_replay_rebuilds_state()
    {
        var events = new object[]
        {
            NewIssuedEvent(),
            new DiscountRedeemed_V1("da_x", "alice@gmail.com", "enc::p", "sub_a", NowUtc.AddDays(1)),
        };
        var rebuilt = DiscountAssignment.ReplayFrom(events);
        Assert.Equal(DiscountStatus.Redeemed, rebuilt.State.Status);
        Assert.Equal(NowUtc.AddDays(1), rebuilt.State.RedeemedAt);
    }

    [Fact]
    public void StreamKey_throws_on_blank_id()
    {
        Assert.Throws<ArgumentException>(() => DiscountAssignment.StreamKey(""));
    }

    [Fact]
    public void StreamKey_uses_known_prefix()
    {
        Assert.Equal("discount-da_x", DiscountAssignment.StreamKey("da_x"));
    }

    // ---- Helpers ----------------------------------------------------------

    private static DiscountIssued_V1 NewIssuedEvent() => new(
        AssignmentId: "da_x",
        TargetEmailNormalized: "alice@gmail.com",
        DiscountKind: DiscountKind.PercentOff,
        DiscountValue: 5_000,
        DurationMonths: 3,
        IssuedByAdminSubjectIdEncrypted: "enc::admin",
        Reason: "loyalty",
        StripeCouponId: "cou_x",
        StripePromotionCodeId: "promo_x",
        IssuedAt: NowUtc);

    private static DiscountAssignmentState NewIssuedState()
    {
        var s = new DiscountAssignmentState();
        s.Apply(NewIssuedEvent());
        return s;
    }
}
