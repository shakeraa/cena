// =============================================================================
// Cena Platform — SupportEscalationPolicy tests (EPIC-PRR-J PRR-391/392/393)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class SupportEscalationPolicyTests
{
    private static DiagnosticDisputeView Prior(DateTimeOffset at, DisputeReason reason = DisputeReason.WrongNarration) =>
        new(
            DisputeId: Guid.NewGuid().ToString("D"),
            DiagnosticId: "d",
            StudentSubjectIdHash: "h",
            Reason: reason,
            StudentComment: null,
            Status: DisputeStatus.New,
            SubmittedAt: at,
            ReviewedAt: null,
            ReviewerNote: null);

    [Fact]
    public void EmptyHistoryDoesNotEscalateStandardUser()
    {
        var p = new SupportEscalationPolicy();
        var d = p.Decide(Array.Empty<DiagnosticDisputeView>(),
            DisputeReason.WrongNarration, SubscriptionTier.Plus, DateTimeOffset.UtcNow);
        Assert.False(d.Escalate);
        Assert.Equal("standard_queue", d.Reason);
    }

    [Fact]
    public void TwoPriorDisputesInWindowEscalate()
    {
        var p = new SupportEscalationPolicy();
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var priors = new[]
        {
            Prior(now.AddDays(-1)),
            Prior(now.AddDays(-3)),
        };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Plus, now);
        Assert.True(d.Escalate);
        Assert.Equal("persistent_disputes_in_window", d.Reason);
    }

    [Fact]
    public void PriorsOutsideTheWindowDoNotCount()
    {
        var p = new SupportEscalationPolicy();
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var priors = new[]
        {
            Prior(now.AddDays(-10)),
            Prior(now.AddDays(-20)),
        };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Plus, now);
        Assert.False(d.Escalate);
    }

    [Fact]
    public void PendingOtherCategoryNeverEscalates()
    {
        var p = new SupportEscalationPolicy();
        var now = DateTimeOffset.UtcNow;
        // Many recent priors but pending is "Other" — skip entirely.
        var priors = new[] { Prior(now.AddDays(-1)), Prior(now.AddDays(-2)), Prior(now.AddDays(-3)) };
        var d = p.Decide(priors, DisputeReason.Other, SubscriptionTier.Plus, now);
        Assert.False(d.Escalate);
        Assert.Equal("pending_is_other", d.Reason);
    }

    [Fact]
    public void PriorOtherCategoriesAreFilteredOut()
    {
        var p = new SupportEscalationPolicy();
        var now = DateTimeOffset.UtcNow;
        // 2 priors in window but both Other — doesn't satisfy threshold.
        var priors = new[]
        {
            Prior(now.AddDays(-1), DisputeReason.Other),
            Prior(now.AddDays(-2), DisputeReason.Other),
        };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Plus, now);
        Assert.False(d.Escalate);
    }

    [Fact]
    public void PremiumTierWithPriorThisMonthEscalates()
    {
        var p = new SupportEscalationPolicy();
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var priors = new[] { Prior(new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero)) };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Premium, now);
        Assert.True(d.Escalate);
        Assert.Equal("premium_tier_priority_sla", d.Reason);
    }

    [Fact]
    public void PremiumTierWithPriorLastMonthDoesNotEscalate()
    {
        var p = new SupportEscalationPolicy();
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var priors = new[] { Prior(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero)) };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Premium, now);
        Assert.False(d.Escalate);
    }

    [Fact]
    public void PlusTierWithOnePriorDoesNotEscalate()
    {
        // Only Premium has the 1-prior-this-month SLA; Plus falls under the 2-in-7-day rule.
        var p = new SupportEscalationPolicy();
        var now = DateTimeOffset.UtcNow;
        var priors = new[] { Prior(now.AddDays(-1)) };
        var d = p.Decide(priors, DisputeReason.WrongNarration, SubscriptionTier.Plus, now);
        Assert.False(d.Escalate);
    }

    [Fact]
    public void NullHistoryThrows()
    {
        var p = new SupportEscalationPolicy();
        Assert.Throws<ArgumentNullException>(() =>
            p.Decide(null!, DisputeReason.WrongNarration, SubscriptionTier.Plus, DateTimeOffset.UtcNow));
    }
}
