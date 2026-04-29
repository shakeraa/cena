// =============================================================================
// Cena Platform — VariantRateLimitPolicy tests (PRR-265, ADR-0059 §15.5 R1)
//
// Pinning tests for the per-tier × per-kind × payment-verified matrix from
// ADR-0059 §15.5. Editing any value here MUST land via a PR reviewed by
// the §15.5 decision-holder. The architecture test
// (VariantRateLimitArchitectureTest) further pins the constants to the
// design memo so a careless edit fails CI.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Variants;
using Cena.Infrastructure.RateLimiting;

namespace Cena.Actors.Tests.Variants;

public sealed class VariantRateLimitPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly VariantRateLimitPolicy _policy = new();

    // -----------------------------------------------------------------
    // Free + payment-verified (the §15.5 row 1 default for free students
    // who completed payment-method or institute-SSO verification)
    // -----------------------------------------------------------------
    [Fact]
    public async Task Unsubscribed_Structural_PaymentVerified_HasFreeRowCaps()
    {
        var caps = await _policy.ResolveAsync(
            SubscriptionTier.Unsubscribed, VariantKind.Structural,
            paymentVerified: true, instituteId: null, Now, default);

        Assert.Equal(VariantRateLimitPolicy.FreeWithPaymentStructuralPerDay, caps.PerDayLimit);
        Assert.Equal(VariantRateLimitPolicy.StructuralPerSource, caps.PerSourceLimit);
        Assert.False(caps.RequiresPaymentVerification);
    }

    [Fact]
    public async Task Unsubscribed_Structural_NoPayment_IsZeroAndRequiresPayment()
    {
        var caps = await _policy.ResolveAsync(
            SubscriptionTier.Unsubscribed, VariantKind.Structural,
            paymentVerified: false, instituteId: null, Now, default);

        Assert.Equal(0, caps.PerDayLimit);
        Assert.True(caps.RequiresPaymentVerification);
    }

    [Fact]
    public async Task Unsubscribed_Parametric_NotAvailable_RegardlessOfPayment()
    {
        var withPayment = await _policy.ResolveAsync(
            SubscriptionTier.Unsubscribed, VariantKind.Parametric,
            paymentVerified: true, null, Now, default);
        var withoutPayment = await _policy.ResolveAsync(
            SubscriptionTier.Unsubscribed, VariantKind.Parametric,
            paymentVerified: false, null, Now, default);

        // Free tier parametric is forbidden outright per Q-A
        // (Ministry §16 derivative-works distance).
        Assert.Equal(0, withPayment.PerDayLimit);
        Assert.Equal(0, withoutPayment.PerDayLimit);
        Assert.True(withPayment.RequiresPaymentVerification);
    }

    // -----------------------------------------------------------------
    // Paid tiers — uniform structural / parametric caps regardless of
    // tier (per ADR-0059 §15.5 row 2). Trial inherits the paid caps.
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(SubscriptionTier.Basic)]
    [InlineData(SubscriptionTier.Plus)]
    [InlineData(SubscriptionTier.Premium)]
    [InlineData(SubscriptionTier.SchoolSku)]
    [InlineData(SubscriptionTier.TrialPlus)]
    public async Task Paid_Structural_HasPaidRowCaps(SubscriptionTier tier)
    {
        var caps = await _policy.ResolveAsync(
            tier, VariantKind.Structural,
            paymentVerified: false,    // payment is irrelevant for paid tiers
            instituteId: null, Now, default);

        Assert.Equal(VariantRateLimitPolicy.PaidStructuralPerDay, caps.PerDayLimit);
        Assert.Equal(VariantRateLimitPolicy.StructuralPerSource, caps.PerSourceLimit);
        Assert.False(caps.RequiresPaymentVerification);
    }

    [Theory]
    [InlineData(SubscriptionTier.Basic)]
    [InlineData(SubscriptionTier.Plus)]
    [InlineData(SubscriptionTier.Premium)]
    [InlineData(SubscriptionTier.SchoolSku)]
    [InlineData(SubscriptionTier.TrialPlus)]
    public async Task Paid_Parametric_HasPaidRowCaps(SubscriptionTier tier)
    {
        var caps = await _policy.ResolveAsync(
            tier, VariantKind.Parametric,
            paymentVerified: false,
            instituteId: null, Now, default);

        Assert.Equal(VariantRateLimitPolicy.PaidParametricPerDay, caps.PerDayLimit);
        Assert.Equal(VariantRateLimitPolicy.ParametricPerSource, caps.PerSourceLimit);
    }

    // -----------------------------------------------------------------
    // Per-institute caps — finops cost ceiling enforcement.
    // -----------------------------------------------------------------
    [Fact]
    public async Task Paid_Institute_StructuralCeiling_Applies()
    {
        var caps = await _policy.ResolveAsync(
            SubscriptionTier.SchoolSku, VariantKind.Structural,
            paymentVerified: false, "institute-X", Now, default);

        Assert.Equal(VariantRateLimitPolicy.InstitutePaidStructuralPerDay,
            caps.InstitutePerDayLimit);
        Assert.Equal(VariantRateLimitPolicy.InstitutePerSourcePerDay,
            caps.InstitutePerSourceLimit);
    }

    [Fact]
    public async Task Free_Institute_StructuralCeilingIsLowerThanPaid()
    {
        var caps = await _policy.ResolveAsync(
            SubscriptionTier.Unsubscribed, VariantKind.Structural,
            paymentVerified: true, "institute-Y", Now, default);

        Assert.Equal(VariantRateLimitPolicy.InstituteFreeStructuralPerDay,
            caps.InstitutePerDayLimit);
        Assert.True(VariantRateLimitPolicy.InstituteFreeStructuralPerDay
                    < VariantRateLimitPolicy.InstitutePaidStructuralPerDay,
            "Free-institute ceiling must be lower than paid (finops invariant).");
    }

    // -----------------------------------------------------------------
    // Window durations — pinned to ADR-0059 §14.3.1 cogsci spacing.
    // -----------------------------------------------------------------
    [Fact]
    public void PerDayWindow_Is24Hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), _policy.PerDayWindow);
    }

    [Fact]
    public void PerSourceWindow_Is30Days()
    {
        Assert.Equal(TimeSpan.FromDays(30), _policy.PerSourceWindow);
    }

    // -----------------------------------------------------------------
    // ResolveSync parity — the static path must agree with the
    // async resolver for every tier.
    // -----------------------------------------------------------------
    [Theory]
    [InlineData(SubscriptionTier.Unsubscribed, VariantKind.Structural, true)]
    [InlineData(SubscriptionTier.Unsubscribed, VariantKind.Structural, false)]
    [InlineData(SubscriptionTier.Unsubscribed, VariantKind.Parametric, true)]
    [InlineData(SubscriptionTier.Basic, VariantKind.Structural, false)]
    [InlineData(SubscriptionTier.Premium, VariantKind.Parametric, false)]
    [InlineData(SubscriptionTier.SchoolSku, VariantKind.Structural, false)]
    [InlineData(SubscriptionTier.TrialPlus, VariantKind.Parametric, false)]
    public async Task ResolveSync_Matches_ResolveAsync(
        SubscriptionTier tier, VariantKind kind, bool paymentVerified)
    {
        var sync = VariantRateLimitPolicy.ResolveSync(tier, kind, paymentVerified);
        var async_ = await _policy.ResolveAsync(
            tier, kind, paymentVerified, "institute-Z", Now, default);

        Assert.Equal(sync, async_);
    }
}
