// =============================================================================
// Cena Platform — Redis Cost Budget Service Tests (RATE-001 Tier 3)
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.RateLimit;

public sealed class RedisCostBudgetServiceTests
{
    [Fact]
    public async Task TryChargeTenantAsync_WithinBudget_AllowsCharge()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<double>()).Returns(5.0);

        var config = new ConfigurationBuilder().Build();
        var service = new RedisCostBudgetService(redis, config, NullLogger<RedisCostBudgetService>.Instance);
        var result = await service.TryChargeTenantAsync("tenant-1", 5.0);

        Assert.True(result);
        await db.Received(2).KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task TryChargeTenantAsync_ExceedsTenantBudget_DeniesCharge()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        // Global passes, tenant fails
        db.StringIncrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("global")), Arg.Any<double>()).Returns(5.0);
        db.StringIncrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("tenant")), Arg.Any<double>()).Returns(101.0);

        var config = new ConfigurationBuilder().Build();
        var service = new RedisCostBudgetService(redis, config, NullLogger<RedisCostBudgetService>.Instance);
        var result = await service.TryChargeTenantAsync("tenant-1", 5.0);

        Assert.False(result);
        await db.Received(1).StringDecrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("global")), Arg.Any<double>());
        await db.Received(1).StringDecrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("tenant")), Arg.Any<double>());
    }

    [Fact]
    public async Task TryChargeTenantAsync_ExceedsGlobalBudget_DeniesCharge()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.StringIncrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("global")), Arg.Any<double>()).Returns(1001.0);

        var config = new ConfigurationBuilder().Build();
        var service = new RedisCostBudgetService(redis, config, NullLogger<RedisCostBudgetService>.Instance);
        var result = await service.TryChargeTenantAsync("tenant-1", 5.0);

        Assert.False(result);
        await db.Received(1).StringDecrementAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("global")), Arg.Any<double>());
    }

    [Fact]
    public async Task TryChargeTenantAsync_ZeroCost_AlwaysAllowed()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        var config = new ConfigurationBuilder().Build();
        var service = new RedisCostBudgetService(redis, config, NullLogger<RedisCostBudgetService>.Instance);
        var result = await service.TryChargeTenantAsync("tenant-1", 0.0);

        Assert.True(result);
        await db.DidNotReceive().StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<double>());
    }

    [Fact]
    public async Task GetTenantUsageAsync_ReturnsUsage()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.StringGetAsync(Arg.Any<RedisKey>()).Returns((RedisValue)"12.50");

        var config = new ConfigurationBuilder().Build();
        var service = new RedisCostBudgetService(redis, config, NullLogger<RedisCostBudgetService>.Instance);
        var usage = await service.GetTenantUsageAsync("tenant-1");

        Assert.Equal(12.5, usage.Used);
    }
}
