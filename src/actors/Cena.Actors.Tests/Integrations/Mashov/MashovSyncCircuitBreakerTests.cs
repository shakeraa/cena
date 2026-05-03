// =============================================================================
// Cena Platform — MashovSyncCircuitBreakerTests (prr-039)
//
// Unit + integration-style tests for the Mashov circuit breaker:
//
//   1. Opens after 5 failures within the sampling window
//   2. Half-open after cooldown, then closes on success
//   3. Failure during half-open re-opens immediately
//   4. Per-tenant isolation — one tenant's circuit does not cascade
//   5. Staleness service reads last-successful-sync from the circuit
//   6. End-to-end: forced 5xx → circuit opens → staleness badge fires
//      (the task's explicit integration test demand)
// =============================================================================

using Cena.Actors.Integrations.Mashov;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Integrations.Mashov;

/// <summary>
/// Minimal controllable TimeProvider — avoids taking a new package
/// dependency (Microsoft.Extensions.TimeProvider.Testing) that isn't
/// part of the current Cena.Actors.Tests package set. Mirrors the
/// FrozenClock convention used elsewhere in this test project.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;
    public FakeTimeProvider(DateTimeOffset now) { _now = now; }
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) { _now = _now.Add(delta); }
}

public sealed class MashovSyncCircuitBreakerTests
{
    private static MashovSyncCircuitBreaker NewBreaker(FakeTimeProvider time) =>
        new(NullLogger<MashovSyncCircuitBreaker>.Instance, time);

    [Fact]
    public void Closed_On_Boot_For_Unknown_Tenant()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        var status = cb.Status("tenant-a");

        Assert.Equal(MashovCircuitState.Closed, status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
        Assert.Null(status.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public void Opens_After_Five_Failures_Within_Window()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", $"failure-{i}");

        Assert.Equal(MashovCircuitState.Open, cb.Status("tenant-a").State);
    }

    [Fact]
    public void Stays_Closed_If_Failures_Fall_Outside_Window()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        cb.RecordFailure("tenant-a", "f1");
        cb.RecordFailure("tenant-a", "f2");

        // Advance past the sampling window; the two old failures age out.
        time.Advance(MashovSyncCircuitBreaker.SamplingWindow + TimeSpan.FromSeconds(1));

        cb.RecordFailure("tenant-a", "f3");
        Assert.Equal(MashovCircuitState.Closed, cb.Status("tenant-a").State);
    }

    [Fact]
    public async Task Open_Circuit_Throws_Without_Invoking_Call()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", "f");

        var called = false;
        await Assert.ThrowsAsync<MashovCircuitOpenException>(async () =>
        {
            await cb.ExecuteAsync("tenant-a", ct =>
            {
                called = true;
                return Task.FromResult(42);
            });
        });

        Assert.False(called, "inner call must NOT be invoked when circuit is open");
    }

    [Fact]
    public async Task Transitions_HalfOpen_After_Cooldown_Then_Closes_On_Success()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", "f");
        Assert.Equal(MashovCircuitState.Open, cb.Status("tenant-a").State);

        // Advance past the cooldown; ExecuteAsync will observe half-open.
        time.Advance(MashovSyncCircuitBreaker.CooldownDuration + TimeSpan.FromSeconds(1));

        var result = await cb.ExecuteAsync("tenant-a", _ => Task.FromResult("ok"));
        Assert.Equal("ok", result);
        Assert.Equal(MashovCircuitState.Closed, cb.Status("tenant-a").State);
    }

    [Fact]
    public async Task Failure_During_HalfOpen_Reopens_Circuit()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", "f");
        time.Advance(MashovSyncCircuitBreaker.CooldownDuration + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await cb.ExecuteAsync<int>("tenant-a", _ =>
                throw new InvalidOperationException("upstream 503"));
        });

        Assert.Equal(MashovCircuitState.Open, cb.Status("tenant-a").State);
    }

    [Fact]
    public void Per_Tenant_Isolation_One_Tenant_Does_Not_Cascade_Into_Others()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", "f");
        Assert.Equal(MashovCircuitState.Open, cb.Status("tenant-a").State);

        // tenant-b was silent — must still be closed.
        Assert.Equal(MashovCircuitState.Closed, cb.Status("tenant-b").State);
    }

    [Fact]
    public void RecordSuccess_Updates_LastSuccessfulSync_Even_When_Already_Closed()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = NewBreaker(time);

        cb.RecordSuccess("tenant-a");
        var status = cb.Status("tenant-a");
        Assert.NotNull(status.LastSuccessfulSyncAtUtc);
        Assert.Equal(time.GetUtcNow(), status.LastSuccessfulSyncAtUtc);
    }
}

