// =============================================================================
// Cena Platform — Misconception Retention Worker unit tests (prr-015)
//
// Covers:
//   • One purge callback invoked per registered store with correct cutoff.
//   • Clamped retention (>90d) → cutoff = now - 90d, clamp metric emitted.
//   • Per-store timeout kills the one slow callback, others still run.
//   • A throwing callback does not abort the sweep; failure counter fires.
//   • Last-success timestamp updates; lag gauge observation reflects it.
//   • Each of the three purge strategies (Delete, Anonymize, HashRedact) is
//     exercised by an in-memory fake store so the enum cannot rot.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Tests.Compliance;

/// <summary>
/// Fast-forwardable clock implementing the production
/// <see cref="Cena.Infrastructure.Compliance.IClock"/>. Named to avoid
/// collision with the test-local <c>TestClock</c> defined in
/// <c>RetentionWorkerTests</c>, which targets a different IClock interface.
/// </summary>
internal sealed class MisconceptionTestClock : Cena.Infrastructure.Compliance.IClock
{
    private DateTimeOffset _now;
    public MisconceptionTestClock(DateTimeOffset initial) { _now = initial; }
    public DateTimeOffset UtcNow => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

public sealed class MisconceptionRetentionWorkerTests
{
    private static (MisconceptionRetentionWorker Worker,
                    InMemoryMisconceptionPiiStoreRegistry Registry,
                    MisconceptionRetentionMetrics Metrics,
                    MisconceptionTestClock Clock)
        BuildWorker(TimeSpan? storeTimeout = null)
    {
        var clock = new MisconceptionTestClock(new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero));
        var registry = new InMemoryMisconceptionPiiStoreRegistry();
        var metrics = new MisconceptionRetentionMetrics(clock);

        var services = new ServiceCollection();
        services.AddSingleton<IMisconceptionPiiStoreRegistry>(registry);
        services.AddSingleton(metrics);
        services.AddSingleton<Cena.Infrastructure.Compliance.IClock>(clock);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new MisconceptionRetentionWorkerOptions
        {
            CronExpression = "15 * * * *",
            StoreTimeout = storeTimeout ?? TimeSpan.FromSeconds(30)
        });

        var worker = new MisconceptionRetentionWorker(
            sp,
            NullLogger<MisconceptionRetentionWorker>.Instance,
            options,
            clock,
            metrics);

