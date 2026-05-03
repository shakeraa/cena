// =============================================================================
// Cena Platform — Push Notification Rate Limiter Tests (PWA-BE-002)
// =============================================================================

using Cena.Actors.Notifications;
using StackExchange.Redis;
using NSubstitute;

namespace Cena.Actors.Tests.Notifications;

public sealed class PushNotificationRateLimiterTests
{
    [Fact]
    public async Task CanSendAsync_BelowLimits_ReturnsTrue()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        // Lua script returns {0, 0} — dayCount=0, weekCount=0
        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)0), RedisResult.Create((RedisValue)0) }));

        var limiter = new PushNotificationRateLimiter(redis);
        var result = await limiter.CanSendAsync("student-001");

        Assert.True(result);
    }

    [Fact]
    public async Task CanSendAsync_DailyLimitExceeded_ReturnsFalse()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        // Lua script returns {3, 0} — dayCount=3 (at limit)
        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)3), RedisResult.Create((RedisValue)0) }));

        var limiter = new PushNotificationRateLimiter(redis);
        var result = await limiter.CanSendAsync("student-001");

        Assert.False(result);
    }

    [Fact]
    public async Task CanSendAsync_WeeklyLimitExceeded_ReturnsFalse()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        // Lua script returns {0, 10} — weekCount=10 (at limit)
        db.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>())
            .Returns(RedisResult.Create(new RedisResult[] { RedisResult.Create((RedisValue)0), RedisResult.Create((RedisValue)10) }));

        var limiter = new PushNotificationRateLimiter(redis);
        var result = await limiter.CanSendAsync("student-001");

        Assert.False(result);
    }

    [Fact]
    public async Task RecordSentAsync_AddsToBothWindows()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase().Returns(db);

        var limiter = new PushNotificationRateLimiter(redis);
        await limiter.RecordSentAsync("student-001");

        await db.Received(2).SortedSetAddAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<double>());
        await db.Received(2).KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<TimeSpan?>());
    }
}
