// =============================================================================
// Cena Platform — DiscountAssignmentService + InMemory store + provider tests
// (per-user discount-codes feature)
//
// Coverage matrix:
//   InMemoryDiscountCouponProvider
//     - Create returns deterministic ids tied to assignment id
//     - Revoke is idempotent
//   InMemoryDiscountAssignmentStore
//     - Append + Load round-trip
//     - FindActiveByEmailAsync returns most recent Issued for that email
//     - FindActiveByEmailAsync returns null after revoke
//     - FindActiveByEmailAsync returns null after redeem
//     - ListByEmailAsync returns all statuses, newest-first
//     - ListRecentAsync returns newest-first capped at limit
//   DiscountAssignmentService
//     - Issue happy path: persists, returns AssignmentId + PromotionCodeString
//     - Issue normalizes the email (Alice@Gmail.com → alice@gmail.com)
//     - Issue rejects invalid email shape
//     - Issue rejects when an active assignment already exists for the email
//     - Issue with a normalized-Gmail-equivalent rejects (a.l.i.c.e+x@gmail.com)
//     - Issue triggers the email dispatcher
//     - Issue rolls back the gateway coupon on validation failure (clean leak)
//     - Revoke happy path: appends Revoked, calls gateway.Revoke
//     - Revoke not-found: throws not_found
//     - Revoke after redeem: throws already_redeemed
//     - Redeem idempotent on already-Redeemed (drops silently, no throw)
//     - FindActiveForEmailAsync round-trips through the normalizer
//     - Pricing-cap: AmountOff cannot exceed max retail annual price
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class DiscountAssignmentServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    // ---- InMemoryDiscountCouponProvider -----------------------------------

    [Fact]
    public async Task InMemoryProvider_create_returns_deterministic_ids()
    {
        var provider = new InMemoryDiscountCouponProvider();
        var result = await provider.CreateCouponAsync(new CouponCreateRequest(
            AssignmentId: "abc",
            TargetEmailNormalized: "alice@gmail.com",
            DiscountKind: DiscountKind.PercentOff,
            DiscountValue: 5_000,
            DurationMonths: 3), default);

        Assert.Equal("cou_inmem_abc", result.CouponId);
        Assert.Equal("promo_inmem_abc", result.PromotionCodeId);
        Assert.Equal("CENA-ABC", result.PromotionCodeString);
    }

    [Fact]
    public async Task InMemoryProvider_revoke_is_idempotent()
    {
        var provider = new InMemoryDiscountCouponProvider();
        await provider.CreateCouponAsync(new CouponCreateRequest(
            "abc", "alice@gmail.com", DiscountKind.PercentOff, 5_000, 3), default);
        await provider.RevokeCouponAsync(new CouponRevokeRequest(
            "abc", "cou_inmem_abc", "promo_inmem_abc"), default);
        await provider.RevokeCouponAsync(new CouponRevokeRequest(
            "abc", "cou_inmem_abc", "promo_inmem_abc"), default); // idempotent
        Assert.Single(provider.Revoked);
    }

    [Fact]
    public async Task InMemoryProvider_revoke_with_blank_coupon_id_is_noop()
    {
        var provider = new InMemoryDiscountCouponProvider();
        await provider.RevokeCouponAsync(new CouponRevokeRequest("abc", "", ""), default);
        Assert.Empty(provider.Revoked);
    }

    // ---- InMemoryDiscountAssignmentStore ----------------------------------

    [Fact]
    public async Task InMemoryStore_append_then_load_roundtrip()
    {
        var store = new InMemoryDiscountAssignmentStore();
        var evt = NewIssuedEvent("a1", "alice@gmail.com");
        await store.AppendAsync("a1", evt, default);

        var aggregate = await store.LoadAsync("a1", default);
        Assert.Equal(DiscountStatus.Issued, aggregate.State.Status);
        Assert.Equal("alice@gmail.com", aggregate.State.TargetEmailNormalized);
    }

    [Fact]
    public async Task InMemoryStore_find_active_returns_most_recent_issued()
    {
        var store = new InMemoryDiscountAssignmentStore();

        // First active assignment for alice
        await store.AppendAsync("a1", NewIssuedEvent("a1", "alice@gmail.com",
            issuedAt: NowUtc), default);
        // Later issuance for bob (separate stream, separate email)
        await store.AppendAsync("a2", NewIssuedEvent("a2", "bob@example.com",
            issuedAt: NowUtc.AddMinutes(5)), default);

        var hit = await store.FindActiveByEmailAsync("alice@gmail.com", default);
        Assert.NotNull(hit);
        Assert.Equal("a1", hit!.AssignmentId);
    }

    [Fact]
    public async Task InMemoryStore_find_active_skips_revoked()
    {
        var store = new InMemoryDiscountAssignmentStore();
        await store.AppendAsync("a1", NewIssuedEvent("a1", "alice@gmail.com"), default);
        await store.AppendAsync("a1", new DiscountRevoked_V1(
            "a1", "alice@gmail.com", "enc::admin", "x", NowUtc.AddMinutes(1)), default);

        var hit = await store.FindActiveByEmailAsync("alice@gmail.com", default);
        Assert.Null(hit);
    }

    [Fact]
    public async Task InMemoryStore_find_active_skips_redeemed()
    {
        var store = new InMemoryDiscountAssignmentStore();
        await store.AppendAsync("a1", NewIssuedEvent("a1", "alice@gmail.com"), default);
        await store.AppendAsync("a1", new DiscountRedeemed_V1(
            "a1", "alice@gmail.com", "enc::p", "sub_a", NowUtc.AddMinutes(1)), default);

        var hit = await store.FindActiveByEmailAsync("alice@gmail.com", default);
        Assert.Null(hit);
    }

    [Fact]
    public async Task InMemoryStore_list_by_email_returns_all_statuses_newest_first()
    {
        var store = new InMemoryDiscountAssignmentStore();
        await store.AppendAsync("a1", NewIssuedEvent("a1", "alice@gmail.com",
            issuedAt: NowUtc), default);
        await store.AppendAsync("a1", new DiscountRevoked_V1(
            "a1", "alice@gmail.com", "enc::admin", "x", NowUtc.AddMinutes(1)), default);

        await store.AppendAsync("a2", NewIssuedEvent("a2", "alice@gmail.com",
            issuedAt: NowUtc.AddDays(1)), default);

        var list = await store.ListByEmailAsync("alice@gmail.com", default);
        Assert.Equal(2, list.Count);
        // Newest first.
        Assert.Equal("a2", list[0].AssignmentId);
        Assert.Equal("a1", list[1].AssignmentId);
        Assert.Equal(DiscountStatus.Issued, list[0].Status);
        Assert.Equal(DiscountStatus.Revoked, list[1].Status);
    }

    [Fact]
    public async Task InMemoryStore_list_recent_caps_at_limit()
    {
        var store = new InMemoryDiscountAssignmentStore();
        for (int i = 0; i < 7; i++)
        {
            var id = $"a{i}";
            await store.AppendAsync(id, NewIssuedEvent(
                id, $"u{i}@example.com", issuedAt: NowUtc.AddSeconds(i)), default);
        }
        var list = await store.ListRecentAsync(limit: 3, default);
        Assert.Equal(3, list.Count);
        Assert.Equal("a6", list[0].AssignmentId);
        Assert.Equal("a4", list[2].AssignmentId);
    }

    // ---- Service ----------------------------------------------------------

    [Fact]
    public async Task Service_issue_happy_path()
    {
        var (svc, _, provider, dispatcher) = NewService();
        var result = await svc.IssueAsync(
            rawTargetEmail: "Alice@Gmail.com",
            kind: DiscountKind.PercentOff,
            value: 5_000,
            durationMonths: 3,
            issuedByAdminSubjectIdEncrypted: "enc::admin",
            reason: "loyalty",
            ct: default);

        Assert.StartsWith("da_", result.AssignmentId);
        Assert.NotEmpty(result.PromotionCodeString);
        Assert.Single(provider.Created);
        Assert.True(dispatcher.LastCalled);
        Assert.Equal("alice@gmail.com", dispatcher.LastEmail);
        Assert.Equal(DiscountKind.PercentOff, dispatcher.LastKind);
    }

    [Fact]
    public async Task Service_issue_normalizes_the_email()
    {
        var (svc, store, _, _) = NewService();
        var result = await svc.IssueAsync(
            "Alice+study@GoogleMail.COM", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "loyalty", default);
        var aggregate = await store.LoadAsync(result.AssignmentId, default);
        Assert.Equal("alice@gmail.com", aggregate.State.TargetEmailNormalized);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@no-local.com")]
    [InlineData("missing@domain")]
    public async Task Service_issue_rejects_invalid_email_shape(string badEmail)
    {
        var (svc, _, _, _) = NewService();
        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.IssueAsync(badEmail, DiscountKind.PercentOff,
                5_000, 3, "enc::admin", "x", default));
        Assert.Equal("invalid_email_format", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_issue_rejects_when_active_exists_for_same_email()
    {
        var (svc, _, _, _) = NewService();
        await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "first", default);
        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
                5_000, 3, "enc::admin", "second", default));
        Assert.Equal("discount_already_active", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_issue_rejects_when_active_exists_for_normalized_equivalent()
    {
        var (svc, _, _, _) = NewService();
        await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "first", default);

        // Same normalized form via Gmail dot/+ trickery should collide.
        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.IssueAsync("A.L.I.C.E+study@GoogleMail.COM", DiscountKind.PercentOff,
                5_000, 3, "enc::admin", "second", default));
        Assert.Equal("discount_already_active", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_issue_rolls_back_gateway_coupon_on_validation_failure()
    {
        var (svc, _, provider, _) = NewService();
        // Pass an invalid duration so the command rejects AFTER the coupon was minted.
        await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
                5_000, durationMonths: 999, "enc::admin", "x", default));
        // Provider.Created has the coupon, but Revoked also has the same id.
        Assert.Single(provider.Created);
        Assert.Single(provider.Revoked);
    }

    [Fact]
    public async Task Service_issue_amount_off_capped_at_max_retail_annual()
    {
        var (svc, _, _, _) = NewService();
        var max = DiscountAssignmentService.MaxAmountOffAgorot();
        Assert.True(max > 0);

        // At cap is OK
        await svc.IssueAsync("alice@gmail.com", DiscountKind.AmountOff,
            (int)max, 3, "enc::admin", "x", default);

        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.IssueAsync("bob@gmail.com", DiscountKind.AmountOff,
                (int)max + 1, 3, "enc::admin", "x", default));
        Assert.Equal("amount_exceeds_tier_price", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_revoke_happy_path()
    {
        var (svc, store, provider, _) = NewService();
        var issued = await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);
        await svc.RevokeAsync(issued.AssignmentId, "enc::admin-2", "fixed", default);

        var aggregate = await store.LoadAsync(issued.AssignmentId, default);
        Assert.Equal(DiscountStatus.Revoked, aggregate.State.Status);
        Assert.Equal("fixed", aggregate.State.RevokeReason);
        Assert.Single(provider.Revoked);
    }

    [Fact]
    public async Task Service_revoke_unknown_id_throws_not_found()
    {
        var (svc, _, _, _) = NewService();
        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.RevokeAsync("does_not_exist", "enc::admin", "x", default));
        Assert.Equal("not_found", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_revoke_after_redeem_throws_already_redeemed()
    {
        var (svc, store, _, _) = NewService();
        var issued = await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);
        // Drive a redemption via the service.
        await svc.RedeemAsync(issued.AssignmentId, "enc::p", "sub_a", default);

        var ex = await Assert.ThrowsAsync<DiscountAssignmentException>(() =>
            svc.RevokeAsync(issued.AssignmentId, "enc::admin-2", "fixed", default));
        Assert.Equal("already_redeemed", ex.ReasonCode);
    }

    [Fact]
    public async Task Service_redeem_is_idempotent_on_terminal_state()
    {
        var (svc, store, _, _) = NewService();
        var issued = await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);

        // First redeem succeeds.
        await svc.RedeemAsync(issued.AssignmentId, "enc::p", "sub_a", default);
        // Second redeem is a no-op (does NOT throw).
        await svc.RedeemAsync(issued.AssignmentId, "enc::p2", "sub_b", default);

        var aggregate = await store.LoadAsync(issued.AssignmentId, default);
        // First redemption sticks; second is dropped.
        Assert.Equal("enc::p", aggregate.State.RedeemedByParentSubjectIdEncrypted);
        Assert.Equal("sub_a", aggregate.State.Redemption.StripeSubscriptionId);
    }

    [Fact]
    public async Task Service_redeem_silently_drops_when_unknown_id()
    {
        var (svc, _, _, _) = NewService();
        // Should not throw — webhook redeliveries with stale ids must
        // not crash the webhook handler.
        await svc.RedeemAsync("unknown_assignment", "enc::p", "sub_a", default);
    }

    [Fact]
    public async Task Service_find_active_for_email_normalizes_input()
    {
        var (svc, _, _, _) = NewService();
        await svc.IssueAsync("Alice@Gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);

        var hit = await svc.FindActiveForEmailAsync("ALICE+study@gmail.com", default);
        Assert.NotNull(hit);
        Assert.Equal("alice@gmail.com", hit!.TargetEmailNormalized);
    }

    [Fact]
    public async Task Service_list_by_email_normalizes_input()
    {
        var (svc, _, _, _) = NewService();
        await svc.IssueAsync("Alice@Gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);
        var list = await svc.ListByEmailAsync("a.lice@GMAIL.COM", default);
        Assert.Single(list);
    }

    [Fact]
    public async Task Service_list_recent_returns_across_emails()
    {
        var (svc, _, _, _) = NewService();
        await svc.IssueAsync("alice@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);
        await svc.IssueAsync("bob@gmail.com", DiscountKind.PercentOff,
            5_000, 3, "enc::admin", "x", default);
        var list = await svc.ListRecentAsync(10, default);
        Assert.Equal(2, list.Count);
    }

    // ---- End-to-end (admin-issue → student-lookup → checkout → redeem) ---

    [Fact]
    public async Task EndToEnd_admin_issues_then_student_lookup_then_checkout_then_redeem()
    {
        var (svc, store, _, _) = NewService();

        // 1. Admin issues a discount for alice@gmail.com (50% off, 3 months).
        var issued = await svc.IssueAsync(
            rawTargetEmail: "alice@gmail.com",
            kind: DiscountKind.PercentOff,
            value: 5_000,
            durationMonths: 3,
            issuedByAdminSubjectIdEncrypted: "enc::admin",
            reason: "loyalty",
            ct: default);
        Assert.NotEmpty(issued.AssignmentId);

        // 2. Student-side lookup by their email (with Gmail-alias variant)
        //    finds the discount.
        var hit = await svc.FindActiveForEmailAsync("Alice+Study@gmail.com", default);
        Assert.NotNull(hit);
        Assert.Equal(issued.AssignmentId, hit!.AssignmentId);
        Assert.NotEmpty(hit.StripePromotionCodeId);

        // 3. Checkout would be created with the discount's PromotionCodeId
        //    in CheckoutSessionRequest. The student endpoint composes that
        //    request; here we simulate the post-conversion webhook fires
        //    Redeem with the parent + Stripe subscription id.
        await svc.RedeemAsync(
            assignmentId: issued.AssignmentId,
            parentSubjectIdEncrypted: "enc::parent",
            stripeSubscriptionId: "sub_e2e",
            ct: default);

        // 4. Aggregate state is now Redeemed; future student lookups return null.
        var aggregate = await store.LoadAsync(issued.AssignmentId, default);
        Assert.Equal(DiscountStatus.Redeemed, aggregate.State.Status);
        Assert.Equal("enc::parent", aggregate.State.RedeemedByParentSubjectIdEncrypted);
        Assert.Equal("sub_e2e", aggregate.State.Redemption.StripeSubscriptionId);

        var afterRedeem = await svc.FindActiveForEmailAsync("alice@gmail.com", default);
        Assert.Null(afterRedeem);
    }

    // ---- Helpers ----------------------------------------------------------

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = NowUtc;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CapturingDispatcher : IDiscountIssuedEmailDispatcher
    {
        public bool LastCalled;
        public string LastEmail = "";
        public DiscountKind LastKind;
        public int LastValue;
        public int LastDuration;
        public string LastPromoCode = "";

        public Task<bool> SendDiscountIssuedAsync(
            string targetEmailNormalized, DiscountKind kind, int value,
            int durationMonths, string promotionCodeString, CancellationToken ct)
        {
            LastCalled = true;
            LastEmail = targetEmailNormalized;
            LastKind = kind;
            LastValue = value;
            LastDuration = durationMonths;
            LastPromoCode = promotionCodeString;
            return Task.FromResult(true);
        }
    }

    private static (
        DiscountAssignmentService svc,
        InMemoryDiscountAssignmentStore store,
        InMemoryDiscountCouponProvider provider,
        CapturingDispatcher dispatcher) NewService()
    {
        var store = new InMemoryDiscountAssignmentStore();
        var provider = new InMemoryDiscountCouponProvider();
        var dispatcher = new CapturingDispatcher();
        var svc = new DiscountAssignmentService(store, provider, dispatcher, new FakeClock());
        return (svc, store, provider, dispatcher);
    }

    private static DiscountIssued_V1 NewIssuedEvent(
        string id, string emailNormalized, DateTimeOffset? issuedAt = null) => new(
            AssignmentId: id,
            TargetEmailNormalized: emailNormalized,
            DiscountKind: DiscountKind.PercentOff,
            DiscountValue: 5_000,
            DurationMonths: 3,
            IssuedByAdminSubjectIdEncrypted: "enc::admin",
            Reason: "loyalty",
            StripeCouponId: "cou_" + id,
            StripePromotionCodeId: "promo_" + id,
            IssuedAt: issuedAt ?? NowUtc);
}
