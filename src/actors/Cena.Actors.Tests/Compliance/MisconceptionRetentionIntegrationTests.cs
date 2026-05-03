// =============================================================================
// Cena Platform — Misconception retention integration tests (prr-015)
//
// End-to-end happy path through the registry + worker + fake store:
//
//   1. Host registers a fake misconception store (backed by an in-memory
//      list, declared retention = 30 days).
//   2. Caller seeds two misconception records: one detected 31 days ago,
//      one detected 7 days ago.
//   3. The retention worker runs (RunOnceAsync) against the clock at time T.
//   4. Assert only the 31-day-old record has been purged.
//
// This covers the contract the task body calls out under "Integration test:
// create misconception record, fast-forward 31 days, verify purge."
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Compliance;

public sealed class MisconceptionRetentionIntegrationTests
{
    private sealed class FakeMisconceptionRow
    {
        public Guid Id { get; init; }
        public string SessionId { get; init; } = "";
        public string BuggyRuleId { get; init; } = "";
        public string StudentAnswer { get; set; } = "";
        public DateTimeOffset DetectedAt { get; init; }
    }

    private sealed class FakeMisconceptionStore
    {
        public List<FakeMisconceptionRow> Rows { get; } = new();

        public int PurgeOlderThan(DateTimeOffset cutoff)
        {
            var toRemove = Rows.Where(r => r.DetectedAt < cutoff).ToList();
            foreach (var r in toRemove) Rows.Remove(r);
            return toRemove.Count;
        }
    }

    [Fact]
    public async Task EndToEnd_RecordOlderThan30Days_IsPurgedByWorker()
    {
        // Arrange
        var t0 = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
        var clock = new MisconceptionTestClock(t0);
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var metrics = new MisconceptionRetentionMetrics(clock);

        var fake = new FakeMisconceptionStore();

        // Seed one expired + one fresh record.
        var expiredId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        fake.Rows.Add(new FakeMisconceptionRow
        {
            Id = expiredId,
            SessionId = "session-a",
            BuggyRuleId = "DIST-EXP-SUM",
            StudentAnswer = "x² + 9",
            DetectedAt = t0 - TimeSpan.FromDays(31),
        });
        fake.Rows.Add(new FakeMisconceptionRow
        {
            Id = freshId,
            SessionId = "session-b",
            BuggyRuleId = "CHAIN-RULE-MISSING",
            StudentAnswer = "cos(2x)",
            DetectedAt = t0 - TimeSpan.FromDays(7),
        });

        var store = new RegisteredMisconceptionStore(
            StoreName: "fake-misconception-store",
            RetentionDays: 30,
            PurgeStrategy: MisconceptionPurgeStrategy.Delete,
            SessionScopeVerified: true,
            OwningModule: "Cena.Actors.Tests.Compliance");

        registry.Register(store,
            (cutoff, _) => Task.FromResult(fake.PurgeOlderThan(cutoff)));

        var services = new ServiceCollection();
        services.AddSingleton<IMisconceptionPiiStoreRegistry>(registry);
        services.AddSingleton(metrics);
        services.AddSingleton<Cena.Infrastructure.Compliance.IClock>(clock);
        var sp = services.BuildServiceProvider();

        var worker = new MisconceptionRetentionWorker(
            sp,
            NullLogger<MisconceptionRetentionWorker>.Instance,
            Options.Create(new MisconceptionRetentionWorkerOptions()),
            clock,
            metrics);

        // Act
        await worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.DoesNotContain(fake.Rows, r => r.Id == expiredId);
        Assert.Contains(fake.Rows, r => r.Id == freshId);
        Assert.NotNull(metrics.GetLastSuccess("fake-misconception-store"));
    }

    [Fact]
    public async Task EndToEnd_NoExpiredRecords_WorkerRunsIsClean()
    {
        var t0 = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
        var clock = new MisconceptionTestClock(t0);
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var metrics = new MisconceptionRetentionMetrics(clock);

        var fake = new FakeMisconceptionStore();
        fake.Rows.Add(new FakeMisconceptionRow
        {
            Id = Guid.NewGuid(),
            SessionId = "session-a",
            BuggyRuleId = "INTEGRAL-CONSTANT",
            StudentAnswer = "x^2",
            DetectedAt = t0 - TimeSpan.FromDays(5),
        });

        registry.Register(
            new RegisteredMisconceptionStore(
                "fake-store",
                30,
                MisconceptionPurgeStrategy.Delete,
                SessionScopeVerified: true,
                OwningModule: "Cena.Actors.Tests.Compliance"),
            (cutoff, _) => Task.FromResult(fake.PurgeOlderThan(cutoff)));

        var services = new ServiceCollection();
        services.AddSingleton<IMisconceptionPiiStoreRegistry>(registry);
        services.AddSingleton(metrics);
        services.AddSingleton<Cena.Infrastructure.Compliance.IClock>(clock);
        var sp = services.BuildServiceProvider();

        var worker = new MisconceptionRetentionWorker(
            sp,
            NullLogger<MisconceptionRetentionWorker>.Instance,
            Options.Create(new MisconceptionRetentionWorkerOptions()),
            clock,
            metrics);

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Single(fake.Rows);
        Assert.NotNull(metrics.GetLastSuccess("fake-store"));
    }

    [Fact]
    public async Task EndToEnd_ClockAdvanceAcross30DayBoundary_RecordPurgedOnSecondRun()
    {
        // Start: record is 29 days old. Advance 2 days to 31. Re-run.
        var t0 = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        var clock = new MisconceptionTestClock(t0);
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var metrics = new MisconceptionRetentionMetrics(clock);

        var fake = new FakeMisconceptionStore();
        var rowId = Guid.NewGuid();
        fake.Rows.Add(new FakeMisconceptionRow
        {
            Id = rowId,
            SessionId = "session-a",
            BuggyRuleId = "SQRT-SUM",
            StudentAnswer = "7",
            DetectedAt = t0 - TimeSpan.FromDays(29),
        });

        registry.Register(
            new RegisteredMisconceptionStore(
                "fake-store",
                30,
                MisconceptionPurgeStrategy.Delete,
                SessionScopeVerified: true,
                OwningModule: "Cena.Actors.Tests.Compliance"),
            (cutoff, _) => Task.FromResult(fake.PurgeOlderThan(cutoff)));

        var services = new ServiceCollection();
        services.AddSingleton<IMisconceptionPiiStoreRegistry>(registry);
        services.AddSingleton(metrics);
        services.AddSingleton<Cena.Infrastructure.Compliance.IClock>(clock);
        var sp = services.BuildServiceProvider();

        var worker = new MisconceptionRetentionWorker(
            sp,
            NullLogger<MisconceptionRetentionWorker>.Instance,
            Options.Create(new MisconceptionRetentionWorkerOptions()),
            clock,
            metrics);

        // First run: record is 29d old, inside retention.
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.Contains(fake.Rows, r => r.Id == rowId);

        // Advance past the 30-day boundary.
        clock.Advance(TimeSpan.FromDays(2));

        // Second run: record is now 31d old, outside retention.
        await worker.RunOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(fake.Rows, r => r.Id == rowId);
    }
}
