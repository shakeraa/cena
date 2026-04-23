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

    // PRR-401 — soft-cap emitter is called exactly once across repeat checks
    // in the same (student, cap, month). We use a counting fake emitter +
    // the in-memory ledger so the integration-style invariant is locked
    // at the gate, not just at the emitter unit.
    [Fact]
    public async Task SoftCapReached_invokes_emitter_exactly_once_across_repeat_checks()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var emitter = new CountingSoftCapEmitter();
        var gate = new PhotoDiagnosticQuotaGate(
            entitlements: new FakeEntitlementResolver(SubscriptionTier.Premium),
            usage: usage,
            enforcer: new PerTierCapEnforcer(),
            credits: null,
            hardCapAdjuster: null,
            softCapEmitter: emitter);
        var now = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        // Get the counter to 100 so the gate returns SoftCapReached on the
        // 101st check.
        for (var i = 0; i < 100; i++)
        {
            await gate.CommitAsync("s-101", now, default);
        }

        for (var i = 0; i < 5; i++)
        {
            var d = await gate.CheckAsync("s-101", now.AddMinutes(i), default);
            Assert.Equal(CapDecision.SoftCapReached, d.Decision);
        }

        // Five CheckAsync calls at SoftCapReached, but emitter invoked once.
        Assert.Equal(5, emitter.CallCount);
        Assert.Equal(1, emitter.EmittedCount);
        Assert.Equal(
            Cena.Actors.Subscriptions.Events.EntitlementSoftCapReached_V1.CapTypes.PhotoDiagnosticMonthly,
            emitter.LastCapType);
    }

    [Fact]
    public async Task Allow_does_not_invoke_soft_cap_emitter()
    {
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var emitter = new CountingSoftCapEmitter();
        var gate = new PhotoDiagnosticQuotaGate(
            entitlements: new FakeEntitlementResolver(SubscriptionTier.Premium),
            usage: usage,
            enforcer: new PerTierCapEnforcer(),
            credits: null,
            hardCapAdjuster: null,
            softCapEmitter: emitter);

        for (var i = 0; i < 50; i++)
        {
            var d = await gate.CheckAsync("s-102", DateTimeOffset.UtcNow, default);
            Assert.Equal(CapDecision.Allow, d.Decision);
        }

        Assert.Equal(0, emitter.CallCount);
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

    // Inline counting emitter wraps the real idempotency mechanism
    // (InMemorySoftCapEmissionLedger) to give us both "how many times did
    // the gate call me" and "how many times did the invariant actually
    // let me emit". Both numbers matter to the PRR-401 acceptance criteria.
    private sealed class CountingSoftCapEmitter : ISoftCapEventEmitter
    {
        private readonly InMemorySoftCapEmissionLedger _ledger = new();
        public int CallCount { get; private set; }
        public int EmittedCount { get; private set; }
        public string? LastCapType { get; private set; }

        public async Task EmitIfFirstInPeriodAsync(
            string studentSubjectIdHash,
            string parentSubjectIdEncrypted,
            string capType,
            int usageCount,
            int capLimit,
            DateTimeOffset nowUtc,
            CancellationToken ct)
        {
            CallCount++;
            LastCapType = capType;
            var month = MonthlyUsageKey.For(nowUtc);
            if (await _ledger.TryClaimAsync(studentSubjectIdHash, capType, month, nowUtc, ct))
            {
                EmittedCount++;
            }
        }
    }
}
