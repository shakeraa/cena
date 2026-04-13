// =============================================================================
// Cena Platform — Redis Cost Circuit Breaker Tests (RATE-001 Tier 4)
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.RateLimit;

public sealed class RedisCostCircuitBreakerTests
{
    [Fact]
    public async Task IsOpenAsync_BelowThreshold_ReturnsFalse()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"500.0");

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        var isOpen = await cb.IsOpenAsync();

        Assert.False(isOpen);
    }

    [Fact]
    public async Task IsOpenAsync_AtThreshold_ReturnsTrue()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"1000.0");

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        var isOpen = await cb.IsOpenAsync();

        Assert.True(isOpen);
    }

    [Fact]
    public async Task IsOpenAsync_RedisError_FailsClosed()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        var isOpen = await cb.IsOpenAsync();

        Assert.False(isOpen);
    }

    [Fact]
    public async Task RecordSpendAsync_PositiveCost_IncrementsRedis()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        await cb.RecordSpendAsync(5.5);

        await db.Received(1).StringIncrementAsync(Arg.Any<RedisKey>(), 5.5);
        await db.Received(1).KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task RecordSpendAsync_ZeroCost_DoesNothing()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        await cb.RecordSpendAsync(0.0);

        await db.DidNotReceive().StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<double>());
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsUsedAndThreshold()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"250.0");

        var cb = new RedisCostCircuitBreaker(redis, new ConfigurationBuilder().Build(), NullLogger<RedisCostCircuitBreaker>.Instance);
        var status = await cb.GetStatusAsync();

        Assert.Equal(250.0, status.Used);
        Assert.Equal(1000.0, status.Threshold);
    }
}
