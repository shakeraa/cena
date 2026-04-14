// =============================================================================
// RDY-001: Redis Cost Circuit Breaker Tests
// Verifies fail-closed behavior when Redis is unavailable.
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.RateLimit;

public class RedisCostCircuitBreakerTests
{
    [Fact]
    public async Task IsOpenAsync_RedisUnavailable_ReturnsTrue_BlockingRequests()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromException<RedisValue>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));

        var config = new ConfigurationBuilder().Build();
        var breaker = new RedisCostCircuitBreaker(redis, config, NullLogger<RedisCostCircuitBreaker>.Instance);

        // Act
        var isOpen = await breaker.IsOpenAsync();

        // Assert
        Assert.True(isOpen);
    }

    [Fact]
    public async Task IsOpenAsync_RedisAvailable_UnderThreshold_ReturnsFalse()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("100.0"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:CostCircuitBreaker:DailyThresholdUsd"] = "1000"
            })
            .Build();
        var breaker = new RedisCostCircuitBreaker(redis, config, NullLogger<RedisCostCircuitBreaker>.Instance);

        // Act
        var isOpen = await breaker.IsOpenAsync();

        // Assert
        Assert.False(isOpen);
    }

    [Fact]
    public async Task IsOpenAsync_RedisAvailable_OverThreshold_ReturnsTrue()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("1500.0"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cena:CostCircuitBreaker:DailyThresholdUsd"] = "1000"
            })
            .Build();
        var breaker = new RedisCostCircuitBreaker(redis, config, NullLogger<RedisCostCircuitBreaker>.Instance);

        // Act
        var isOpen = await breaker.IsOpenAsync();

        // Assert
        Assert.True(isOpen);
    }
}
