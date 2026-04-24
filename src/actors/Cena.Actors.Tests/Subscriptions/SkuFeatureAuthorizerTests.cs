// =============================================================================
// Cena Platform — SkuFeatureAuthorizer tests (EPIC-PRR-I PRR-343)
//
// Locks the feature-fence matrix so a TierCatalog edit that
// accidentally grants parent-only features to SchoolSku can't ship.
// Every retail-vs-b2b fence we care about has at least one test here.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SkuFeatureAuthorizerTests
{
    // ── Parent-state convenience overload ──────────────────────────────────

    [Theory]
    [InlineData(SubscriptionTier.SchoolSku, TierFeature.ParentDashboard, false)]
    [InlineData(SubscriptionTier.SchoolSku, TierFeature.TutorHandoffPdf, false)]
    [InlineData(SubscriptionTier.Premium, TierFeature.ParentDashboard, true)]
    [InlineData(SubscriptionTier.Premium, TierFeature.TutorHandoffPdf, true)]
    [InlineData(SubscriptionTier.Basic, TierFeature.ParentDashboard, false)]
    [InlineData(SubscriptionTier.Plus, TierFeature.ParentDashboard, false)]
    public void CheckParent_matches_TierCatalog_matrix(
        SubscriptionTier tier, TierFeature feature, bool expectedAllowed)
    {
        var state = BuildActiveState(tier);
        var decision = SkuFeatureAuthorizer.CheckParent(state, feature);
        Assert.Equal(expectedAllowed, decision.Allowed);
    }

    [Fact]
    public void CheckParent_school_sku_parent_dashboard_returns_feature_not_in_sku()
    {
        // Specifically lock the stable reason code so the UI can render
        // the localized "upgrade your institution plan" copy.
        var state = BuildActiveState(SubscriptionTier.SchoolSku);
        var decision = SkuFeatureAuthorizer.CheckParent(state, TierFeature.ParentDashboard);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_not_in_sku", decision.ReasonCode);
    }

    [Fact]
    public void CheckParent_inactive_subscription_returns_unsubscribed_code()
    {
        // Default SubscriptionState has Status = Unsubscribed.
        var state = new SubscriptionState();
        var decision = SkuFeatureAuthorizer.CheckParent(state, TierFeature.ParentDashboard);
        Assert.False(decision.Allowed);
        Assert.Equal("unsubscribed", decision.ReasonCode);
    }

    [Fact]
    public void CheckParent_cancelled_subscription_returns_unsubscribed_code()
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            "enc::parent", "enc::student", SubscriptionTier.Premium,
            BillingCycle.Monthly, 24_900L, "txn",
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow.AddDays(25)));
        aggregate.Apply(new SubscriptionCancelled_V1(
            "enc::parent", "test", "parent", DateTimeOffset.UtcNow));

        var decision = SkuFeatureAuthorizer.CheckParent(aggregate.State, TierFeature.ParentDashboard);
        Assert.False(decision.Allowed);
        Assert.Equal("unsubscribed", decision.ReasonCode);
    }

    [Fact]
    public void CheckParent_null_state_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SkuFeatureAuthorizer.CheckParent(null!, TierFeature.ParentDashboard));
    }

    [Fact]
    public void CheckParent_unknown_feature_enum_returns_unknown_feature_code()
    {
        var state = BuildActiveState(SubscriptionTier.Premium);
        // Invalid enum cast — not defined in TierFeature.
        var bogus = (TierFeature)999;
        var decision = SkuFeatureAuthorizer.CheckParent(state, bogus);
        Assert.False(decision.Allowed);
        Assert.Equal("unknown_feature", decision.ReasonCode);
    }

    // ── Entitlement-view overload (per-student lookups) ────────────────────

    [Fact]
    public void Check_entitlement_premium_allows_parent_dashboard()
    {
        var ent = BuildEntitlement(SubscriptionTier.Premium);
        var decision = SkuFeatureAuthorizer.Check(ent, TierFeature.ParentDashboard);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Check_entitlement_school_sku_denies_tutor_handoff()
    {
        var ent = BuildEntitlement(SubscriptionTier.SchoolSku);
        var decision = SkuFeatureAuthorizer.Check(ent, TierFeature.TutorHandoffPdf);
        Assert.False(decision.Allowed);
        Assert.Equal("feature_not_in_sku", decision.ReasonCode);
    }

    [Fact]
    public void Check_entitlement_unsubscribed_denies_with_stable_code()
    {
        var ent = BuildEntitlement(SubscriptionTier.Unsubscribed);
        var decision = SkuFeatureAuthorizer.Check(ent, TierFeature.ParentDashboard);
        Assert.False(decision.Allowed);
        Assert.Equal("unsubscribed", decision.ReasonCode);
    }

    [Fact]
    public void Check_entitlement_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SkuFeatureAuthorizer.Check(null!, TierFeature.ParentDashboard));
    }

    [Fact]
    public void Check_entitlement_school_sku_allows_classroom_dashboard()
    {
        // Inverse: SchoolSku DOES get ClassroomDashboard + TeacherAssigned-
        // Practice + Sso. The fence is directional — we're denying parent-
        // side features on b2b tiers, not all features.
        var ent = BuildEntitlement(SubscriptionTier.SchoolSku);
        Assert.True(SkuFeatureAuthorizer.Check(ent, TierFeature.ClassroomDashboard).Allowed);
        Assert.True(SkuFeatureAuthorizer.Check(ent, TierFeature.TeacherAssignedPractice).Allowed);
        Assert.True(SkuFeatureAuthorizer.Check(ent, TierFeature.Sso).Allowed);
    }

    [Fact]
    public void Check_entitlement_premium_denies_classroom_dashboard()
    {
        // Inverse fence: retail tiers don't get B2B features. Premium
        // parents cannot access ClassroomDashboard (that's institution-
        // only content).
        var ent = BuildEntitlement(SubscriptionTier.Premium);
        Assert.False(SkuFeatureAuthorizer.Check(ent, TierFeature.ClassroomDashboard).Allowed);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SubscriptionState BuildActiveState(SubscriptionTier tier)
    {
        var aggregate = new SubscriptionAggregate();
        aggregate.Apply(new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::parent",
            PrimaryStudentSubjectIdEncrypted: "enc::student",
            Tier: tier,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 7_900L,
            PaymentTransactionIdEncrypted: "txn",
            ActivatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            RenewsAt: DateTimeOffset.UtcNow.AddDays(29)));
        return aggregate.State;
    }

    private static StudentEntitlementView BuildEntitlement(SubscriptionTier tier) => new(
        StudentSubjectIdEncrypted: "enc::student",
        EffectiveTier: tier,
        SourceParentSubjectIdEncrypted: "enc::parent",
        ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
        LastUpdatedAt: DateTimeOffset.UtcNow);
}