public sealed class MashovStalenessServiceTests
{
    private sealed class FakeTenantSource : IMashovProbeTenantSource
    {
        private readonly List<string> _tenants;
        public FakeTenantSource(params string[] tenants) { _tenants = tenants.ToList(); }
        public IReadOnlyList<string> ConfiguredTenants() => _tenants;
    }

    [Fact]
    public void Unconfigured_Tenant_Returns_IsStale_False()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = new MashovSyncCircuitBreaker(NullLogger<MashovSyncCircuitBreaker>.Instance, time);
        var svc = new MashovStalenessService(cb, new FakeTenantSource(), time);

        var dto = svc.ForTenant("tenant-x");
        Assert.False(dto.IsConfigured);
        Assert.False(dto.IsStale);
        Assert.Null(dto.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public void Configured_But_Never_Synced_Returns_IsStale_True()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = new MashovSyncCircuitBreaker(NullLogger<MashovSyncCircuitBreaker>.Instance, time);
        var svc = new MashovStalenessService(cb, new FakeTenantSource("tenant-a"), time);

        var dto = svc.ForTenant("tenant-a");

        Assert.True(dto.IsConfigured);
        Assert.True(dto.IsStale);
        Assert.Null(dto.LastSuccessfulSyncAtUtc);
    }

    [Fact]
    public void Within_Threshold_Returns_IsStale_False()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = new MashovSyncCircuitBreaker(NullLogger<MashovSyncCircuitBreaker>.Instance, time);
        var svc = new MashovStalenessService(cb, new FakeTenantSource("tenant-a"), time);

        cb.RecordSuccess("tenant-a");
        // Advance 1 minute — below 5-minute threshold.
        time.Advance(TimeSpan.FromMinutes(1));

        var dto = svc.ForTenant("tenant-a");
        Assert.False(dto.IsStale);
        Assert.Equal(1, dto.MinutesSinceLastSync);
    }

    [Fact]
    public void Beyond_Threshold_Returns_IsStale_True()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = new MashovSyncCircuitBreaker(NullLogger<MashovSyncCircuitBreaker>.Instance, time);
        var svc = new MashovStalenessService(cb, new FakeTenantSource("tenant-a"), time);

        cb.RecordSuccess("tenant-a");
        // Advance 6 minutes — above the 5-minute threshold.
        time.Advance(TimeSpan.FromMinutes(6));

        var dto = svc.ForTenant("tenant-a");
        Assert.True(dto.IsStale);
        Assert.Equal(6, dto.MinutesSinceLastSync);
    }

    [Fact]
    public void Forced_5xx_Opens_Circuit_And_Staleness_Fires_EndToEnd()
    {
        // The task's integration-test call-out:
        //   "forced 5xx → circuit opens → staleness badge fires"
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cb = new MashovSyncCircuitBreaker(NullLogger<MashovSyncCircuitBreaker>.Instance, time);
        var svc = new MashovStalenessService(cb, new FakeTenantSource("tenant-a"), time);

        // 1. Start with a healthy sync.
        cb.RecordSuccess("tenant-a");
        Assert.False(svc.ForTenant("tenant-a").IsStale);

        // 2. Simulate five consecutive 5xx by recording failures.
        for (int i = 0; i < MashovSyncCircuitBreaker.FailureThreshold; i++)
            cb.RecordFailure("tenant-a", "HTTP 503 from api.mashov.info");
        Assert.Equal(MashovCircuitState.Open, cb.Status("tenant-a").State);

        // 3. Time passes past the staleness threshold without any
        //    successful resync (circuit is open; probes all fail-through).
        time.Advance(MashovStalenessService.StalenessThreshold + TimeSpan.FromMinutes(1));

        // 4. Staleness badge fires.
        var dto = svc.ForTenant("tenant-a");
        Assert.True(dto.IsStale, "prr-039: stale-badge must fire when sync is >5m old with open circuit");
        Assert.Equal("open", dto.CircuitState);
    }
}
