// =============================================================================
// Cena Platform — PerTierCapEnforcer tests (EPIC-PRR-I PRR-312, EPIC-PRR-J PRR-400)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class PerTierCapEnforcerTests
{
    private readonly PerTierCapEnforcer _sut = new();

    [Fact]
    public void Basic_student_zero_photo_diagnostic_hard_blocks()
    {
        var ent = Entitlement(SubscriptionTier.Basic);
        var result = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 0);
        Assert.Equal(CapDecision.HardCapReached, result.Decision);
    }

    [Fact]
    public void Plus_student_within_20_photo_diagnostics_allows()
    {
        var ent = Entitlement(SubscriptionTier.Plus);
        var result = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 15);
        Assert.Equal(CapDecision.Allow, result.Decision);
    }

    [Fact]
    public void Plus_student_at_20_photo_diagnostics_is_hard_cap()
    {
        var ent = Entitlement(SubscriptionTier.Plus);
        // Plus has soft==hard at 20
        var result = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 20);
        Assert.Equal(CapDecision.HardCapReached, result.Decision);
    }

    [Fact]
    public void Premium_student_at_100_photo_diagnostics_is_soft_cap()
    {
        var ent = Entitlement(SubscriptionTier.Premium);
        var result = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 100);
        Assert.Equal(CapDecision.SoftCapReached, result.Decision);
        Assert.Equal(100, result.SoftCap);
        Assert.Equal(300, result.HardCap);
    }

    [Fact]
    public void Premium_student_at_300_photo_diagnostics_is_hard_cap()
    {
        var ent = Entitlement(SubscriptionTier.Premium);
        var result = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 300);
        Assert.Equal(CapDecision.HardCapReached, result.Decision);
    }

    [Fact]
    public void Basic_sonnet_escalations_within_20_per_week_allow()
    {
        var ent = Entitlement(SubscriptionTier.Basic);
        var result = _sut.Check(ent, CapCounter.SonnetEscalationPerWeek, 15);
        Assert.Equal(CapDecision.Allow, result.Decision);
    }

    [Fact]
    public void Basic_sonnet_escalations_at_20_per_week_hard_cap()
    {
        var ent = Entitlement(SubscriptionTier.Basic);
        var result = _sut.Check(ent, CapCounter.SonnetEscalationPerWeek, 20);
        Assert.Equal(CapDecision.HardCapReached, result.Decision);
    }

    [Fact]
    public void Plus_unlimited_sonnet_always_allow()
    {
        var ent = Entitlement(SubscriptionTier.Plus);
        var result = _sut.Check(ent, CapCounter.SonnetEscalationPerWeek, 10_000);
        Assert.Equal(CapDecision.Allow, result.Decision);
    }

    [Fact]
    public void Unsubscribed_zero_cap_is_always_hard_cap()
    {
        var ent = Entitlement(SubscriptionTier.Unsubscribed);
        var photo = _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, 0);
        var sonnet = _sut.Check(ent, CapCounter.SonnetEscalationPerWeek, 0);
        var hint = _sut.Check(ent, CapCounter.HintRequestPerMonth, 0);
        Assert.Equal(CapDecision.HardCapReached, photo.Decision);
        Assert.Equal(CapDecision.HardCapReached, sonnet.Decision);
        Assert.Equal(CapDecision.HardCapReached, hint.Decision);
    }

    [Fact]
    public void Negative_usage_throws()
    {
        var ent = Entitlement(SubscriptionTier.Premium);
        Assert.Throws<ArgumentException>(() =>
            _sut.Check(ent, CapCounter.PhotoDiagnosticPerMonth, -1));
    }

    private static StudentEntitlementView Entitlement(SubscriptionTier tier) => new(
        StudentSubjectIdEncrypted: "enc::student-x",
        EffectiveTier: tier,
        SourceParentSubjectIdEncrypted: "enc::parent",
        ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
        LastUpdatedAt: DateTimeOffset.UtcNow);
}
