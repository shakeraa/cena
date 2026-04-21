// =============================================================================
// Cena Platform — Tutor Turn Budget Integration Tests (prr-105)
//
// Verifies the 4 DoD scenarios from prr-105:
//   Test 1: Under the cap → CanTakeTurnAsync returns true.
//   Test 2: At the cap → returns false + cap-hit metric emitted.
//   Test 3: 21st turn (default cap 20) → fallback path.
//   Test 4: Per-institute config override tightens the cap.
//
// Uses a FakeDatabase that implements the StackExchange.Redis surface we rely
// on (StringGet / StringIncrement / KeyExpire). Keeps the tests self-
// contained without requiring a live Redis.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Tutor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Tutor;

public sealed class TutorTurnBudgetTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly FakeMeterFactory _meterFactory = new();

    public TutorTurnBudgetTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_db);
    }

    private TutorTurnBudget NewSut(
        int? defaultMaxTurns = null,
        Dictionary<string, string?>? extraConfig = null)
    {
        var config = new Dictionary<string, string?>();
        if (defaultMaxTurns.HasValue)
            config["Cena:Tutor:DefaultMaxTurns"] = defaultMaxTurns.Value.ToString();
        if (extraConfig is not null)
            foreach (var kv in extraConfig) config[kv.Key] = kv.Value;

        IConfiguration built = new ConfigurationBuilder()
            .AddInMemoryCollection(config!)
            .Build();

        return new TutorTurnBudget(
            _redis, built, NullLogger<TutorTurnBudget>.Instance, _meterFactory);
    }

    [Fact]
    public async Task UnderCap_AllowsTurn()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("5"));

        var sut = NewSut();
        var allowed = await sut.CanTakeTurnAsync("session-1", "institute-A");

        Assert.True(allowed);
    }

    [Fact]
    public async Task AtCap_RefusesTurn_AndRecordsCapHitMetric()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("20")); // = default cap

        // Attach a MeterListener before SUT construction so the instrument
        // creation is visible; capture cap-hit measurements + their labels.
        var capHitMeasurements = new List<(long Value, string? InstituteLabel)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Cena.Actors.TutorTurnBudget")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name != "cena_tutor_turn_cap_hit_total") return;
            string? institute = null;
            foreach (var tag in tags)
                if (tag.Key == "institute_id") institute = tag.Value?.ToString();
            capHitMeasurements.Add((value, institute));
        });
        listener.Start();

        var sut = NewSut();
        var allowed = await sut.CanTakeTurnAsync("session-1", "institute-A");

        Assert.False(allowed);

        // prr-105: cap-hit metric must be emitted exactly once with the
        // institute_id label populated.
        Assert.Single(capHitMeasurements);
        Assert.Equal(1L, capHitMeasurements[0].Value);
        Assert.Equal("institute-A", capHitMeasurements[0].InstituteLabel);
    }

    [Fact]
    public async Task TwentyFirstTurn_IsRefused()
    {
        // 20 turns recorded → 21st call sees count == 20 → refused.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("20"));

        var sut = NewSut();
        Assert.False(await sut.CanTakeTurnAsync("session-abc"));
    }

    [Fact]
    public async Task NewSession_CountsFromZero_AllowsFirstTurn()
    {
        // Fresh session → Redis returns empty → count 0 → allowed.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var sut = NewSut();
        Assert.True(await sut.CanTakeTurnAsync("fresh-session"));
    }

    [Fact]
    public void ResolveCap_DefaultsToPlatformValue_WhenNoOverride()
    {
        var sut = NewSut(defaultMaxTurns: 20);
        Assert.Equal(20, sut.ResolveCapFor("institute-without-override"));
        Assert.Equal(20, sut.ResolveCapFor(null));
    }

    [Fact]
    public void ResolveCap_AppliesInstituteOverride_WhenConfigured()
    {
        var sut = NewSut(
            defaultMaxTurns: 20,
            extraConfig: new Dictionary<string, string?>
            {
                ["Cena:Tutor:InstituteOverrides:strict-school:MaxTurns"] = "5"
            });
        Assert.Equal(5, sut.ResolveCapFor("strict-school"));
    }

    [Fact]
    public void ResolveCap_RefusesToRaiseAbovePlatformDefault()
    {
        // Institutes may tighten but not relax the platform cap.
        var sut = NewSut(
            defaultMaxTurns: 20,
            extraConfig: new Dictionary<string, string?>
            {
                ["Cena:Tutor:InstituteOverrides:chatty-school:MaxTurns"] = "999"
            });
        Assert.Equal(20, sut.ResolveCapFor("chatty-school"));
    }

    [Fact]
    public void ResolveCap_IgnoresBelowFloor_Configuration()
    {
        // Configured = 2 (below floor). The platform default is clamped
        // to the floor at construction time.
        var sut = NewSut(defaultMaxTurns: 2);
        Assert.Equal(TutorTurnBudget.MinConfigurableTurns, sut.ResolveCapFor(null));
    }

    [Fact]
    public async Task RecordTurn_AtomicallyIncrementsRedis()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(7L);

        var sut = NewSut();
        var count = await sut.RecordTurnAsync("session-xyz");

        Assert.Equal(7L, count);
        await _db.Received(1).StringIncrementAsync(Arg.Any<RedisKey>(), 1L, Arg.Any<CommandFlags>());
        await _db.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(), Arg.Any<TimeSpan>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CanTakeTurn_FailsOpen_WhenRedisThrows()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(
                ConnectionFailureType.UnableToConnect, "redis down"));

        var sut = NewSut();
        // Fail-safe: block tutoring on Redis outage would be worse than
        // leaking a single overage. Independent backstops (prr-012
        // SocraticCallBudget + ICostCircuitBreaker) remain in force.
        Assert.True(await sut.CanTakeTurnAsync("any-session"));
    }

    /// <summary>
    /// Minimal IMeterFactory for unit tests — returns real Meter instances
    /// so measurements are observable via MeterListener.
    /// </summary>
    private sealed class FakeMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
