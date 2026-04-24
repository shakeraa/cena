// =============================================================================
// Cena Platform — DiagnosticDisputeRetentionWorker tests (EPIC-PRR-J PRR-410)
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticDisputeRetentionWorkerTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static async Task<(DiagnosticDisputeRetentionWorker Worker,
        InMemoryDiagnosticDisputeRepository Repo,
        FakeTimeProvider Clock)> NewWorkerWithSeedAsync(DateTimeOffset now, int keepN, int purgeN)
    {
        var clock = new FakeTimeProvider(now);
        var repo = new InMemoryDiagnosticDisputeRepository();

        // Seed N "keep" disputes within the window, N "purge" disputes past it.
        for (int i = 0; i < keepN; i++)
        {
            await repo.InsertAsync(MakeDoc($"keep-{i}", now.AddDays(-30)), default);
        }
        for (int i = 0; i < purgeN; i++)
        {
            await repo.InsertAsync(MakeDoc($"purge-{i}", now.AddDays(-100)), default);
        }

        var worker = new DiagnosticDisputeRetentionWorker(
            repo, clock, NullLogger<DiagnosticDisputeRetentionWorker>.Instance);
        return (worker, repo, clock);
    }

    [Fact]
    public async Task DeletesDisputesOlderThanRetentionWindow()
    {
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var (worker, repo, _) = await NewWorkerWithSeedAsync(now, keepN: 3, purgeN: 4);

        var deleted = await worker.RunOnceAsync(default);

        Assert.Equal(4, deleted);
        var remaining = await repo.ListRecentAsync(null, 100, default);
        Assert.Equal(3, remaining.Count);
        Assert.All(remaining, d => Assert.StartsWith("keep-", d.Id));
    }

    [Fact]
    public async Task DeletesZeroWhenAllDisputesAreRecent()
    {
        var now = DateTimeOffset.UtcNow;
        var (worker, _, _) = await NewWorkerWithSeedAsync(now, keepN: 5, purgeN: 0);

        var deleted = await worker.RunOnceAsync(default);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task ExactlyAtThresholdIsKept()
    {
        var now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var repo = new InMemoryDiagnosticDisputeRepository();
        // SubmittedAt == now - RetentionWindow: strict < means this row is KEPT
        await repo.InsertAsync(MakeDoc("exact-threshold",
            now - DiagnosticDisputeRetentionWorker.RetentionWindow), default);
        var worker = new DiagnosticDisputeRetentionWorker(
            repo, clock, NullLogger<DiagnosticDisputeRetentionWorker>.Instance);

        var deleted = await worker.RunOnceAsync(default);

        Assert.Equal(0, deleted);
        Assert.NotNull(await repo.GetAsync("exact-threshold", default));
    }

    [Fact]
    public void RetentionWindowIs90Days()
    {
        Assert.Equal(TimeSpan.FromDays(90), DiagnosticDisputeRetentionWorker.RetentionWindow);
    }

    [Fact]
    public void RunIntervalIs24Hours()
    {
        Assert.Equal(TimeSpan.FromHours(24), DiagnosticDisputeRetentionWorker.RunInterval);
    }

    private static DiagnosticDisputeDocument MakeDoc(string id, DateTimeOffset submittedAt) => new()
    {
        Id = id,
        DiagnosticId = $"diag-{id}",
        StudentSubjectIdHash = "hash",
        Reason = DisputeReason.Other,
        StudentComment = null,
        Status = DisputeStatus.New,
        SubmittedAt = submittedAt,
        ReviewedAt = null,
        ReviewerNote = null,
    };
}
