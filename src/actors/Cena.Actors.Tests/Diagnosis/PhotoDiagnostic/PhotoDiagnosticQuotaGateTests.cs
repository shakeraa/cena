// =============================================================================
// Cena Platform — PhotoDiagnosticQuotaGate tests (EPIC-PRR-J PRR-400/401/402)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class PhotoDiagnosticQuotaGateTests
{
    private static PhotoDiagnosticQuotaGate NewGate(
        SubscriptionTier tier,
        InMemoryPhotoDiagnosticMonthlyUsage usage)
    {
        var entitlements = new FakeEntitlementResolver(tier);
        return new PhotoDiagnosticQuotaGate(entitlements, usage, new PerTierCapEnforcer());
    }

    [Fact]
    public async Task BasicTierIsAlwaysHardCapReached()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Basic, usage);
        var decision = await gate.CheckAsync("s1", DateTimeOffset.UtcNow, default);

        Assert.Equal(CapDecision.HardCapReached, decision.Decision);
        Assert.Equal(SubscriptionTier.Basic, decision.Tier);
        Assert.Equal(0, decision.SoftCap);
    }

    [Fact]
    public async Task PlusTierAllowsUntilTwentyThenHardCap()
    {
        // Plus has soft=20, hard=20.
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Plus, usage);
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 20; i++)
        {
            var d = await gate.CheckAsync("s2", now, default);
            Assert.Equal(CapDecision.Allow, d.Decision);
            await gate.CommitAsync("s2", now, default);
        }

        var capped = await gate.CheckAsync("s2", now, default);
        Assert.Equal(CapDecision.HardCapReached, capped.Decision);
    }

    [Fact]
    public async Task PremiumHasSoftCapAtHundredAndHardAtThreeHundred()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Premium, usage);
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 100; i++) await gate.CommitAsync("s3", now, default);

        var atSoft = await gate.CheckAsync("s3", now, default);
        Assert.Equal(CapDecision.SoftCapReached, atSoft.Decision);
        Assert.Equal(100, atSoft.SoftCap);
        Assert.Equal(300, atSoft.HardCap);
    }

    [Fact]
    public async Task UnsubscribedIsHardCapReached()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Unsubscribed, usage);
        var decision = await gate.CheckAsync("s4", DateTimeOffset.UtcNow, default);

        Assert.Equal(CapDecision.HardCapReached, decision.Decision);
    }

    [Fact]
    public async Task CheckDoesNotMoveTheCounter()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Plus, usage);
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 5; i++) await gate.CheckAsync("s5", now, default);

        var decision = await gate.CheckAsync("s5", now, default);
        Assert.Equal(0, decision.CurrentUsage);
    }

    [Fact]
    public async Task DifferentMonthsAreIndependent()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Plus, usage);
        var april = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);
        var may = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 20; i++) await gate.CommitAsync("s6", april, default);

        var aprilDecision = await gate.CheckAsync("s6", april, default);
        Assert.Equal(CapDecision.HardCapReached, aprilDecision.Decision);

        var mayDecision = await gate.CheckAsync("s6", may, default);
        Assert.Equal(CapDecision.Allow, mayDecision.Decision);
        Assert.Equal(0, mayDecision.CurrentUsage);
    }

    [Fact]
    public void MonthlyUsageKeyIsYyyyMm()
    {
        var d = new DateTimeOffset(2026, 4, 15, 23, 59, 59, TimeSpan.FromHours(3));
        Assert.Equal("2026-04", MonthlyUsageKey.For(d));
    }

    [Fact]
    public async Task RejectsEmptyStudent()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var gate = NewGate(SubscriptionTier.Plus, usage);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            gate.CheckAsync("", DateTimeOffset.UtcNow, default));
    }

    private sealed class FakeEntitlementResolver : IStudentEntitlementResolver
    {
        private readonly SubscriptionTier _tier;
        public FakeEntitlementResolver(SubscriptionTier tier) => _tier = tier;

        public Task<StudentEntitlementView> ResolveAsync(string studentSubjectIdEncrypted, CancellationToken ct) =>
            Task.FromResult(new StudentEntitlementView(
                StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
                EffectiveTier: _tier,
                SourceParentSubjectIdEncrypted: "parent",
                ValidUntil: null,
                LastUpdatedAt: DateTimeOffset.UtcNow));
    }
}
