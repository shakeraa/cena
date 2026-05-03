// =============================================================================
// Cena Platform — Redis Rate Limit Service Tests (RATE-001)
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.RateLimit;

public sealed class RedisRateLimitServiceTests
{
    [Fact]
    public async Task TryAcquireAsync_NewBucket_AllowsRequest()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)1), RedisResult.Create((RedisValue)59) }));

        var service = new RedisRateLimitService(redis, NullLogger<RedisRateLimitService>.Instance);
        var result = await service.TryAcquireAsync("student-1", "api", 60, 1);

        Assert.True(result.Allowed);
        Assert.Equal(59, result.RemainingTokens);
    }

    [Fact]
    public async Task TryAcquireAsync_ExhaustedBucket_DeniesRequest()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)0), RedisResult.Create((RedisValue)5000) }));

        var service = new RedisRateLimitService(redis, NullLogger<RedisRateLimitService>.Instance);
        var result = await service.TryAcquireAsync("student-1", "api", 60, 1);

        Assert.False(result.Allowed);
        Assert.True(result.RetryAfter > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task TryAcquireAsync_RedisError_FailsOpen()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns<Task<RedisResult>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var service = new RedisRateLimitService(redis, NullLogger<RedisRateLimitService>.Instance);
        var result = await service.TryAcquireAsync("student-1", "api", 60, 1);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task GetBucketStateAsync_ReturnsCurrentTokens()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        db.HashGetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue[]>())
            .Returns(new RedisValue[] { 42, 1234567890000 });

        var service = new RedisRateLimitService(redis, NullLogger<RedisRateLimitService>.Instance);
        var state = await service.GetBucketStateAsync("student-1", "api");

        Assert.Equal(42, state.RemainingTokens);
    }
}
