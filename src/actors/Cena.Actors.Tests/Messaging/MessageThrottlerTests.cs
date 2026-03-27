using Cena.Actors.Messaging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessageThrottlerTests
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;

    public MessageThrottlerTests()
    {
        _redis = Substitute.For<IConnectionMultiplexer>();
        _db = Substitute.For<IDatabase>();
        _redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);

        // Default: all counters return 0 (no sends yet)
        _db.StringGet(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);
    }

    private MessageThrottler CreateThrottler() => new(_redis);

    [Fact]
    public void Teacher_AllowedWhenUnderLimit()
    {
        var throttler = CreateThrottler();
        var result = throttler.Check("teacher-1", MessageRole.Teacher);
        Assert.True(result.Allowed);
    }

    [Fact]
    public void Teacher_BlockedAfterHourlyLimit()
    {
        // Simulate 30 hourly sends already recorded
        _db.StringGet(Arg.Is<RedisKey>(k => k.ToString().Contains(":hourly")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)30);

        var throttler = CreateThrottler();
        var blocked = throttler.Check("teacher-1", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public void Parent_BlockedAfterHourlyLimit()
    {
        _db.StringGet(Arg.Is<RedisKey>(k => k.ToString().Contains(":hourly")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)5);

        var throttler = CreateThrottler();
        Assert.False(throttler.Check("parent-1", MessageRole.Parent).Allowed);
    }

    [Fact]
    public void Teacher_BlockedAfterDailyLimit()
    {
        // Hourly is under limit, but daily is at limit
        _db.StringGet(Arg.Is<RedisKey>(k => k.ToString().Contains(":hourly")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)10);
        _db.StringGet(Arg.Is<RedisKey>(k => k.ToString().Contains(":daily")), Arg.Any<CommandFlags>())
            .Returns((RedisValue)100);

        var throttler = CreateThrottler();
        var blocked = throttler.Check("teacher-1", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public void Student_AlwaysBlocked()
    {
        var throttler = CreateThrottler();
        var result = throttler.Check("student-1", MessageRole.Student);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void System_AlwaysAllowed()
    {
        var throttler = CreateThrottler();
        Assert.True(throttler.Check("system", MessageRole.System).Allowed);
    }

    [Fact]
    public void RecordSend_IncrementsRedisCounters()
    {
        var throttler = CreateThrottler();

        throttler.RecordSend("teacher-1", MessageRole.Teacher);

        _db.Received(1).StringIncrement(
            Arg.Is<RedisKey>(k => k.ToString().Contains("teacher-1:hourly")),
            Arg.Any<long>(), Arg.Any<CommandFlags>());
        _db.Received(1).StringIncrement(
            Arg.Is<RedisKey>(k => k.ToString().Contains("teacher-1:daily")),
            Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public void RecordSend_Student_NoOp()
    {
        var throttler = CreateThrottler();

        throttler.RecordSend("student-1", MessageRole.Student);

        _db.DidNotReceive().StringIncrement(
            Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public void Reset_DeletesRedisKeys()
    {
        var throttler = CreateThrottler();

        throttler.Reset("parent-1");

        _db.Received(1).KeyDelete(
            Arg.Is<RedisKey>(k => k.ToString().Contains("parent-1:hourly")),
            Arg.Any<CommandFlags>());
        _db.Received(1).KeyDelete(
            Arg.Is<RedisKey>(k => k.ToString().Contains("parent-1:daily")),
            Arg.Any<CommandFlags>());
    }
}
