// =============================================================================
// Cena Platform — SocraticCallBudget Unit Tests (prr-012)
//
// Verifies the 3-call/session hard cap on Socratic LLM invocations.
// Redis is substituted; the tests focus on the counter semantics, the
// fail-safe path when Redis throws, and the at-cap boundary.
// =============================================================================

using Cena.Actors.Tutor;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Tests.Tutor;

public sealed class SocraticCallBudgetTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly SocraticCallBudget _sut;

    public SocraticCallBudgetTests()
    {
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        _sut = new SocraticCallBudget(
            _redis, NullLogger<SocraticCallBudget>.Instance, new DummyMeterFactory());
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_FirstCall_ReturnsTrue()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisValue.Null));

        var allowed = await _sut.CanMakeLlmCallAsync("session-1");

        Assert.True(allowed);
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_UnderCap_ReturnsTrue()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult((RedisValue)2L));

        var allowed = await _sut.CanMakeLlmCallAsync("session-1");

        Assert.True(allowed);
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_AtCap_ReturnsFalse()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult((RedisValue)(long)SocraticCallBudget.MaxLlmCallsPerSession));

        var allowed = await _sut.CanMakeLlmCallAsync("session-1");

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_BeyondCap_ReturnsFalse()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult((RedisValue)42L));

        var allowed = await _sut.CanMakeLlmCallAsync("session-1");

        Assert.False(allowed);
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_RedisThrows_FailsOpen()
    {
        // Fail-safe: tutoring must not be entirely blocked on a Redis outage.
        // ICostCircuitBreaker is the independent backstop on actual spend.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisValue>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));

        var allowed = await _sut.CanMakeLlmCallAsync("session-1");

        Assert.True(allowed);
    }

    [Fact]
    public async Task RecordLlmCallAsync_IncrementsAndExpires()
    {
        _db.StringIncrementAsync(Arg.Any<RedisKey>(), 1L, Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(1L));
        _db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));

        var count = await _sut.RecordLlmCallAsync("session-1");

        Assert.Equal(1L, count);
        await _db.Received(1).StringIncrementAsync(
            Arg.Is<RedisKey>(k => k.ToString().EndsWith(":session-1")),
            1L, Arg.Any<CommandFlags>());
        await _db.Received(1).KeyExpireAsync(
            Arg.Is<RedisKey>(k => k.ToString().EndsWith(":session-1")),
            Arg.Is<TimeSpan>(t => t.TotalHours >= 23 && t.TotalHours <= 25),
            Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CanMakeLlmCallAsync_EmptySessionId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CanMakeLlmCallAsync(""));
    }

    [Fact]
    public async Task GetCallCountAsync_ReturnsZero_WhenKeyMissing()
    {
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisValue.Null));

        var count = await _sut.GetCallCountAsync("session-1");

        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task KeyFormat_UsesSessionScopedPrefix()
    {
        // The Redis key is load-bearing: the finops dashboard + alerting
        // all target this exact prefix. Guard against accidental drift.
        var key = SocraticCallBudget.BuildKey("abc-123");

        Assert.Equal("cena:socratic:calls:abc-123", key);
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
