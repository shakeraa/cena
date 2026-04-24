// =============================================================================
// Cena Platform — DiagnosticCreditService tests (EPIC-PRR-J PRR-391)
//
// Pins the orchestration contract:
//   - happy path: ledger written, dispute flipped to Upheld
//   - not-found dispute throws
//   - already-Upheld dispute throws (idempotency)
//   - out-of-range uploadQuotaBumpCount throws
//   - dispatcher invoked exactly once on success
//   - quota gate honours the ledger (credits > usage -> Allow)
//   - ListByStudentAsync returns the student's history
//
// Uses the in-memory ledger + dispute repo + a fake dispatcher + the
// real DiagnosticDisputeService to integrate the full credit flow the
// way production DI wires it.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Actors.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticCreditServiceTests
{
    private sealed class Harness
    {
        public InMemoryDiagnosticDisputeRepository DisputeRepo { get; }
        public InMemoryDiagnosticCreditLedger Ledger { get; }
        public RecordingDispatcher Dispatcher { get; }
        public DiagnosticDisputeService Disputes { get; }
        public DiagnosticCreditService Credits { get; }
        public FakeTimeProvider Clock { get; }

        public Harness()
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));
            DisputeRepo = new InMemoryDiagnosticDisputeRepository();
            Ledger = new InMemoryDiagnosticCreditLedger();
            Dispatcher = new RecordingDispatcher();
            var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
            Disputes = new DiagnosticDisputeService(
                DisputeRepo, metrics,
                NullLogger<DiagnosticDisputeService>.Instance, Clock);
            Credits = new DiagnosticCreditService(
                Disputes, DisputeRepo, Ledger, Dispatcher,
                Clock, NullLogger<DiagnosticCreditService>.Instance);
        }

        public async Task<DiagnosticDisputeView> SeedDisputeAsync(string diagnosticId = "d-seed", string studentHash = "stu-hash")
        {
            return await Disputes.SubmitAsync(new SubmitDiagnosticDisputeCommand(
                DiagnosticId: diagnosticId,
                StudentSubjectIdHash: studentHash,
                Reason: DisputeReason.WrongNarration,
                StudentComment: "original comment"), default);
        }
    }

    [Fact]
    public async Task IssueCreditWritesLedgerAndFlipsDisputeToUpheld()
    {
        var h = new Harness();
        var dispute = await h.SeedDisputeAsync();

        var credit = await h.Credits.IssueCreditAsync(
            dispute.DisputeId, adminSubjectId: "admin-1", uploadQuotaBumpCount: 3,
            reason: "Template misread the denominator", default);

        // Ledger row
        Assert.NotEqual(string.Empty, credit.Id);
        Assert.Equal(dispute.DisputeId, credit.DisputeId);
        Assert.Equal("stu-hash", credit.StudentSubjectIdHash);
        Assert.Equal("admin-1", credit.CreditedBy);
        Assert.Equal(DiagnosticCreditKind.FreeUploadQuota, credit.CreditKind);
        Assert.Equal(3, credit.UploadQuotaBumpCount);
        Assert.Equal("Template misread the denominator", credit.Reason);

        // Persisted
        var stored = await h.Ledger.GetByDisputeAsync(dispute.DisputeId, default);
        Assert.NotNull(stored);
        Assert.Equal(credit.Id, stored!.Id);

        // Dispute flipped
        var after = await h.Disputes.GetAsync(dispute.DisputeId, default);
        Assert.NotNull(after);
        Assert.Equal(DisputeStatus.Upheld, after!.Status);
        Assert.NotNull(after.ReviewedAt);
        Assert.Contains("credit issued: 3 uploads", after.ReviewerNote);
        Assert.Contains("Template misread the denominator", after.ReviewerNote);
    }

    [Fact]
    public async Task IssueCreditThrowsWhenDisputeNotFound()
    {
        var h = new Harness();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Credits.IssueCreditAsync("not-a-real-dispute", "admin-1", 5, "", default));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IssueCreditThrowsWhenDisputeAlreadyUpheld()
    {
        // Idempotency guard: once a credit is issued for a dispute, a
        // second click must not mint a second ledger row.
        var h = new Harness();
        var dispute = await h.SeedDisputeAsync();
        await h.Credits.IssueCreditAsync(dispute.DisputeId, "admin-1", 2, "first", default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Credits.IssueCreditAsync(dispute.DisputeId, "admin-2", 10, "second", default));
        Assert.Contains("already Upheld", ex.Message, StringComparison.Ordinal);

        // Ledger still has exactly one row for this dispute.
        var rows = await h.Ledger.ListByStudentAsync("stu-hash", 50, default);
        Assert.Single(rows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(51)]
    [InlineData(1000)]
    public async Task IssueCreditRejectsUploadBumpOutsideAllowedRange(int badValue)
    {
        var h = new Harness();
        var dispute = await h.SeedDisputeAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            h.Credits.IssueCreditAsync(dispute.DisputeId, "admin-1", badValue, "", default));
    }

    [Fact]
    public async Task DispatcherIsCalledExactlyOnceOnSuccess()
    {
        var h = new Harness();
        var dispute = await h.SeedDisputeAsync();

        await h.Credits.IssueCreditAsync(dispute.DisputeId, "admin-1", 4, "", default);

        Assert.Equal(1, h.Dispatcher.Calls);
        var last = Assert.Single(h.Dispatcher.Received);
        Assert.Equal(dispute.DisputeId, last.Ledger.DisputeId);
        Assert.Equal(dispute.StudentSubjectIdHash, last.StudentSubjectIdHash);
    }

    [Fact]
    public async Task QuotaGateReturnsAllowWhenCreditCoversRawUsage()
    {
        // PhotoDiagnosticQuotaGate consults IDiagnosticCreditLedger and
        // subtracts credits from raw monthly usage before the cap check.
        // A Plus student with rawUsage=20 (hard cap hit) and a 5-credit
        // grant in the same month should see Allow because effectiveUsage
        // becomes 15.
        var h = new Harness();
        var usage = new InMemoryPhotoDiagnosticMonthlyUsage();
        var now = h.Clock.GetUtcNow();

        // Burn the Plus cap (20/mo hard).
        for (var i = 0; i < 20; i++)
            await usage.IncrementAsync("stu-cap", now, default);

        // Seed + credit a dispute on that same student this month.
        var dispute = await h.Disputes.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-cap", "stu-cap", DisputeReason.WrongNarration, null), default);
        await h.Credits.IssueCreditAsync(dispute.DisputeId, "admin-1", 5, "system error", default);

        var gate = new PhotoDiagnosticQuotaGate(
            new FakeEntitlementResolver(SubscriptionTier.Plus),
            usage,
            new PerTierCapEnforcer(),
            h.Ledger);

        var decision = await gate.CheckAsync("stu-cap", now, default);

        Assert.Equal(CapDecision.Allow, decision.Decision);
        Assert.Equal(15, decision.CurrentUsage);
    }

    [Fact]
    public async Task ListByStudentReturnsCreditHistoryMostRecentFirst()
    {
        var h = new Harness();

        // Two disputes for the same student, one credit each, spaced in time.
        var d1 = await h.Disputes.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-1", "stu-hist", DisputeReason.OcrMisread, null), default);
        await h.Credits.IssueCreditAsync(d1.DisputeId, "admin-1", 2, "first", default);

        h.Clock.Advance(TimeSpan.FromMinutes(10));

        var d2 = await h.Disputes.SubmitAsync(new SubmitDiagnosticDisputeCommand(
            "d-2", "stu-hist", DisputeReason.WrongStepIdentified, null), default);
        await h.Credits.IssueCreditAsync(d2.DisputeId, "admin-2", 7, "second", default);

        var list = await h.Ledger.ListByStudentAsync("stu-hist", 10, default);

        Assert.Equal(2, list.Count);
        Assert.Equal(d2.DisputeId, list[0].DisputeId); // newest first
        Assert.Equal(d1.DisputeId, list[1].DisputeId);
    }

    [Fact]
    public async Task DispatcherFailureDoesNotRollBackCreditIssuance()
    {
        // Credit-on-account must land even if the apology dispatch fails.
        var h = new Harness();
        h.Dispatcher.ThrowNext = true;
        var dispute = await h.SeedDisputeAsync();

        var credit = await h.Credits.IssueCreditAsync(
            dispute.DisputeId, "admin-1", 3, "", default);

        var stored = await h.Ledger.GetByDisputeAsync(dispute.DisputeId, default);
        Assert.NotNull(stored);
        Assert.Equal(credit.Id, stored!.Id);

        var after = await h.Disputes.GetAsync(dispute.DisputeId, default);
        Assert.Equal(DisputeStatus.Upheld, after!.Status);
    }

    // ---- Test doubles ------------------------------------------------------

    private sealed class RecordingDispatcher : IDiagnosticCreditDispatcher
    {
        public int Calls { get; private set; }
        public List<DiagnosticCreditDispatchRequest> Received { get; } = new();
        public bool ThrowNext { get; set; }

        public Task<DiagnosticCreditDispatchResult> DispatchAsync(
            DiagnosticCreditDispatchRequest request, CancellationToken ct)
        {
            Calls++;
            Received.Add(request);
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new InvalidOperationException("simulated dispatch failure");
            }
            return Task.FromResult(new DiagnosticCreditDispatchResult(
                Delivered: true, Channel: "test", ErrorCode: null));
        }
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
