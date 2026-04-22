// =============================================================================
// Cena Platform — TierLlmRoutingPolicy tests (EPIC-PRR-I PRR-311, ADR-0026)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TierLlmRoutingPolicyTests
{
    private readonly TierLlmRoutingPolicy _sut = new();

    [Fact]
    public void Simple_call_always_routes_to_haiku_regardless_of_tier()
    {
        foreach (var tier in new[]
        {
            SubscriptionTier.Basic, SubscriptionTier.Plus,
            SubscriptionTier.Premium, SubscriptionTier.Unsubscribed,
        })
        {
            var decision = _sut.Decide(Entitlement(tier), complexityEstimate: 0.10, weeklySonnetEscalationCount: 0);
            Assert.Equal(LlmModelTier.Haiku, decision.Target);
        }
    }

    [Fact]
    public void Complex_call_basic_within_cap_routes_to_sonnet()
    {
        var decision = _sut.Decide(
            Entitlement(SubscriptionTier.Basic),
            complexityEstimate: 0.80,
            weeklySonnetEscalationCount: 10);
        Assert.Equal(LlmModelTier.Sonnet, decision.Target);
    }

    [Fact]
    public void Complex_call_basic_over_cap_degrades_to_haiku()
    {
        var decision = _sut.Decide(
            Entitlement(SubscriptionTier.Basic),
            complexityEstimate: 0.80,
            weeklySonnetEscalationCount: 20);
        Assert.Equal(LlmModelTier.Haiku, decision.Target);
        Assert.Equal("basic-weekly-sonnet-cap", decision.DegradeReason);
    }

    [Fact]
    public void Complex_call_plus_always_sonnet()
    {
        var decision = _sut.Decide(
            Entitlement(SubscriptionTier.Plus),
            complexityEstimate: 0.80,
            weeklySonnetEscalationCount: 10_000);
        Assert.Equal(LlmModelTier.Sonnet, decision.Target);
        Assert.False(decision.SoftCapReached);
    }

    [Fact]
    public void Complex_call_premium_always_sonnet_no_weekly_cap()
    {
        var decision = _sut.Decide(
            Entitlement(SubscriptionTier.Premium),
            complexityEstimate: 0.80,
            weeklySonnetEscalationCount: 10_000);
        Assert.Equal(LlmModelTier.Sonnet, decision.Target);
    }

    [Fact]
    public void Complex_call_unsubscribed_degrades_to_haiku()
    {
        var decision = _sut.Decide(
            Entitlement(SubscriptionTier.Unsubscribed),
            complexityEstimate: 0.80,
            weeklySonnetEscalationCount: 0);
        Assert.Equal(LlmModelTier.Haiku, decision.Target);
        Assert.Equal("no-subscription", decision.DegradeReason);
    }

    [Fact]
    public void Negative_usage_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _sut.Decide(Entitlement(SubscriptionTier.Basic), 0.5, -1));
    }

    private static StudentEntitlementView Entitlement(SubscriptionTier tier) => new(
        StudentSubjectIdEncrypted: "enc::student",
        EffectiveTier: tier,
        SourceParentSubjectIdEncrypted: "enc::parent",
        ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
        LastUpdatedAt: DateTimeOffset.UtcNow);
}
