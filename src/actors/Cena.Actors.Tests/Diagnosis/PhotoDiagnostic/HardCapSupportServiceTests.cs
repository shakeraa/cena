// =============================================================================
// Cena Platform — HardCapSupportService tests (PRR-402)
//
// Covers the orchestration contract:
//   - Open ticket for Premium at hard cap: succeeds, stamps defaults
//   - Open fails for non-Premium tiers (ArgumentException)
//   - Open fails when reason > 500 chars
//   - Open rejects a second open ticket in the same month window
//   - ResolveWithExtensionAsync happy path: Resolved + ResolvedAtUtc + grant
//   - ResolveWithExtensionAsync out-of-range extensionCount throws
//   - ResolveWithExtensionAsync on a non-Open ticket throws (idempotency)
//   - RejectAsync flips the ticket to Rejected with zero grant
//   - ListActiveGrantsForStudentAsync returns sum across Resolved tickets
//   - Quota-gate integration: +50 grant lets a student at 320 raw uploads
//     stay inside the (300+50) effective hard cap
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class HardCapSupportServiceTests
{
    private sealed class Harness
    {
        public InMemoryHardCapSupportTicketRepository Repo { get; }
        public HardCapSupportService Service { get; }
        public FakeTimeProvider Clock { get; }
        public PhotoDiagnosticMetrics Metrics { get; }

        public Harness()
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));
            Repo = new InMemoryHardCapSupportTicketRepository();
            Metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
            Service = new HardCapSupportService(
                Repo, Metrics, Clock,
                NullLogger<HardCapSupportService>.Instance);
        }
    }

    [Fact]
    public async Task OpenTicket_for_Premium_at_hard_cap_succeeds()
    {
        var h = new Harness();
        var now = h.Clock.GetUtcNow();

        var ticket = await h.Service.OpenTicketAsync(
            studentSubjectIdHash: "stu-1",
            parentSubjectIdEncrypted: "parent-enc-1",
            tier: SubscriptionTier.Premium,
            uploadCount: 300,
            monthWindow: MonthlyUsageKey.For(now),
            reason: "Exam week, one extra upload needed",
            ct: default);

        Assert.False(string.IsNullOrWhiteSpace(ticket.Id));
        Assert.Equal("stu-1", ticket.StudentSubjectIdHash);
        Assert.Equal(SubscriptionTier.Premium, ticket.Tier);
        Assert.Equal(300, ticket.UploadCountAtRequest);
        Assert.Equal("2026-04", ticket.MonthlyWindow);
        Assert.Equal(HardCapSupportTicketStatus.Open, ticket.Status);
        Assert.Equal(0, ticket.GrantedExtensionCount);
        Assert.Empty(ticket.GrantedBy);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.Equal(now, ticket.RequestedAtUtc);
        Assert.Equal("Exam week, one extra upload needed", ticket.Reason);

        var persisted = await h.Repo.GetAsync(ticket.Id, default);
        Assert.NotNull(persisted);
        Assert.Equal(ticket.Id, persisted!.Id);
    }

    [Theory]
    [InlineData(SubscriptionTier.Unsubscribed)]
    [InlineData(SubscriptionTier.Basic)]
    [InlineData(SubscriptionTier.Plus)]
    [InlineData(SubscriptionTier.SchoolSku)]
    public async Task OpenTicket_fails_for_non_Premium_tier(SubscriptionTier tier)
    {
        var h = new Harness();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Service.OpenTicketAsync(
                "stu-1", "parent-enc", tier, uploadCount: 20,
                monthWindow: "2026-04", reason: null, ct: default));
    }

    [Fact]
    public async Task OpenTicket_fails_when_reason_exceeds_500_chars()
    {
        var h = new Harness();
        var huge = new string('x', HardCapSupportService.MaxReasonLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            h.Service.OpenTicketAsync(
                "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
                "2026-04", huge, ct: default));
    }

    [Fact]
    public async Task OpenTicket_rejects_duplicate_open_ticket_in_same_month()
    {
        // Second submission inside the same month window is almost always a
        // student retry; the first ticket still has all the context support
        // needs.
        var h = new Harness();
        await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", "first", default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.OpenTicketAsync(
                "stu-1", "parent-enc", SubscriptionTier.Premium, 301,
                "2026-04", "second", default));

        // Different month is fine.
        var nextMonth = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 5,
            "2026-05", null, default);
        Assert.Equal("2026-05", nextMonth.MonthlyWindow);
    }

    [Fact]
    public async Task ResolveWithExtension_happy_path_writes_Resolved_and_stamps_timestamp()
    {
        var h = new Harness();
        var ticket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", null, default);
        h.Clock.Advance(TimeSpan.FromHours(2));

        var resolved = await h.Service.ResolveWithExtensionAsync(
            ticket.Id, adminSubjectId: "admin-7", extensionCount: 50, default);

        Assert.Equal(HardCapSupportTicketStatus.Resolved, resolved.Status);
        Assert.Equal(50, resolved.GrantedExtensionCount);
        Assert.Equal("admin-7", resolved.GrantedBy);
        Assert.Equal(h.Clock.GetUtcNow(), resolved.ResolvedAtUtc);

        // Persisted state matches the returned projection.
        var stored = await h.Repo.GetAsync(ticket.Id, default);
        Assert.NotNull(stored);
        Assert.Equal(HardCapSupportTicketStatus.Resolved, stored!.Status);
        Assert.Equal(50, stored.GrantedExtensionCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(1000)]
    public async Task ResolveWithExtension_rejects_out_of_range_count(int bad)
    {
        var h = new Harness();
        var ticket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", null, default);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            h.Service.ResolveWithExtensionAsync(ticket.Id, "admin-1", bad, default));
    }

    [Fact]
    public async Task ResolveWithExtension_fails_when_ticket_is_not_Open()
    {
        // Idempotency guard: once a ticket leaves Open it is settled; a
        // second admin click must not mint a second grant.
        var h = new Harness();
        var ticket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", null, default);
        await h.Service.ResolveWithExtensionAsync(ticket.Id, "admin-1", 25, default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ResolveWithExtensionAsync(ticket.Id, "admin-2", 50, default));
        Assert.Contains("not Open", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Ledger still shows one grant only.
        var total = await h.Repo.ListActiveGrantsForStudentAsync("stu-1", "2026-04", default);
        Assert.Equal(25, total);
    }

    [Fact]
    public async Task ResolveWithExtension_fails_when_ticket_not_found()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Service.ResolveWithExtensionAsync("no-such-ticket", "admin-1", 10, default));
    }

    [Fact]
    public async Task RejectAsync_flips_ticket_to_Rejected_with_zero_grant()
    {
        var h = new Harness();
        var ticket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", null, default);
        h.Clock.Advance(TimeSpan.FromHours(1));

        var rejected = await h.Service.RejectAsync(ticket.Id, "admin-9", default);

        Assert.Equal(HardCapSupportTicketStatus.Rejected, rejected.Status);
        Assert.Equal(0, rejected.GrantedExtensionCount);
        Assert.Equal("admin-9", rejected.GrantedBy);
        Assert.Equal(h.Clock.GetUtcNow(), rejected.ResolvedAtUtc);

        // Rejection does NOT contribute to the active-grant sum.
        var total = await h.Repo.ListActiveGrantsForStudentAsync("stu-1", "2026-04", default);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task ListActiveGrantsForStudent_sums_across_resolved_tickets()
    {
        // Per-service policy one Open ticket per (student, month), but if
        // support resolves and a later month produces another grant, the
        // repo-level sum is what the quota gate consumes. Use two distinct
        // months to avoid the OpenTicket duplicate guard.
        var h = new Harness();
        var aprilTicket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 300,
            "2026-04", null, default);
        await h.Service.ResolveWithExtensionAsync(aprilTicket.Id, "admin-1", 40, default);

        var mayTicket = await h.Service.OpenTicketAsync(
            "stu-1", "parent-enc", SubscriptionTier.Premium, 301,
            "2026-05", null, default);
        await h.Service.ResolveWithExtensionAsync(mayTicket.Id, "admin-1", 25, default);

        Assert.Equal(40, await h.Repo.ListActiveGrantsForStudentAsync("stu-1", "2026-04", default));
        Assert.Equal(25, await h.Repo.ListActiveGrantsForStudentAsync("stu-1", "2026-05", default));
        Assert.Equal(0,  await h.Repo.ListActiveGrantsForStudentAsync("stu-1", "2026-06", default));
    }

    [Fact]
    public async Task QuotaGate_with_active_grant_keeps_student_inside_bumped_hard_cap()
    {
        // Premium student has burned 320 real uploads in April — past the
        // 300 hard cap. Support grants +50. The effective hard cap for
        // that month is (300 + 50) = 350, so the student at 320 is now
        // soft-cap territory (above soft=100) but still below the bumped
        // hard cap → decision is SoftCapReached, not HardCapReached.
        //
        // Importantly, the raw IPhotoDiagnosticMonthlyUsage counter still
        // reads 320 — we did NOT decrement it. That keeps abuse detection
        // and analytics honest. See the ledger-not-decrement banner on
        // HardCapSupportService.
        var h = new Harness();
        var now = h.Clock.GetUtcNow();
        var monthWindow = MonthlyUsageKey.For(now);

        var ticket = await h.Service.OpenTicketAsync(
            "stu-premium", "parent-enc", SubscriptionTier.Premium, 300,
            monthWindow, null, default);
        await h.Service.ResolveWithExtensionAsync(ticket.Id, "admin-1", 50, default);

        // Burn 320 uploads for this student in the same month.
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        for (var i = 0; i < 320; i++)
            await usage.IncrementAsync("stu-premium", now, default);

        var entitlements = new FakeEntitlementResolver(SubscriptionTier.Premium);
        var enforcer = new PerTierCapEnforcer();
        var hardCapAdjuster = new HardCapExtensionAdjuster(h.Repo);
        var gate = new PhotoDiagnosticQuotaGate(
            entitlements, usage, enforcer, credits: null, hardCapAdjuster: hardCapAdjuster);

        var decision = await gate.CheckAsync("stu-premium", now, default);

        Assert.Equal(320, decision.CurrentUsage);
        Assert.Equal(350, decision.HardCap);       // 300 base + 50 grant
        Assert.Equal(CapDecision.SoftCapReached, decision.Decision);
    }

    // ---- Test doubles ------------------------------------------------------

    private sealed class FakeEntitlementResolver : IStudentEntitlementResolver
    {
        private readonly SubscriptionTier _tier;
        public FakeEntitlementResolver(SubscriptionTier tier) => _tier = tier;
        public Task<StudentEntitlementView> ResolveAsync(
            string studentSubjectIdEncrypted, CancellationToken ct) =>
            Task.FromResult(new StudentEntitlementView(
                StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
                EffectiveTier: _tier,
                SourceParentSubjectIdEncrypted: "parent",
                ValidUntil: null,
                LastUpdatedAt: DateTimeOffset.UtcNow));
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