        return (worker, registry, metrics, clock);
    }

    private static RegisteredMisconceptionStore NewStore(
        string name,
        int days = 30,
        MisconceptionPurgeStrategy strategy = MisconceptionPurgeStrategy.Delete,
        bool verified = true) =>
        new(name, days, strategy, verified, OwningModule: "Cena.Actors.Tests");

    [Fact]
    public async Task RunOnceAsync_InvokesEveryRegisteredPurgeCallback()
    {
        var (worker, registry, metrics, clock) = BuildWorker();

        var calls = new List<(string Name, DateTimeOffset Cutoff)>();

        registry.Register(NewStore("alpha", days: 30),
            (cutoff, _) => { calls.Add(("alpha", cutoff)); return Task.FromResult(3); });
        registry.Register(NewStore("bravo", days: 60),
            (cutoff, _) => { calls.Add(("bravo", cutoff)); return Task.FromResult(5); });

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, calls.Count);

        var alpha = calls.Single(c => c.Name == "alpha");
        var bravo = calls.Single(c => c.Name == "bravo");
        Assert.Equal(clock.UtcNow - TimeSpan.FromDays(30), alpha.Cutoff);
        Assert.Equal(clock.UtcNow - TimeSpan.FromDays(60), bravo.Cutoff);

        Assert.NotNull(metrics.GetLastSuccess("alpha"));
        Assert.NotNull(metrics.GetLastSuccess("bravo"));
    }

    [Fact]
    public async Task RunOnceAsync_DeclaredRetentionOverHardCap_ClampsTo90d()
    {
        var (worker, registry, _, clock) = BuildWorker();
        DateTimeOffset seenCutoff = default;

        registry.Register(NewStore("alpha", days: 365),
            (cutoff, _) => { seenCutoff = cutoff; return Task.FromResult(0); });

        await worker.RunOnceAsync(CancellationToken.None);

        // Effective cutoff must be now - 90d, not now - 365d.
        Assert.Equal(clock.UtcNow - TimeSpan.FromDays(90), seenCutoff);
    }

    [Fact]
    public async Task RunOnceAsync_OneStoreThrows_OthersStillPurge()
    {
        var (worker, registry, metrics, _) = BuildWorker();
        var alphaRan = false;
        var charlieRan = false;

        registry.Register(NewStore("alpha"),
            (_, __) => { alphaRan = true; return Task.FromResult(1); });
        registry.Register(NewStore("bravo"),
            (_, __) => throw new InvalidOperationException("boom"));
        registry.Register(NewStore("charlie"),
            (_, __) => { charlieRan = true; return Task.FromResult(2); });

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.True(alphaRan);
        Assert.True(charlieRan);
        // Failing store does NOT get a last-success stamp.
        Assert.Null(metrics.GetLastSuccess("bravo"));
        Assert.NotNull(metrics.GetLastSuccess("alpha"));
        Assert.NotNull(metrics.GetLastSuccess("charlie"));
    }

    [Fact]
    public async Task RunOnceAsync_SlowStoreTimesOut_OthersStillRun()
    {
        var (worker, registry, metrics, _) = BuildWorker(storeTimeout: TimeSpan.FromMilliseconds(50));
        var bravoRan = false;

        registry.Register(NewStore("alpha"),
            async (_, ct) =>
            {
                // Wait longer than the per-store timeout. The linked CTS
                // trips, OperationCanceledException flies, the worker logs +
                // counts but does not abort the sweep.
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return 0;
            });

        registry.Register(NewStore("bravo"),
            (_, __) => { bravoRan = true; return Task.FromResult(7); });

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.True(bravoRan, "bravo must still run even though alpha timed out");
        Assert.Null(metrics.GetLastSuccess("alpha"));
        Assert.NotNull(metrics.GetLastSuccess("bravo"));
    }

    [Fact]
    public async Task RunOnceAsync_RecordsLastSuccessTimestamp()
    {
        var (worker, registry, metrics, clock) = BuildWorker();
        registry.Register(NewStore("alpha"),
            (_, __) => Task.FromResult(0));

        await worker.RunOnceAsync(CancellationToken.None);
        var first = metrics.GetLastSuccess("alpha");
        Assert.NotNull(first);

        clock.Advance(TimeSpan.FromHours(6));
        await worker.RunOnceAsync(CancellationToken.None);
        var second = metrics.GetLastSuccess("alpha");

        Assert.NotNull(second);
        Assert.Equal(TimeSpan.FromHours(6), second!.Value - first!.Value);
    }

    [Theory]
    [InlineData(MisconceptionPurgeStrategy.Delete)]
    [InlineData(MisconceptionPurgeStrategy.Anonymize)]
    [InlineData(MisconceptionPurgeStrategy.HashRedact)]
    public async Task RunOnceAsync_AllPurgeStrategiesInvokeCallback(MisconceptionPurgeStrategy strategy)
    {
        var (worker, registry, metrics, _) = BuildWorker();
        var called = false;

        registry.Register(NewStore($"alpha-{strategy}", strategy: strategy),
            (_, __) => { called = true; return Task.FromResult(1); });

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.True(called);
        Assert.NotNull(metrics.GetLastSuccess($"alpha-{strategy}"));
    }

    [Fact]
    public async Task RunOnceAsync_UnverifiedSessionScope_StillPurges()
    {
        var (worker, registry, metrics, _) = BuildWorker();
        var called = false;

        registry.Register(NewStore("alpha", verified: false),
            (_, __) => { called = true; return Task.FromResult(1); });

        await worker.RunOnceAsync(CancellationToken.None);

        Assert.True(called);
        // Worker still treats the purge as a success. The unverified flag
        // shows up in logs + (transitively) alerting; it does not block
        // retention enforcement.
        Assert.NotNull(metrics.GetLastSuccess("alpha"));
    }
}
